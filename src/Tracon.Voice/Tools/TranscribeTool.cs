using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Transcribes a recorded audio attachment to text.
/// </summary>
/// <remarks>
/// The tool <strong>does not take</strong> raw audio, it takes an attachment
/// id. Putting base64 audio in the context window is both expensive and
/// something the model cannot decode.
/// </remarks>
internal sealed class TranscribeTool : VoiceToolBase
{
    /// <summary>The tool name.</summary>
    public const string ToolName = "transcribe";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "attachmentId": {
              "type": "string",
              "description": "The id (GUID) of the audio attachment to transcribe."
            },
            "languageCode": {
              "type": "string",
              "description": "The ISO 639-1 code of the expected language. If left empty, the language is auto-detected."
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
        "Transcribes a recorded audio attachment to text. Takes the attachment's id, returns the resolved text.";

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var rawId = RequireText(arguments, "attachmentId");

        if (!Guid.TryParse(rawId, out var attachmentId))
        {
            throw new TraconException($"'{rawId}' is not a valid attachment id.");
        }

        var store = Resolve<IAttachmentStore>();
        var tenantContext = Resolve<ITenantContext>();
        var transcriber = Resolve<ISpeechTranscriber>();
        var pricing = Resolve<VoicePricing>();
        var options = Resolve<IOptions<VoiceOptions>>().Value;

        var tenantId = TraconRunContext.Current?.TenantId ?? tenantContext.TenantId;

        var descriptor = await store.GetAsync(tenantId, attachmentId, cancellationToken).ConfigureAwait(false)
                         ?? throw new TraconException($"No attachment with id '{attachmentId}' was found.");

        if (!descriptor.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            throw new TraconException(
                $"The attachment with id '{attachmentId}' is not an audio file (type: {descriptor.MediaType}).");
        }

        var content = await store.OpenReadAsync(tenantId, attachmentId, cancellationToken).ConfigureAwait(false)
                      ?? throw new TraconException($"Could not read the content of the attachment with id '{attachmentId}'.");

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
            ? $"[lang={language}] {transcript.Text}"
            : transcript.Text;
    }

    /// <summary>Attaches the usage measurement to the current call.</summary>
    /// <remarks>
    /// If the provider did not report the duration, the measurement is NOT
    /// reported at all — writing zero seconds would mean "free".
    /// </remarks>
    private static void ReportUsage(SpeechTranscript transcript, VoicePricing pricing, VoiceOptions options)
    {
        if (transcript.AudioDuration is not { } duration)
        {
            return;
        }

        var modelId = options.TranscriptionModelId ?? ElevenLabsSpeechClient.DefaultTranscriptionModel;

        _ = TraconToolUsage.Report(new ToolCallUsage
        {
            Unit = ToolUsageUnits.Seconds,
            Quantity = (decimal)duration.TotalSeconds,
            Cost = pricing.ForDuration(options.Provider, modelId, duration),
            Currency = pricing.Currency,
            IsEstimated = false,
        });
    }
}
