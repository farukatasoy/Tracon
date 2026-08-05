using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Kayitli bir ses ekini metne cevirir.
/// </summary>
/// <remarks>
/// Tool ham ses <strong>almaz</strong>, bir ek kimligi alir. Baglam penceresine
/// base64 ses koymak hem pahalidir hem de model bunu cozemez.
/// </remarks>
internal sealed class TranscribeTool : VoiceToolBase
{
    /// <summary>Tool adi.</summary>
    public const string ToolName = "transcribe";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "attachmentId": {
              "type": "string",
              "description": "Metne cevrilecek ses ekinin kimligi (GUID)."
            },
            "languageCode": {
              "type": "string",
              "description": "Beklenen dilin ISO 639-1 kodu. Bos birakilirsa dil kendiliginden sezilir."
            }
          },
          "required": ["attachmentId"]
        }
        """;

    public TranscribeTool(IServiceProvider services)
        : base(services, Schema)
    {
    }

    /// <inheritdoc />
    public override string Name => ToolName;

    /// <inheritdoc />
    public override string Description =>
        "Kayitli bir ses ekini metne cevirir. Ekin kimligini alir, cozulen metni doner.";

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var rawId = RequireText(arguments, "attachmentId");

        if (!Guid.TryParse(rawId, out var attachmentId))
        {
            throw new AgentPrismException($"'{rawId}' gecerli bir ek kimligi degil.");
        }

        var store = Resolve<IAttachmentStore>();
        var tenantContext = Resolve<ITenantContext>();
        var transcriber = Resolve<ISpeechTranscriber>();
        var pricing = Resolve<VoicePricing>();
        var options = Resolve<IOptions<VoiceOptions>>().Value;

        var tenantId = AgentPrismRunContext.Current?.TenantId ?? tenantContext.TenantId;

        var descriptor = await store.GetAsync(tenantId, attachmentId, cancellationToken).ConfigureAwait(false)
                         ?? throw new AgentPrismException($"'{attachmentId}' kimlikli ek bulunamadi.");

        if (!descriptor.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentPrismException(
                $"'{attachmentId}' kimlikli ek bir ses dosyasi degil (tur: {descriptor.MediaType}).");
        }

        var content = await store.OpenReadAsync(tenantId, attachmentId, cancellationToken).ConfigureAwait(false)
                      ?? throw new AgentPrismException($"'{attachmentId}' kimlikli ekin icerigi okunamadi.");

        SpeechTranscript transcript;

        await using (content.ConfigureAwait(false))
        {
            transcript = await transcriber
                .TranscribeAsync(
                    content,
                    descriptor.MediaType,
                    new SpeechTranscriptionOptions
                    {
                        ModelId = options.TranscriptionModelId,
                        LanguageCode = OptionalText(arguments, "languageCode"),
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        ReportUsage(transcript, pricing, options);

        return transcript.LanguageCode is { Length: > 0 } language
            ? $"[dil={language}] {transcript.Text}"
            : transcript.Text;
    }

    /// <summary>Olcumu suren cagriya baglar.</summary>
    /// <remarks>
    /// Saglayici sureyi bildirmediyse olcum HIC bildirilmez — sifir saniye
    /// yazmak "bedava" demek olurdu.
    /// </remarks>
    private static void ReportUsage(SpeechTranscript transcript, VoicePricing pricing, VoiceOptions options)
    {
        if (transcript.AudioDuration is not { } duration)
        {
            return;
        }

        var modelId = options.TranscriptionModelId ?? ElevenLabsSpeechClient.DefaultTranscriptionModel;

        _ = AgentPrismToolUsage.Report(new ToolCallUsage
        {
            Unit = ToolUsageUnits.Seconds,
            Quantity = (decimal)duration.TotalSeconds,
            Cost = pricing.ForDuration(options.Provider, modelId, duration),
            Currency = pricing.Currency,
            IsEstimated = false,
        });
    }
}
