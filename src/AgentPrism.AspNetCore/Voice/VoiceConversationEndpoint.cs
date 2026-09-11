using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
                "The token is carried in the 'Sec-WebSocket-Protocol' subprotocol; it is NOT accepted in the query string. " +
                "If a registered IRunAuthorizationHandler denies the caller, the handshake is refused with 404 — identical " +
                "to a session belonging to another tenant, so a denial never confirms the session exists.");
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

        if (!await IsAuthorizedAsync(context, services, options).ConfigureAwait(false))
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

        if (!await VoiceEndpointGates
                .OwnsSessionAsync(services, sessionId, tenantId, context.RequestAborted)
                .ConfigureAwait(false))
        {
            // 🚨 sessionId is untrusted input. Another tenant's session is
            // answered as if it "does not exist"; reporting its existence would leak information.
            await WriteSessionNotFoundAsync(context, sessionId).ConfigureAwait(false);

            return;
        }

        // Both consumer gates live in VoiceEndpointGates so that every voice
        // endpoint denies with the SAME body (K-687).
        if (await VoiceEndpointGates.DeniesSessionAsync(context, sessionId, tenantContext).ConfigureAwait(false))
        {
            await WriteSessionNotFoundAsync(context, sessionId).ConfigureAwait(false);

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
    /// Enforces the credential layer carried in the WebSocket subprotocol header.
    /// </summary>
    /// <remarks>
    /// A browser cannot set an <c>Authorization</c> header on a handshake, so the
    /// credential travels in a subprotocol value instead. Two rules apply, and the
    /// second one is the reason this method exists:
    /// <list type="number">
    /// <item>
    /// When nothing is presented and no static token is configured, the request
    /// passes. The group has still cleared the loopback restriction and the
    /// authorization policy, and this keeps the local default working.
    /// </item>
    /// <item>
    /// When something IS presented, it must be verified. It used to be compared
    /// only against the static token: an installation authenticating with tenant
    /// API keys configures no static token, so the comparison was skipped and ANY
    /// subprotocol value was accepted. The reverse also failed - with a static
    /// token configured, a valid API key was compared against it, did not match,
    /// and a legitimate caller got 401.
    /// </item>
    /// </list>
    /// </remarks>
    private static async ValueTask<bool> IsAuthorizedAsync(
        HttpContext context,
        IServiceProvider services,
        AgentPrismEndpointOptions options)
    {
        var presented = ExtractPresentedToken(context);

        if (presented is null)
        {
            // Nothing presented: allowed only when no static token is demanded.
            return options.AuthToken is not { Length: > 0 };
        }

        if (options.AuthToken is { Length: > 0 } expected
            && BearerTokenValidator.IsValidToken(presented, expected))
        {
            return true;
        }

        if (services.GetService<IApiKeyStore>() is { } apiKeyStore)
        {
            var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;

            var record = await ApiKeyAuthenticator
                .AuthenticateAsync(apiKeyStore, presented, timeProvider, context.RequestAborted)
                .ConfigureAwait(false);

            if (record is not null)
            {
                ApiKeyRequestContext.Set(context, record);

                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the token carried in the subprotocol header.</summary>
    /// <returns>The presented value; <see langword="null"/> if none was sent.</returns>
    private static string? ExtractPresentedToken(HttpContext context)
    {
        foreach (var requested in context.WebSockets.WebSocketRequestedProtocols)
        {
            if (requested.StartsWith(VoiceConversationProtocol.TokenSubProtocolPrefix, StringComparison.Ordinal))
            {
                return requested[VoiceConversationProtocol.TokenSubProtocolPrefix.Length..];
            }
        }

        return null;
    }

    /// <summary>
    /// Writes the single 404 this endpoint uses for "no such session", whether
    /// the session belongs to another tenant or the handler denied the caller.
    /// </summary>
    /// <remarks>
    /// Delegated so the live endpoints and this one answer with the SAME bytes.
    /// </remarks>
    private static Task WriteSessionNotFoundAsync(HttpContext context, string sessionId)
        => VoiceEndpointGates.WriteSessionNotFoundAsync(context, sessionId);

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
        => VoiceEndpointGates.WriteProblemAsync(context, statusCode, title, detail);
}
