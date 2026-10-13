using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Tracon;

/// <summary>
/// Voice provider health and voice list endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There are two speech paths and their metering behavior DIFFERS.</strong>
/// The agent's <c>speak</c> tool runs inside a run; its metering is written to
/// the <c>tool_invocations</c> row. The <c>POST /api/voice/speak</c> endpoint
/// here, however, is an <em>operator action</em> and is OUTSIDE a run:
/// <c>tool_invocations.run_id</c> is a required foreign key, so a metering row
/// cannot be written without a run.
/// </para>
/// <para>
/// The cost is still not invisible: the endpoint <strong>returns the metered
/// character count and amount in the response</strong>, and the frontend shows
/// this. The endpoint also requires the <c>Operator</c> role and respects the
/// same character limit as the tool. If a persistent record is required, use
/// the agent's tool instead.
/// </para>
/// <para>
/// The voice provider is DELIBERATELY absent from the <c>/api/models/health</c>
/// output: it is not an <c>IModelProvider</c>, and combining the two sources
/// into one list would make the circuit breaker and model catalog behave
/// incorrectly.
/// </para>
/// <para>
/// Services are resolved optionally: if <c>UseVoice()</c> was not called, the
/// endpoints return an explicit <c>501</c> instead of <c>404</c> — so a missing
/// configuration is not confused with a wrong address.
/// </para>
/// <para>
/// Speaking and listing voices go through <see cref="ISpeechProviderResolver"/>:
/// the request may name a registered synthesizer, and the tenant's egress policy and
/// own credential are checked before the provider is called. A refusal is <c>403</c>,
/// an unknown name <c>400</c>, a provider failure <c>502</c>.
/// </para>
/// </remarks>
internal static class VoiceEndpoints
{
    /// <summary>Maps the voice endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/voice/health", CheckHealthAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("TraconVoiceHealth")
            .WithTags("Tracon", "Voice")
            .WithSummary("Checks the voice provider's availability.")
            .WithDescription("The check does not incur cost: no speech is generated, only the available voices are read.");

        builder.MapGet("/api/voice/voices", ListVoicesAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconVoiceList")
            .WithTags("Tracon", "Voice")
            .WithSummary("Lists the available voices.")
            .Produces<IReadOnlyList<VoiceDescriptor>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .WithDescription(
                "The list comes from the speech provider, not from Tracon; the identifiers it " +
                "returns are the values the speak endpoint and the agent's 'speak' tool accept. " +
                "The optional 'provider' query parameter names a synthesizer registered with " +
                "AddSpeechSynthesizer; without it the default synthesizer is used. Returns 400 for " +
                "an unknown name, and 403 when the tenant's egress policy does not allow the " +
                "provider or the tenant has its own credential that the provider cannot use. When " +
                "no default synthesizer is registered (UseVoice() was never called), the response " +
                "is 501 rather than 404, so a missing configuration is not mistaken for a wrong address.");

        builder.MapGet("/api/voice/providers", ListProviders)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconVoiceProviders")
            .WithTags("Tracon", "Voice")
            .WithSummary("Lists the registered speech providers.")
            .WithDescription(
                "Each entry is a synthesizer or a transcriber: its registration name (null for the " +
                "default), its kind, the provider name a tenant's egress policy and provider bindings " +
                "use, and whether it can call the service with a tenant's own credential. Reading the " +
                "list calls no provider. An empty list means no speech provider is registered.");

        builder.MapGet("/api/voice/sessions", ListSessionsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconVoiceSessions")
            .WithTags("Tracon", "Voice")
            .WithSummary("Lists the summary record of real-time speech connections.")
            .WithDescription(
                "The record does NOT contain audio: it only carries duration, turn count, and " +
                "metering. If the speech layer is not enabled, the list is empty.");

        builder.MapPost("/api/voice/speak", SpeakAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconVoiceSpeak")
            .WithTags("Tracon", "Voice")
            .WithSummary("Synthesizes speech from text and saves it as an attachment.")
            .Accepts<SpeakRequest>("application/json")
            .Produces<SpeakResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithDescription(
                "This is an operator action and is NOT tied to a run; the metering is not " +
                "written to tool_invocations, it is returned in the response. If persistent " +
                "metering is required, use the agent's `speak` tool. The optional 'provider' " +
                "field names a synthesizer registered with AddSpeechSynthesizer; without it the " +
                "default synthesizer is used. Returns 400 for an unknown name, 403 when the " +
                "tenant's egress policy does not allow the provider or the tenant has its own " +
                "credential that the provider cannot use (the call is not made on the setup " +
                "credential), and 501 when no default synthesizer is registered.");
    }

    private static async Task<Results<Ok<SpeakResponse>, ProblemHttpResult>> SpeakAsync(
        HttpContext httpContext,
        [FromServices] ISpeechProviderResolver speechProviders,
        [FromServices] IVoicePricingReader? pricing,
        AttachmentTypeGuard guard,
        IAttachmentStore store,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<SpeakRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return TypedResults.Problem(
                title: "Text empty",
                detail: "'text' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // F-298: the tenant's checks run before the provider is reached, and before
        // the limit is read: the limit belongs to the synthesizer the name selects.
        var (resolved, refusal) = await ResolveSynthesizerAsync(speechProviders, request.Provider, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        var synthesizer = resolved!;

        // 🚨 SpeakTool (the agent's tool call) already enforced this limit; this HTTP
        // operator endpoint (called directly, independent of the agent) was separate
        // and did not honor the XML doc's claim to "respect the same limit".
        var maxCharacters = synthesizer.MaxCharactersPerRequest;

        if (request.Text.Length > maxCharacters)
        {
            return TypedResults.Problem(
                title: "Text too long",
                detail: $"The text is {request.Text.Length} characters; the limit is {maxCharacters}. " +
                        "Shorten the text or raise the speech synthesizer's limit " +
                        "('Tracon:Voice:MaxCharactersPerRequest' for the built-in client).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        SpeechAudio audio;

        try
        {
            audio = await synthesizer
                .SynthesizeAsync(
                    new SpeechRequest
                    {
                        Text = request.Text,
                        VoiceId = request.VoiceId,
                        IncludeTimestamps = request.IncludeTimestamps,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TraconException exception)
        {
            // The message only carries the status code and reason; the provider's body
            // (which echoes the request and sometimes a fragment of the key) never passes through.
            return TypedResults.Problem(
                title: "Speech could not be generated",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }

        var validation = guard.Validate(audio.Data.Span);

        if (!validation.IsValid)
        {
            return TypedResults.Problem(
                title: "Generated speech could not be saved",
                detail: validation.Error,
                statusCode: StatusCodes.Status502BadGateway);
        }

        var descriptor = await store.SaveAsync(
            new AttachmentContent
            {
                TenantId = tenantContext.TenantId,

                // The session id is REQUIRED: if left blank, the retention policy
                // treats the attachment as orphaned and deletes it.
                SessionId = request.SessionId,
                // F-336: the extension follows the DETECTED type; a fixed '.mp3'
                // misnamed OGG and WAV output.
                FileName = AttachmentFileNames.For("speech", validation.MediaType!, DateTimeOffset.UtcNow),
                MediaType = validation.MediaType!,
                Data = audio.Data,
                CreatedBy = actorResolver.Resolve(),
            },
            cancellationToken).ConfigureAwait(false);

        var characters = audio.CharactersBilled ?? request.Text.Length;

        return TypedResults.Ok(new SpeakResponse
        {
            Attachment = descriptor,
            Characters = characters,
            IsEstimated = audio.UsageSource != SpeechUsageSource.Provider,
            Cost = Price(pricing, synthesizer.ProviderName, audio.ModelId, request.Provider is null, characters),
            Currency = pricing?.Currency,
            Alignment = audio.Alignment,
        });
    }

    /// <summary>Prices a synthesis by the provider and model that produced it.</summary>
    /// <remarks>
    /// The default synthesizer keeps the older lookup as its fallback: a pricing
    /// reader written before the provider-aware member answers only
    /// <see cref="IVoicePricingReader.ForCharacters"/>, and the default synthesizer is
    /// the one that member describes. A named synthesizer never borrows the default's
    /// price.
    /// </remarks>
    private static decimal? Price(
        IVoicePricingReader? pricing,
        string providerName,
        string? modelId,
        bool isDefault,
        int characters)
    {
        if (pricing is null)
        {
            return null;
        }

        var cost = pricing.ForSynthesis(providerName, modelId, characters);

        return cost is null && isDefault ? pricing.ForCharacters(characters) : cost;
    }

    /// <summary>
    /// Resolves the synthesizer a request names through the tenant's checks, or the
    /// problem that answers the request instead.
    /// </summary>
    private static async ValueTask<(ISpeechSynthesizer? Synthesizer, ProblemHttpResult? Problem)> ResolveSynthesizerAsync(
        ISpeechProviderResolver speechProviders,
        string? provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var synthesizer = await speechProviders.GetSynthesizerAsync(provider, cancellationToken).ConfigureAwait(false);

            return (synthesizer, null);
        }
        catch (SpeechProviderNotFoundException) when (provider is null)
        {
            return (null, NotConfigured());
        }
        catch (SpeechProviderNotFoundException exception)
        {
            return (null, TypedResults.Problem(
                title: "Unknown speech provider",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
        catch (TenantProviderRefusedException exception)
        {
            // The tenant's own rule, not a provider failure: 403, as for live voice
            // and image generation. The message names the tenant and the provider only.
            return (null, TypedResults.Problem(
                title: "Speech provider not available for this tenant",
                detail: exception.Message,
                statusCode: StatusCodes.Status403Forbidden));
        }
    }

    private static Ok<IReadOnlyList<SpeechProviderDescriptor>> ListProviders(
        [FromServices] ISpeechProviderResolver speechProviders)
        => TypedResults.Ok(speechProviders.Providers);

    /// <summary>Lists the speech session records.</summary>
    /// <remarks>
    /// The <see cref="IVoiceSessionStore"/> store is resolved optionally: if the
    /// speech layer was not enabled, it is not registered, and the endpoint
    /// returns an <strong>empty list</strong> instead of <c>501</c>.
    /// the list endpoint reports the absence of data, not the presence of a
    /// capability; the frontend panel renders without error.
    /// </remarks>
    private static async Task<Ok<IReadOnlyList<VoiceSessionRecord>>> ListSessionsAsync(
        [FromServices] IVoiceSessionStore? store,
        ITenantContext tenantContext,
        [FromQuery] string? agentName,
        [FromQuery] string? sessionId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        if (store is null)
        {
            return TypedResults.Ok<IReadOnlyList<VoiceSessionRecord>>([]);
        }

        var records = await store.QueryAsync(
            tenantContext.TenantId,
            new VoiceSessionQuery
            {
                AgentName = agentName,
                SessionId = sessionId,
                Skip = Math.Max(skip ?? 0, 0),
                Take = Math.Clamp(take ?? 50, 1, 200),
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(records);
    }

    private static async Task<Results<Ok<VoiceHealth>, ProblemHttpResult>> CheckHealthAsync(
        [FromServices] IVoiceHealthCheck? healthCheck,
        CancellationToken cancellationToken)
    {
        if (healthCheck is null)
        {
            return NotConfigured();
        }

        var health = await healthCheck.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(health);
    }

    private static async Task<Results<Ok<IReadOnlyList<VoiceDescriptor>>, ProblemHttpResult>> ListVoicesAsync(
        [FromServices] ISpeechProviderResolver speechProviders,
        [FromQuery] string? provider,
        CancellationToken cancellationToken)
    {
        var (synthesizer, refusal) = await ResolveSynthesizerAsync(speechProviders, provider, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        var voices = await synthesizer!.ListVoicesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(voices);
    }

    private static ProblemHttpResult NotConfigured()
        => TypedResults.Problem(
            title: "Voice provider not configured",
            detail: "Add the `Tracon.Voice` package and call `UseVoice(...)` to enable voice.",
            statusCode: StatusCodes.Status501NotImplemented);
}
