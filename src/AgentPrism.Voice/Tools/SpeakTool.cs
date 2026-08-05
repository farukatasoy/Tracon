using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Bir metni seslendirir ve sesi ek deposuna yazar.
/// </summary>
/// <remarks>
/// <para>
/// Tool sonucu modele <strong>ekin kimligini</strong> dondurur, ham sesi degil.
/// Ham ses sonuca konsaydi baglam penceresi base64 ile dolardi.
/// </para>
/// <para>
/// 🚨 Uretilen ses ek deposuna yazilmadan once <c>AttachmentTypeGuard</c>'dan
/// gecer. Depo hicbir dogrulama yapmaz (dogrulama HTTP katmanindadir); saglayici
/// beklenmeyen bir bicim dondururse hata YAZMA aninda cikmalidir, calma aninda
/// degil.
/// </para>
/// </remarks>
internal sealed class SpeakTool : VoiceToolBase
{
    /// <summary>Tool adi. Agent tanimlarinda bu ad kullanilir.</summary>
    public const string ToolName = "speak";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "description": "Seslendirilecek metin."
            },
            "voiceId": {
              "type": "string",
              "description": "Kullanilacak sesin kimligi. Bos birakilirsa varsayilan ses kullanilir."
            }
          },
          "required": ["text"]
        }
        """;

    public SpeakTool(IServiceProvider services)
        : base(services, Schema)
    {
    }

    /// <inheritdoc />
    public override string Name => ToolName;

    /// <inheritdoc />
    public override string Description =>
        "Bir metni sese cevirir ve uretilen ses dosyasini kaydeder. " +
        "Sonuc olarak ekin kimligini doner; kullanici sesi arayuzden calar.";

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var text = RequireText(arguments, "text");
        var options = Resolve<IOptions<VoiceOptions>>().Value;

        if (text.Length > options.MaxCharactersPerRequest)
        {
            // Metin KIRPILMAZ: kirpma, kullanicinin duymadigi bir cumle uretir
            // ve sebebi gorunmez olur.
            throw new AgentPrismException(
                $"Metin {text.Length} karakter; sinir {options.MaxCharactersPerRequest}. " +
                "Metni kisaltin veya `AgentPrism:Voice:MaxCharactersPerRequest` ayarini yukseltin.");
        }

        var synthesizer = Resolve<ISpeechSynthesizer>();
        var guard = Resolve<AttachmentTypeGuard>();
        var store = Resolve<IAttachmentStore>();
        var tenantContext = Resolve<ITenantContext>();
        var pricing = Resolve<VoicePricing>();

        var modelId = options.SynthesisModelId ?? ElevenLabsSpeechClient.DefaultSynthesisModel;

        var request = new SpeechRequest
        {
            Text = text,
            VoiceId = OptionalText(arguments, "voiceId"),
            ModelId = options.SynthesisModelId,
            OutputFormat = options.OutputFormat,
        };

        var audio = await synthesizer.SynthesizeAsync(request, cancellationToken).ConfigureAwait(false);

        var validation = guard.Validate(audio.Data.Span);

        if (!validation.IsValid)
        {
            throw new AgentPrismException(
                $"Uretilen ses kaydedilemedi: {validation.Error} " +
                $"Saglayicinin cikti bicimi '{options.OutputFormat}'. " +
                "Ek deposu turu sihirli bayttan dogrular; konteyner tasimayan bicimler reddedilir.");
        }

        var scope = AgentPrismRunContext.Current;

        var descriptor = await store.SaveAsync(
            new AttachmentContent
            {
                TenantId = scope?.TenantId ?? tenantContext.TenantId,

                // 🚨 Oturum kimligi ZORUNLUDUR. Bos birakilirsa saklama politikasi
                // eki sahipsiz sayar ve kesim tarihinden sonra siler; oturum hala
                // yasarken transcript'teki ses kaybolur (docs/28, G1).
                SessionId = scope?.SessionId,
                RunId = scope?.RunId,
                FileName = BuildFileName(validation.MediaType!),
                MediaType = validation.MediaType!,
                Data = audio.Data,
                CreatedBy = scope?.AgentName,
            },
            cancellationToken).ConfigureAwait(false);

        ReportUsage(audio, pricing, options, modelId, text);

        var duration = audio.Duration is { } value
            ? $", sure={value.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}sn"
            : string.Empty;

        return $"Ses uretildi. attachmentId={descriptor.Id}, tur={descriptor.MediaType}, " +
               $"boyut={descriptor.ByteSize} bayt{duration}";
    }

    /// <summary>Olcumu suren cagriya baglar.</summary>
    /// <remarks>
    /// Bildirim basarisiz olursa (calistirma kaydi kapali) tool'un isi BOZULMAZ;
    /// gozlemlenebilirlik islevselligi bozmaz.
    /// </remarks>
    private static void ReportUsage(
        SpeechAudio audio,
        VoicePricing pricing,
        VoiceOptions options,
        string modelId,
        string text)
    {
        var characters = audio.CharactersBilled ?? text.Length;

        _ = AgentPrismToolUsage.Report(new ToolCallUsage
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = characters,
            Cost = pricing.ForCharacters(options.Provider, modelId, characters),
            Currency = pricing.Currency,
            IsEstimated = audio.UsageSource != SpeechUsageSource.Provider,
        });
    }

    private static string BuildFileName(string mediaType)
    {
        var extension = mediaType switch
        {
            "audio/mpeg" => "mp3",
            "audio/ogg" => "ogg",
            "audio/wav" => "wav",
            _ => "bin",
        };

        return $"speech-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{extension}";
    }
}
