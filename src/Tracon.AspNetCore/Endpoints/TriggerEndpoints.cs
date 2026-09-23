using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Inbound trigger definitions (admin CRUD) and the unauthenticated accept
/// endpoint that queues a run from an external, signed event.
/// </summary>
/// <remarks>
/// <para>
/// The accept endpoint (<see cref="MapAccept"/>) is mapped into its OWN,
/// UNAUTHENTICATED group by <c>TraconEndpointRouteBuilderExtensions</c> —
/// the same pattern as <c>MapMcpOAuthCallback</c>: a caller that owns
/// no Tracon bearer token still needs to reach this route. Identity here
/// is an HMAC signature over the body, verified by
/// <see cref="InboundTriggerDispatcher"/> itself, not by an ASP.NET Core filter.
/// </para>
/// <para>
/// It is written to the audit trail BEFORE calling
/// <see cref="IInboundTriggerStore.UpsertAsync"/>/<see cref="IInboundTriggerStore.DeleteAsync"/>:
/// the same exception ("a trigger definition that cannot be written
/// to the audit trail is not applied") — the same pattern
/// <c>ApprovalEndpoints.DecideAsync</c> uses, not the routine,
/// failure-swallowing <c>AuditRecorder.WriteAsync</c> most admin endpoints use.
/// </para>
/// </remarks>
internal static class TriggerEndpoints
{
    /// <summary>Maps the trigger definition (admin) endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/triggers", ListAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("TraconListInboundTriggers")
            .WithTags("Tracon", "Triggers")
            .WithSummary("Lists a tenant's inbound triggers.")
            .WithDescription(
                "The response carries no signing secret value, only the configuration key's " +
                "NAME and whether it currently resolves ('resolved') — the diagnosis path for " +
                "'I set the secret but signatures still fail'.");

        builder.MapGet("/api/triggers/{name}", GetAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("TraconGetInboundTrigger")
            .WithTags("Tracon", "Triggers")
            .WithSummary("Gets a single inbound trigger.")
            .WithDescription("An unknown name returns 404.");

        builder.MapPut("/api/triggers/{name}", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("TraconSaveInboundTrigger")
            .WithTags("Tracon", "Triggers")
            .WithSummary("Creates or updates an inbound trigger.")
            .Accepts<InboundTriggerSaveRequest>("application/json")
            .WithDescription(
                "'signingSecretConfigurationName' carries only the configuration key's NAME, " +
                "never its value; the name must be under the configured allowed prefix and " +
                "inside the tenant's own key space — '{prefix}{tenantId}:...'; a flat name " +
                "directly under the prefix belongs to the default tenant (400 otherwise). " +
                "'payloadPath' is required when 'payloadMode' is 'path'.");

        builder.MapDelete("/api/triggers/{name}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("TraconDeleteInboundTrigger")
            .WithTags("Tracon", "Triggers")
            .WithSummary("Deletes an inbound trigger.")
            .WithDescription("After deletion, the accept endpoint returns 404 for this name. An unknown name returns 404.");
    }

    /// <summary>Maps the unauthenticated accept endpoint.</summary>
    /// <param name="builder">The unauthenticated endpoint group.</param>
    /// <param name="prefix">The path prefix, used to build the <c>Location</c> response.</param>
    public static void MapAccept(IEndpointRouteBuilder builder, string prefix)
    {
        // 🚨 A lambda, not a direct method group: AcceptAsync needs `prefix`,
        // a plain string captured from THIS method's own parameter — minimal
        // API cannot supply it through [FromServices] (it is not a DI
        // registration) or through the route (it is not a route segment). The
        // same pattern AgentEndpoints uses to hand RunQueuedAsync its prefix.
        builder.MapPost(
                "/api/triggers/{tenantId}/{name}",
                (string tenantId,
                    string name,
                    HttpContext httpContext,
                    [FromServices] InboundTriggerDispatcher dispatcher,
                    [FromServices] QuotaEnforcer? quotaEnforcer,
                    [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
                    [FromServices] IRunAttributionContext? attributionContext,
                    [FromServices] IOptionsMonitor<TraconInboundTriggerOptions> options,
                    CancellationToken cancellationToken)
                    => AcceptAsync(tenantId, name, httpContext, dispatcher, quotaEnforcer, runAuthorizationHandler, attributionContext, options, prefix, cancellationToken))
            .WithName("TraconAcceptInboundTrigger")
            .WithTags("Tracon", "Triggers")
            .WithSummary("Accepts a signed external event and queues a run.")
            .WithDescription(
                "No bearer token: the caller authenticates with an HMAC signature over the raw " +
                "body ('X-Tracon-Timestamp' + 'X-Tracon-Signature', the same headers " +
                "outbound webhooks send, in the reverse direction). The event is " +
                "ALWAYS queued and this ALWAYS returns 202 — there is no synchronous mode; a " +
                "long model call would otherwise fail the caller's own webhook timeout. A missing " +
                "or wrong signature, an unknown trigger, and a disabled trigger all return the " +
                "SAME generic response so a caller cannot enumerate trigger names. If a registered " +
                "IRunAuthorizationHandler denies the target run, the response is 403, before the " +
                "quota check.")
            .Produces<InboundTriggerAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<Ok<IReadOnlyList<InboundTriggerResponse>>> ListAsync(
        [FromServices] IInboundTriggerStore store,
        [FromServices] InboundTriggerSecretResolver resolver,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var triggers = await store.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<InboundTriggerResponse>>(
            [.. triggers.Select(trigger => Describe(trigger, resolver))]);
    }

    private static async Task<Results<Ok<InboundTriggerResponse>, ProblemHttpResult>> GetAsync(
        string name,
        [FromServices] IInboundTriggerStore store,
        [FromServices] InboundTriggerSecretResolver resolver,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var trigger = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        return trigger is null ? TriggerNotFound(name) : TypedResults.Ok(Describe(trigger, resolver));
    }

    private static async Task<Results<Ok<InboundTriggerResponse>, ProblemHttpResult>> SaveAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IInboundTriggerStore store,
        [FromServices] InboundTriggerSecretResolver resolver,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<InboundTriggerSaveRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (string.IsNullOrWhiteSpace(request.TargetName))
        {
            return Invalid("'targetName' is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SigningSecretConfigurationName))
        {
            return Invalid("'signingSecretConfigurationName' is required.");
        }

        try
        {
            // Defense in two layers (the same rationale as phase 65's tenant
            // provider bindings, section 65.2): the resolver validates the
            // SAME prefix and tenant rule again while resolving at request time.
            resolver.ValidateKeyName(tenants.TenantId, request.SigningSecretConfigurationName);
        }
        catch (TraconException ex)
        {
            return Invalid(ex.Message);
        }

        if (request.PayloadMode == InboundTriggerPayloadMode.Path && string.IsNullOrWhiteSpace(request.PayloadPath))
        {
            return Invalid("'payloadPath' is required when 'payloadMode' is 'path'.");
        }

        var existing = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var trigger = new InboundTrigger
        {
            Id = existing?.Id ?? Guid.Empty,
            TenantId = tenants.TenantId,
            Name = name,
            TargetKind = request.TargetKind,
            TargetName = request.TargetName,
            SigningSecretConfigurationName = request.SigningSecretConfigurationName,
            PayloadMode = request.PayloadMode,
            PayloadPath = request.PayloadMode == InboundTriggerPayloadMode.Path ? request.PayloadPath : null,
            Enabled = request.Enabled,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        var logger = loggerFactory.CreateLogger("Tracon.TriggerEndpoints");

        await AuditRecorder.WriteOrThrowAsync(
            auditLog,
            actorResolver.Resolve(),
            logger,
            metrics,
            tenants.TenantId,
            action: existing is null ? "trigger.create" : "trigger.update",
            entity: $"trigger:{name}",
            before: null,
            after: DescribeForAudit(trigger),
            refusal: $"'trigger:{name}' was not saved",
            timeProvider: null,
            cancellationToken).ConfigureAwait(false);

        var saved = await store.UpsertAsync(trigger, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(Describe(saved, resolver));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name,
        [FromServices] IInboundTriggerStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        var existing = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return TriggerNotFound(name);
        }

        await AuditRecorder.WriteOrThrowAsync(
            auditLog,
            actorResolver.Resolve(),
            loggerFactory.CreateLogger("Tracon.TriggerEndpoints"),
            metrics,
            tenants.TenantId,
            action: "trigger.delete",
            entity: $"trigger:{name}",
            before: null,
            after: DescribeForAudit(existing),
            refusal: $"'trigger:{name}' was not deleted",
            timeProvider: null,
            cancellationToken).ConfigureAwait(false);

        await store.DeleteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> AcceptAsync(
        string tenantId,
        string name,
        HttpContext httpContext,
        InboundTriggerDispatcher dispatcher,
        QuotaEnforcer? quotaEnforcer,
        IRunAuthorizationHandler? runAuthorizationHandler,
        IRunAttributionContext? attributionContext,
        IOptionsMonitor<TraconInboundTriggerOptions> options,
        string prefix,
        CancellationToken cancellationToken)
    {
        tenantId = HttpTenantContext.NormalizeRouteTenantId(tenantId);

        var (body, sizeError) = await ReadBoundedBodyAsync(
            httpContext, options.CurrentValue.MaxBodyBytes, cancellationToken).ConfigureAwait(false);

        if (sizeError is not null)
        {
            return sizeError;
        }

        var timestampHeader = httpContext.Request.Headers.TryGetValue(WebhookSigner.TimestampHeader, out var timestampValues)
            ? timestampValues.ToString()
            : null;

        var signatureHeader = httpContext.Request.Headers.TryGetValue(WebhookSigner.SignatureHeader, out var signatureValues)
            ? signatureValues.ToString()
            : null;

        var validation = await dispatcher
            .ValidateAsync(tenantId, name, body!, timestampHeader, signatureHeader, cancellationToken)
            .ConfigureAwait(false);

        if (validation.Outcome != InboundTriggerOutcome.Valid)
        {
            return DescribeOutcome(validation);
        }

        var validated = validation.Validated!;
        var triggerTenant = new FixedTenantContext(validated.Trigger.TenantId);

        // Same 403 shape as every other run-starting endpoint (phase 139,
        // F-185): a trigger cannot bypass the installation's run
        // authorization policy just because it has no bearer token.
        if (await RunAuthorizationGate
                .CheckRunAsync(runAuthorizationHandler, triggerTenant, validated.Trigger.TargetName, sessionId: null, attributionContext, cancellationToken)
                .ConfigureAwait(false) is { } authorizationResult)
        {
            await dispatcher.ReleaseAsync(validated, cancellationToken).ConfigureAwait(false);

            return authorizationResult;
        }

        // Same 429 shape as every other run-starting endpoint (K-394's
        // precedent): a trigger cannot bypass the tenant's quota just because
        // it has no bearer token.
        var quotaResult = await QuotaGate
            .CheckAsync(quotaEnforcer, triggerTenant, validated.Trigger.TargetName, httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (quotaResult is not null)
        {
            await dispatcher.ReleaseAsync(validated, cancellationToken).ConfigureAwait(false);

            return quotaResult;
        }

        InboundTriggerDispatchResult dispatched;

        try
        {
            dispatched = await dispatcher.EnqueueAsync(validated, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The idempotency reservation ValidateAsync made is never
            // completed on success (see InboundTriggerDispatcher's remarks);
            // if queuing itself fails, it must be released here too — the
            // same rationale as the quota-rejection branch above — so a
            // legitimate retry after a transient failure is not mistaken for
            // a replay forever.
            await dispatcher.ReleaseAsync(validated, CancellationToken.None).ConfigureAwait(false);

            throw;
        }

        var location = dispatched.RunId is { } runId
            ? $"{prefix}/api/runs/{runId}"
            : $"{prefix}/api/jobs/{dispatched.JobId}";

        httpContext.Response.Headers.Location = location;

        return TypedResults.Accepted(location, new InboundTriggerAcceptedResponse
        {
            RunId = dispatched.RunId,
            JobId = dispatched.JobId,
            Location = location,
            EventsLocation = dispatched.RunId is not null ? $"{location}/events" : null,
        });
    }

    /// <summary>
    /// Reads the request body up to <paramref name="maxBodyBytes"/>. Does NOT
    /// trust <c>Content-Length</c> — the stream itself is bounded instead.
    /// </summary>
    private static async Task<(string? Body, IResult? Error)> ReadBoundedBodyAsync(
        HttpContext httpContext,
        int maxBodyBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var readBuffer = new byte[8192];
        var total = 0;

        while (true)
        {
            var read = await httpContext.Request.Body.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            total += read;

            if (total > maxBodyBytes)
            {
                return (null, TypedResults.Problem(
                    title: "Payload too large",
                    detail: $"The request body may be at most {maxBodyBytes} bytes.",
                    statusCode: StatusCodes.Status413PayloadTooLarge));
            }

            buffer.Write(readBuffer, 0, read);
        }

        return (Encoding.UTF8.GetString(buffer.ToArray()), null);
    }

    /// <summary>
    /// Maps a failed <see cref="InboundTriggerValidationResult"/> to its HTTP
    /// response. <see cref="InboundTriggerOutcome.Unauthorized"/> covers an
    /// unknown tenant (no silent fallback), an unknown or disabled
    /// trigger name, AND every signature/timestamp failure — one generic
    /// <c>401</c> body for all of them, so a caller without a
    /// valid secret cannot enumerate trigger names by comparing responses.
    /// </summary>
    private static ProblemHttpResult DescribeOutcome(InboundTriggerValidationResult validation)
        => validation.Outcome switch
        {
            InboundTriggerOutcome.RateLimited => RateLimited(),

            InboundTriggerOutcome.Unauthorized => TypedResults.Problem(
                title: "Signature verification failed",
                detail: "The request signature is missing, does not match, or its timestamp is outside the accepted window.",
                statusCode: StatusCodes.Status401Unauthorized),

            InboundTriggerOutcome.Replayed => TypedResults.Problem(
                title: "Request already processed",
                detail: "A request with this signature was already accepted.",
                statusCode: StatusCodes.Status409Conflict),

            InboundTriggerOutcome.InvalidPayload => TypedResults.Problem(
                title: "Invalid request body",
                detail: validation.ErrorDetail,
                statusCode: StatusCodes.Status400BadRequest),

            _ => TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };

    private static ProblemHttpResult RateLimited()
        => TypedResults.Problem(
            title: "Rate limit exceeded",
            detail: "This trigger's per-minute request limit was exceeded.",
            statusCode: StatusCodes.Status429TooManyRequests);

    private static InboundTriggerResponse Describe(InboundTrigger trigger, InboundTriggerSecretResolver resolver)
        => new()
        {
            Name = trigger.Name,
            TargetKind = trigger.TargetKind,
            TargetName = trigger.TargetName,
            SigningSecretConfigurationName = trigger.SigningSecretConfigurationName,
            Resolved = TryResolve(trigger, resolver),
            PayloadMode = trigger.PayloadMode,
            PayloadPath = trigger.PayloadPath,
            Enabled = trigger.Enabled,
            CreatedAt = trigger.CreatedAt,
            UpdatedAt = trigger.UpdatedAt,
        };

    private static bool TryResolve(InboundTrigger trigger, InboundTriggerSecretResolver resolver)
    {
        try
        {
            return resolver.Resolve(trigger) is not null;
        }
        catch (TraconException)
        {
            // The prefix was valid when saved but the allowed prefix setting
            // changed since; reported the same way as a missing value.
            return false;
        }
    }

    /// <summary>Summarizes a trigger for the audit trail.</summary>
    /// <remarks>
    /// NO secret value — only the configuration key's name. The
    /// JSON property is deliberately named <c>secretConfigKeyName</c>, not
    /// <c>signingSecretConfigurationName</c>: <see cref="AuditSecretFilter"/>
    /// blanket-redacts any property whose name contains "secret" regardless
    /// of its value, and the configuration key's name is exactly the
    /// diagnostic detail the audit trail exists to keep (the same
    /// workaround <c>TenantProviderEndpoints.DescribeForAudit</c> uses for
    /// "apikey").
    /// </remarks>
    private static string DescribeForAudit(InboundTrigger trigger)
        => AuditPayload.Write(writer =>
        {
            writer.WriteString("targetKind", trigger.TargetKind.ToString());
            writer.WriteString("targetName", trigger.TargetName);
            writer.WriteString("configKeyName", trigger.SigningSecretConfigurationName);
            writer.WriteBoolean("enabled", trigger.Enabled);
        });

    private static ProblemHttpResult TriggerNotFound(string name)
        => TypedResults.Problem(
            title: "Trigger not found",
            detail: $"There is no trigger named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult Invalid(string detail)
        => TypedResults.Problem(title: "Invalid request", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>A fixed-tenant <see cref="ITenantContext"/> for the accept endpoint's quota check.</summary>
    /// <remarks>
    /// The accept endpoint has no ambient tenant (it is unauthenticated and
    /// identifies the tenant from the route, then the trigger record); the
    /// quota gate needs an <see cref="ITenantContext"/>, so this wraps the
    /// tenant the SIGNATURE already proved, not the ambient ITenantContext
    /// singleton (which would resolve the wrong tenant here, or none).
    /// </remarks>
    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }
}
