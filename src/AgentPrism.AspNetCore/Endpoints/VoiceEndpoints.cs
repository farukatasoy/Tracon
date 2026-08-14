using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Ses saglayicisinin durumu ve ses listesi uclari.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <strong>Iki seslendirme yolu vardir ve olcum davranislari FARKLIDIR.</strong>
/// Agent'in <c>speak</c> tool'u bir calistirmanin icinde calisir; olcumu
/// <c>tool_invocations</c> satirina yazilir. Buradaki <c>POST /api/voice/speak</c>
/// ucu ise bir <em>operator eylemidir</em> ve calistirma DISINDADIR:
/// <c>tool_invocations.run_id</c> zorunlu bir yabanci anahtardir, dolayisiyla
/// calistirmasiz bir olcum satiri yazilamaz.
/// </para>
/// <para>
/// Maliyet yine de gorunmez degildir: uc olculen karakter sayisini ve tutari
/// <strong>yanitta dondurur</strong> ve arayuz bunu gosterir. Ayrica uc
/// <c>Operator</c> rolu ister ve tool ile ayni karakter sinirina uyar.
/// Kalici bir kayit isteniyorsa yol agent'in tool'udur.
/// </para>
/// <para>
/// Ses saglayicisi <c>/api/models/health</c> ciktisinda BILEREK gorunmez: bir
/// <c>IModelProvider</c> degildir ve iki kaynak tek listede toplanirsa devre
/// kesici ile model katalogu yanlis davranir.
/// </para>
/// <para>
/// Servisler istege bagli cozulur: <c>UseVoice()</c> cagrilmadiysa uclar
/// <c>404</c> yerine acik bir <c>501</c> doner — yapilandirma eksikligi ile
/// yanlis adres birbirine karismasin diye.
/// </para>
/// </remarks>
internal static class VoiceEndpoints
{
    /// <summary>Ses uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/voice/health", CheckHealthAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismVoiceHealth")
            .WithTags("AgentPrism", "Voice")
            .WithSummary("Ses saglayicisinin erisilebilirligini denetler.")
            .WithDescription("Denetim ucret uretmez: ses uretilmez, kullanilabilir sesler okunur.");

        builder.MapGet("/api/voice/voices", ListVoicesAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismVoiceList")
            .WithTags("AgentPrism", "Voice")
            .WithSummary("Kullanilabilir sesleri listeler.");

        builder.MapGet("/api/voice/sessions", ListSessionsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismVoiceSessions")
            .WithTags("AgentPrism", "Voice")
            .WithSummary("Gercek zamanli konusma baglantilarinin ozet kaydini listeler.")
            .WithDescription(
                "Kayit ses ICERMEZ: yalnizca sure, tur sayisi ve olcum tasir. Konusma katmani " +
                "acik degilse liste bostur.");

        builder.MapPost("/api/voice/speak", SpeakAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismVoiceSpeak")
            .WithTags("AgentPrism", "Voice")
            .WithSummary("Bir metni seslendirir ve ek olarak kaydeder.")
            .WithDescription(
                "Operator eylemidir ve bir calistirmaya BAGLI DEGILDIR; olcum " +
                "tool_invocations'a yazilmaz, yanitta dondurulur. Kalici olcum " +
                "isteniyorsa agent'in `speak` tool'unu kullanin.");
    }

    private static async Task<Results<Ok<SpeakResponse>, ProblemHttpResult>> SpeakAsync(
        SpeakRequest request,
        [FromServices] ISpeechSynthesizer? synthesizer,
        [FromServices] IVoicePricingReader? pricing,
        AttachmentTypeGuard guard,
        IAttachmentStore store,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        CancellationToken cancellationToken)
    {
        if (synthesizer is null)
        {
            return NotConfigured();
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return TypedResults.Problem(
                title: "Metin bos",
                detail: "'text' alani zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // 🚨 SpeakTool (agent tool cagrisi) bu siniri zaten uyguluyordu; bu HTTP
        // operator ucu (agent'tan bagimsiz, dogrudan cagrilir) aymiydi ve
        // XML dokumaninin "ayni sinira uyar" iddiasini karsilamiyordu.
        var maxCharacters = synthesizer.MaxCharactersPerRequest;

        if (request.Text.Length > maxCharacters)
        {
            return TypedResults.Problem(
                title: "Metin cok uzun",
                detail: $"Metin {request.Text.Length} karakter; sinir {maxCharacters}. " +
                        "Metni kisaltin veya 'AgentPrism:Voice:MaxCharactersPerRequest' ayarini yukseltin.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        SpeechAudio audio;

        try
        {
            audio = await synthesizer
                .SynthesizeAsync(
                    new SpeechRequest { Text = request.Text, VoiceId = request.VoiceId },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AgentPrismException exception)
        {
            // Mesaj yalnizca durum kodu ve gerekce tasir; saglayicinin govdesi
            // (istegi ve bazen anahtar parcasini yankilayan) hicbir zaman gecmez.
            return TypedResults.Problem(
                title: "Ses uretilemedi",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }

        var validation = guard.Validate(audio.Data.Span);

        if (!validation.IsValid)
        {
            return TypedResults.Problem(
                title: "Uretilen ses kaydedilemedi",
                detail: validation.Error,
                statusCode: StatusCodes.Status502BadGateway);
        }

        var descriptor = await store.SaveAsync(
            new AttachmentContent
            {
                TenantId = tenantContext.TenantId,

                // Oturum kimligi ZORUNLUDUR: bos birakilirsa saklama politikasi
                // eki sahipsiz sayar ve siler.
                SessionId = request.SessionId,
                FileName = $"speech-{DateTime.UtcNow:yyyyMMdd-HHmmss}.mp3",
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
            Cost = pricing?.ForCharacters(characters),
            Currency = pricing?.Currency,
        });
    }

    /// <summary>Konusma kayitlarini listeler.</summary>
    /// <remarks>
    /// Depo <see cref="IVoiceSessionStore"/> istege baglidir: konusma katmani
    /// acilmadiysa kayitli degildir ve uc <c>501</c> yerine <strong>bos liste</strong>
    /// doner. Gerekce: liste ucu bir yetenegin varligini degil, verinin yoklugunu
    /// bildirir; arayuz paneli hatasiz cizilir.
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
        [FromServices] ISpeechSynthesizer? synthesizer,
        CancellationToken cancellationToken)
    {
        if (synthesizer is null)
        {
            return NotConfigured();
        }

        var voices = await synthesizer.ListVoicesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(voices);
    }

    private static ProblemHttpResult NotConfigured()
        => TypedResults.Problem(
            title: "Ses saglayicisi yapilandirilmadi",
            detail: "Ses ozelligini acmak icin `AgentPrism.Voice` paketini ekleyin ve `UseVoice(...)` cagirin.",
            statusCode: StatusCodes.Status501NotImplemented);
}
