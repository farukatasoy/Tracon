using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// Gercek zamanli konusmanin WebSocket ucu.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Bu uc barindirma modelini degistirir: baglanti dakikalarca acik kalir ve
/// <em>bir</em> sunucu ornegine baglidir. Yetenek istege baglidir —
/// <c>UseVoiceConversation()</c> cagrilmadikca <see cref="VoiceConversationDriver"/>
/// kayitli olmaz ve uc <c>501</c> doner.
/// </para>
/// <para>
/// 🚨 Uc <strong>ucuncu bir uc grubuna</strong> baglanir
/// (<c>requireBearerToken: false</c>): tarayici bir WebSocket el sikismasina
/// <c>Authorization</c> basligi <strong>ekleyemez</strong>. Token bunun yerine
/// <c>Sec-WebSocket-Protocol</c> alt protokolunde tasinir ve burada
/// <em>elle</em> dogrulanir. Loopback kisiti ve authorization policy yine
/// uygulanir; hicbir katman atlanmaz. Ayni desen arayuz kabugunda (K-046) ve
/// MCP OAuth geri donusunde de kullanildi.
/// </para>
/// <para>
/// Token'in sorgu dizesine konmamasi bilinclidir: adres sunucu gunluklerine,
/// ters vekil gunluklerine ve tarayici gecmisine yazilir.
/// </para>
/// </remarks>
internal static class VoiceConversationEndpoint
{
    /// <summary>Konusma ucunu baglar.</summary>
    /// <param name="builder">Uc grubu (bearer token katmanindan muaf).</param>
    /// <param name="options">Erisim ayarlari.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
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
            .WithSummary("Gercek zamanli konusma icin WebSocket baglantisi acar.")
            .WithDescription(
                "Istemci -> sunucu: ham ses (ikili) ve denetim mesajlari (JSON metin). " +
                "Sunucu -> istemci: ses parcalari (ikili) ve olay cerceveleri (JSON metin). " +
                "Token 'Sec-WebSocket-Protocol' alt protokolunde tasinir; sorgu dizesinde KABUL EDILMEZ.");
    }

    private static async Task HandleAsync(HttpContext context, string sessionId, AgentPrismEndpointOptions options)
    {
        var services = context.RequestServices;

        // Uc yalnizca surucu kayitliyken BAGLANIR (bkz. MapAgentPrism); bu
        // yuzden cozumleme burada zorunludur. `UseVoiceConversation()`
        // cagrilmadiysa bu adres hic yoktur ve istek 404 alir — barindirma
        // modelini degistiren bir yetenek 501 ile "var ama kapali" gorunmez.
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
            // 🚨 Beklenen token hakkinda hicbir bilgi verilmez.
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
            // 🚨 sessionId guvenilmez girdidir. Baska bir kiracinin oturumu
            // "yok" gibi yanitlanir; varligini bildirmek bilgi sizdirirdi.
            await WriteProblemAsync(
                context,
                StatusCodes.Status404NotFound,
                "Session not found",
                $"There is no session with id '{sessionId}', or it does not belong to this tenant.").ConfigureAwait(false);

            return;
        }

        // Yer soket YUKSELTILMEDEN once ayrilir: sinir dolduysa istemci duzgun
        // bir HTTP hatasi gorur. Yukselttikten sonra kapatmak nedeni cok daha
        // kotu anlatirdi.
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
    /// Token katmanini alt protokol basligindan uygular.
    /// </summary>
    /// <remarks>
    /// Token yapilandirilmamissa katman kapalidir ve istek gecer — uc grubu
    /// yine loopback kisitindan ve authorization policy'den gecmistir.
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

    /// <summary>Oturumun bu kiraciya ait olup olmadigini denetler.</summary>
    /// <returns>
    /// Oturum bu kiraciya aitse veya henuz yoksa <see langword="true"/>.
    /// </returns>
    /// <remarks>
    /// Var olmayan bir oturum kabul edilir: ilk konusma turu onu acar. Var olan
    /// ama baska bir kiraciya ait olan oturum reddedilir.
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
