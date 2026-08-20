using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// The WebSocket endpoint for real-time conversation.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint changes the hosting model: the connection stays open for
/// minutes and is tied to <em>one</em> server instance. The capability is opt-in —
/// unless <c>UseVoiceConversation()</c> is called, <see cref="VoiceConversationDriver"/>
/// is not registered and the endpoint returns <c>501</c>.
/// </para>
/// <para>
/// The endpoint is mounted on a <strong>third endpoint group</strong>
/// (<c>requireBearerToken: false</c>): a browser <strong>cannot add</strong> an
/// <c>Authorization</c> header to a WebSocket handshake. Instead, the token is
/// carried in the <c>Sec-WebSocket-Protocol</c> subprotocol and validated
/// <em>by hand</em> here. The loopback restriction and the authorization policy
/// still apply; no layer is skipped. The same pattern is used in the UI shell
///  and the MCP OAuth callback.
/// </para>
/// <para>
/// Not putting the token in the query string is deliberate: the address is
/// written to server logs, reverse proxy logs, and the browser history.
/// </para>
/// </remarks>
internal static class VoiceConversationEndpoint
{
    /// <summary>Maps the conversation endpoint.</summary>
    /// <param name="builder">The endpoint group (exempt from the bearer token layer).</param>
    /// <param name="options">The access settings.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(
        IEndpointRouteBuilder builder,
        AgentPrismEndpointOptions options,
        AgentPrismRolePolicies roles)
    {
        builder.MapGet(
                "/api/voice/sessions/{sessionId}/stream",
                (HttpContext context, string sessionId) => HandleAsync(context, sessionId, options))
            .RequireRole(roles.Operator)
            .WithName("AgentPrismVoiceStream")
            .WithTags("AgentPrism", "Voice")
            .WithSummary("Opens a WebSocket connection for real-time conversation.")
            .WithDescription(
                "Client -> server: raw audio (binary) and control messages (JSON text). " +
                "Server -> client: audio chunks (binary) and event frames (JSON text). " +
                "The token is carried in the 'Sec-WebSocket-Protocol' subprotocol; it is NOT accepted in the query string.");
    }

    private static async Task HandleAsync(HttpContext context, string sessionId, AgentPrismEndpointOptions options)
    {
        var services = context.RequestServices;

        // The endpoint is WIRED UP only while the driver is registered (see
        // MapAgentPrism); this is why resolution here is mandatory. If
        // `UseVoiceConversation()` was not called, this address does not exist at
        // all and the request gets a 404 — a capability that changes the hosting
        // model does not show up as "present but off" via 501.
        var driver = services.GetRequiredService<VoiceConversationDriver>();

        if (!driver.IsReady)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status501NotImplemented,
                "Voice provider not configured",
                "Conversation requires both transcription and synthesis: register an " +
                "ISpeechTranscriber and an ISpeechSynthesizer (e.g. `UseVoice(...)`).").ConfigureAwait(false);

            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "WebSocket upgrade required",
                "This endpoint can only be used over WebSocket. This error is returned if the " +
                "request is not an upgrade request, or the application has no WebSocket middleware.").ConfigureAwait(false);

            return;
        }

        if (!IsTokenValid(context, options))
        {
            // 🚨 No information about the expected token is given.
            await WriteProblemAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Authentication failed",
                $"Send the token via the '{VoiceConversationProtocol.TokenSubProtocolPrefix}<token>' subprotocol.")
                .ConfigureAwait(false);

            return;
        }

        var tenantContext = services.GetRequiredService<ITenantContext>();
        var tenantId = tenantContext.TenantId;

        if (!await OwnsSessionAsync(services, sessionId, tenantId, context.RequestAborted).ConfigureAwait(false))
        {
            // 🚨 sessionId is untrusted input. Another tenant's session is
            // answered as if it "does not exist"; reporting its existence would leak information.
            await WriteProblemAsync(
                context,
                StatusCodes.Status404NotFound,
                "Session not found",
                $"There is no session with id '{sessionId}', or it does not belong to this tenant.").ConfigureAwait(false);

            return;
        }

        // The slot is reserved BEFORE the socket is UPGRADED: if the limit is
        // full, the client sees a proper HTTP error. Closing after the upgrade
        // would explain the reason far worse.
        using var lease = driver.Limiter.TryAcquire(tenantId);

        if (lease is null)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status429TooManyRequests,
                "Concurrent conversation limit reached",
                $"A tenant may open at most {driver.Limiter.Limit} conversation connections.").ConfigureAwait(false);

            return;
        }

        using var socket = await context.WebSockets
            .AcceptWebSocketAsync(VoiceConversationProtocol.SubProtocol)
            .ConfigureAwait(false);

        var actor = services.GetRequiredService<IAuditActorResolver>();

        await driver.RunAsync(
            socket,
            new VoiceConversationRequest
            {
                TenantId = tenantId,
                SessionId = sessionId,
                CreatedBy = actor.Resolve(),
            },
            context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Enforces the token layer from the subprotocol header.
    /// </summary>
    /// <remarks>
    /// If no token is configured, the layer is off and the request passes through
    /// — the endpoint group has still passed the loopback restriction and the
    /// authorization policy.
    /// </remarks>
    private static bool IsTokenValid(HttpContext context, AgentPrismEndpointOptions options)
    {
        if (options.AuthToken is not { Length: > 0 } expected)
        {
            return true;
        }

        foreach (var requested in context.WebSockets.WebSocketRequestedProtocols)
        {
            if (!requested.StartsWith(VoiceConversationProtocol.TokenSubProtocolPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var presented = requested.AsSpan(VoiceConversationProtocol.TokenSubProtocolPrefix.Length);

            if (BearerTokenValidator.IsValidToken(presented, expected))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Checks whether the session belongs to this tenant.</summary>
    /// <returns>
    /// <see langword="true"/> if the session belongs to this tenant or does not exist yet.
    /// </returns>
    /// <remarks>
    /// A session that does not yet exist is accepted: the first conversation turn
    /// opens it. A session that exists but belongs to another tenant is rejected.
    /// </remarks>
    private static async ValueTask<bool> OwnsSessionAsync(
        IServiceProvider services,
        string sessionId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var store = services.GetRequiredService<ISessionStore>();
        var record = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        return record is null || string.Equals(record.TenantId, tenantId, StringComparison.Ordinal);
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        var result = Results.Problem(title: title, detail: detail, statusCode: statusCode);

        await result.ExecuteAsync(context).ConfigureAwait(false);
    }
}
