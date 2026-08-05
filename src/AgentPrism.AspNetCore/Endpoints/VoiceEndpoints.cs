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
            .WithName("AgentPrismVoiceHealth")
            .WithSummary("Ses saglayicisinin erisilebilirligini denetler.")
            .WithDescription("Denetim ucret uretmez: ses uretilmez, kullanilabilir sesler okunur.");

        builder.MapGet("/api/voice/voices", ListVoicesAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismVoiceList")
            .WithSummary("Kullanilabilir sesleri listeler.");

        builder.MapPost("/api/voice/speak", SpeakAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismVoiceSpeak")
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
