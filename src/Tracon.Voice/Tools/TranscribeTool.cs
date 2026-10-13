using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Transcribes a recorded audio attachment to text.
/// </summary>
/// <remarks>
/// <para>
/// The tool <strong>does not take</strong> raw audio, it takes an attachment
/// id. Putting base64 audio in the context window is both expensive and
/// something the model cannot decode.
/// </para>
/// <para>
/// The transcriber is the default one, resolved through
/// <see cref="ISpeechProviderResolver"/> before the attachment is read: the
/// tenant's egress policy and own credential are checked first, and a refusal is
/// the tool's error.
/// </para>
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
        var pricing = Resolve<VoicePricing>();

        // F-298: resolved per call, through the tenant's checks.
        var transcriber = await Resolve<ISpeechProviderResolver>()
            .GetTranscriberAsync(name: null, cancellationToken)
            .ConfigureAwait(false);
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

        // The configured model is the DEFAULT transcriber's setting; this tool only
        // transcribes with the default one.
        var transcription = new SpeechTranscriptionOptions
        {
            ModelId = options.TranscriptionModelId,
            LanguageCode = OptionalText(arguments, "languageCode"),
        };

        await using (content.ConfigureAwait(false))
        {
            transcript = await transcriber
                .TranscribeAsync(content, descriptor.MediaType, transcription, cancellationToken)
                .ConfigureAwait(false);
        }

        ReportUsage(transcript, pricing, transcriber, transcription.ModelId);

        return transcript.LanguageCode is { Length: > 0 } language
            ? $"[lang={language}] {transcript.Text}"
            : transcript.Text;
    }

    /// <summary>Attaches the usage measurement to the current call.</summary>
    /// <remarks>
    /// If the provider did not report the duration, the measurement is NOT
    /// reported at all — writing zero seconds would mean "free". The price is
    /// keyed by the bound transcriber's provider name and the model it reports.
    /// </remarks>
    private static void ReportUsage(
        SpeechTranscript transcript,
        VoicePricing pricing,
        ISpeechTranscriber transcriber,
        string? requestedModel)
    {
        if (transcript.AudioDuration is not { } duration)
        {
            return;
        }

        var model = transcript.ModelId ?? requestedModel;

        _ = TraconToolUsage.Report(new ToolCallUsage
        {
            Unit = ToolUsageUnits.Seconds,
            Quantity = (decimal)duration.TotalSeconds,
            Cost = pricing.ForTranscription(transcriber.ProviderName, model, duration),
            Currency = pricing.Currency,
            IsEstimated = false,
            Provider = transcriber.ProviderName,
            Model = model,
        });
    }
}
