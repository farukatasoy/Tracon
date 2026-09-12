using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>The request that opens a provider-hosted live voice session.</summary>
public sealed record LiveVoiceSessionCreateRequest
{
    /// <summary>Gets the agent session the conversation runs in.</summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    /// <summary>Gets the agent to talk to.</summary>
    [JsonPropertyName("agent")]
    public required string Agent { get; init; }

    /// <summary>Gets the media peer's SDP offer.</summary>
    [JsonPropertyName("sdp")]
    public required string Sdp { get; init; }

    /// <summary>Gets the requested output voice; <see langword="null"/> leaves the configured default.</summary>
    [JsonPropertyName("voice")]
    public string? Voice { get; init; }
}

/// <summary>The answer that opens a provider-hosted live voice session.</summary>
public sealed record LiveVoiceSessionCreateResponse
{
    /// <summary>Gets Tracon's identifier for the session.</summary>
    [JsonPropertyName("voiceSessionId")]
    public required Guid VoiceSessionId { get; init; }

    /// <summary>Gets the SDP answer to hand to the media peer.</summary>
    [JsonPropertyName("sdp")]
    public required string Sdp { get; init; }

    /// <summary>Gets the model the session is bound to.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// Gets whether the conversation's transcript will be written to the session's
    /// durable history.
    /// </summary>
    /// <remarks>
    /// Reported so that no recording happens silently. The user's audio is never
    /// stored, but the text of what they say is durable while this is
    /// <see langword="true"/>.
    /// </remarks>
    [JsonPropertyName("persistTranscript")]
    public required bool PersistTranscript { get; init; }
}

/// <summary>The status of a provider-hosted live voice session.</summary>
public sealed record LiveVoiceSessionStatusResponse
{
    /// <summary>Gets Tracon's identifier for the session.</summary>
    [JsonPropertyName("voiceSessionId")]
    public required Guid VoiceSessionId { get; init; }

    /// <summary>Gets the lifecycle state: <c>pending</c>, <c>active</c> or <c>ended</c>.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>Gets the model the session is bound to.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Gets when the session was created.</summary>
    [JsonPropertyName("startedAt")]
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets the billable duration the provider has reported so far.</summary>
    [JsonPropertyName("liveSeconds")]
    public decimal? LiveSeconds { get; init; }

    /// <summary>Gets how many delegations have become runs.</summary>
    [JsonPropertyName("turns")]
    public required int Turns { get; init; }
}

// 🚨 K-224's subprotocol exemption exists ONLY because a WebSocket handshake cannot
// carry an Authorization header. These are plain HTTP endpoints and the exemption is
// deliberately not borrowed here.
//
// The gate order matches the shipped conversation endpoint and SHARES its helpers, so
// a denial is byte for byte the answer an unreachable session gets (K-687).
/// <summary>The HTTP surface of the provider-hosted live voice layer.</summary>
/// <remarks>
/// These are plain HTTP endpoints and sit in the protected endpoint group. A denial
/// is written by the same helper the conversation endpoint uses, so a refused caller
/// cannot tell a forbidden session from a missing one.
/// </remarks>
internal static class LiveVoiceEndpoints
{
    /// <summary>Maps the live voice endpoints.</summary>
    /// <param name="builder">The protected endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapPost("/api/voice/live/sessions", CreateAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconLiveVoiceCreate")
            .WithTags("Tracon", "Voice")
            .WithSummary("Creates a provider-hosted live voice session.")
            .WithDescription(
                "Relays the media peer's SDP offer to the provider and returns its answer, so the raw API key " +
                "never reaches the browser. Tracon attaches a server-side control connection to the same " +
                "session and turns the work the model delegates into ordinary runs. " +
                "Returns 501 when no live voice provider is registered.")
            // 🚨 The response type is declared explicitly. These handlers write
            // through HttpContext rather than returning a TypedResults value, so
            // nothing infers the shape — and the OpenAPI document, the .NET client
            // and the TypeScript client are all generated from that metadata. Without
            // it the generated client method returns a bare Task and a caller cannot
            // reach the SDP answer at all.
            .Produces<LiveVoiceSessionCreateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        builder.MapDelete("/api/voice/live/sessions/{voiceSessionId:guid}", CloseAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconLiveVoiceClose")
            .WithTags("Tracon", "Voice")
            .WithSummary("Closes a live voice session and writes its record.")
            .WithDescription(
                "Cancels any delegation still running, writes the session record with its measured " +
                "duration and cost, and drops the provider connection. A session another tenant owns " +
                "answers 404, byte for byte the answer a session that does not exist gets.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapGet("/api/voice/live/sessions/{voiceSessionId:guid}", GetAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("TraconLiveVoiceStatus")
            .WithTags("Tracon", "Voice")
            .WithSummary("Reports a live voice session's state and measurement.")
            .WithDescription(
                "The state is 'pending' until media is observed, then 'active', then 'ended'. " +
                "The duration is the provider's own number: Tracon does not carry a live " +
                "session's media and does not invent a duration.")
            .Produces<LiveVoiceSessionStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task CreateAsync(HttpContext context, LiveVoiceSessionCreateRequest request)
    {
        var services = context.RequestServices;
        var launcher = services.GetRequiredService<LiveVoiceSessionLauncher>();

        if (!launcher.IsReady)
        {
            await VoiceEndpointGates.WriteProblemAsync(
                context,
                StatusCodes.Status501NotImplemented,
                "Live voice provider not configured",
                "A live voice session needs a registered ILiveVoiceProvider. Call `UseOpenAILive(...)` " +
                "(and `UseOpenAI(...)` before it) to enable one.").ConfigureAwait(false);

            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Sdp))
        {
            await VoiceEndpointGates.WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "SDP offer required",
                "The body must carry the media peer's SDP offer in the 'sdp' field.").ConfigureAwait(false);

            return;
        }

        var tenantContext = services.GetRequiredService<ITenantContext>();
        var tenantId = tenantContext.TenantId;

        if (!await VoiceEndpointGates
                .OwnsSessionAsync(services, request.SessionId, tenantId, context.RequestAborted)
                .ConfigureAwait(false))
        {
            await VoiceEndpointGates.WriteSessionNotFoundAsync(context, request.SessionId).ConfigureAwait(false);

            return;
        }

        if (await VoiceEndpointGates
                .DeniesSessionAsync(context, request.SessionId, tenantContext)
                .ConfigureAwait(false))
        {
            await VoiceEndpointGates.WriteSessionNotFoundAsync(context, request.SessionId).ConfigureAwait(false);

            return;
        }

        var actor = services.GetRequiredService<IAuditActorResolver>();

        LiveVoiceLaunchResult result;

        try
        {
            result = await launcher
                .LaunchAsync(
                    new LiveVoiceSessionRequest
                    {
                        TenantId = tenantId,
                        SessionId = request.SessionId,
                        AgentName = request.Agent,
                        CreatedBy = actor.Resolve(),
                    },
                    request.Sdp,
                    request.Voice,
                    context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TraconException or HttpRequestException or TimeoutException)
        {
            // 🚨 HttpRequestException belongs here as much as TraconException:
            // the outbound address policy refuses a connection through the socket
            // handler, so its verdict arrives WRAPPED in a transport exception. A
            // catch that named only TraconException let a routine, caller-
            // triggered refusal escape as an unhandled 500.
            await VoiceEndpointGates.WriteProblemAsync(
                context,
                StatusCodes.Status502BadGateway,
                "Live voice session could not be created",
                Describe(exception)).ConfigureAwait(false);

            return;
        }

        if (result.Failure is { } failure)
        {
            var (status, title) = failure switch
            {
                // 🚨 The limit answers BEFORE the provider was called, so a refused
                // request never leaves a billed session behind.
                LiveVoiceLaunchFailure.LimitReached
                    => (StatusCodes.Status429TooManyRequests, "Concurrent live voice session limit reached"),
                LiveVoiceLaunchFailure.AgentNotFound
                    => (StatusCodes.Status404NotFound, "Agent not found"),
                LiveVoiceLaunchFailure.ProviderMissing
                    => (StatusCodes.Status501NotImplemented, "Live voice provider not configured"),
                _ => (StatusCodes.Status400BadRequest, "The live voice session could not be opened"),
            };

            await VoiceEndpointGates
                .WriteProblemAsync(context, status, title, result.Detail ?? title)
                .ConfigureAwait(false);

            return;
        }

        var host = result.Host!;

        await Results.Ok(new LiveVoiceSessionCreateResponse
        {
            VoiceSessionId = host.Id,
            Sdp = result.Handle!.SdpAnswer,
            Model = host.Model,
            PersistTranscript = launcher.Options.PersistTranscript,
        }).ExecuteAsync(context).ConfigureAwait(false);
    }

    /// <summary>Unwraps a transport failure down to the reason worth reporting.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The message.</returns>
    /// <remarks>
    /// The outbound address policy's verdict is the inner exception of the transport
    /// failure that carries it, and that verdict names the setting an operator has to
    /// change. Reporting only the outer message would leave them with "an error
    /// occurred" and nothing to act on.
    /// </remarks>
    private static string Describe(Exception exception)
        => exception.InnerException is TraconException inner
            ? inner.Message
            : exception.Message;

    private static async Task CloseAsync(HttpContext context, Guid voiceSessionId)
    {
        var services = context.RequestServices;
        var launcher = services.GetRequiredService<LiveVoiceSessionLauncher>();
        var tenantId = services.GetRequiredService<ITenantContext>().TenantId;

        if (launcher.Registry.Find(voiceSessionId, tenantId) is not { } host)
        {
            // 🚨 Another tenant's session is answered exactly as a missing one.
            await VoiceEndpointGates
                .WriteSessionNotFoundAsync(context, voiceSessionId.ToString("D"))
                .ConfigureAwait(false);

            return;
        }

        await launcher.Registry.CloseAsync(host, VoiceSessionEndReason.Client).ConfigureAwait(false);

        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task GetAsync(HttpContext context, Guid voiceSessionId)
    {
        var services = context.RequestServices;
        var launcher = services.GetRequiredService<LiveVoiceSessionLauncher>();
        var tenantId = services.GetRequiredService<ITenantContext>().TenantId;

        if (launcher.Registry.Find(voiceSessionId, tenantId) is not { } host)
        {
            await VoiceEndpointGates
                .WriteSessionNotFoundAsync(context, voiceSessionId.ToString("D"))
                .ConfigureAwait(false);

            return;
        }

        await Results.Ok(new LiveVoiceSessionStatusResponse
        {
            VoiceSessionId = host.Id,
            State = host.State switch
            {
                LiveVoiceSessionState.Pending => "pending",
                LiveVoiceSessionState.Active => "active",
                _ => "ended",
            },
            Model = host.Model,
            StartedAt = host.StartedAt,
            LiveSeconds = host.LiveSeconds,
            Turns = host.Turns,
        }).ExecuteAsync(context).ConfigureAwait(false);
    }
}
