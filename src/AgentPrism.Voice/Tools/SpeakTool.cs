using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Speaks a piece of text and writes the audio to the attachment store.
/// </summary>
/// <remarks>
/// <para>
/// The tool result returns the <strong>attachment id</strong> to the model, not
/// the raw audio. If the raw audio were placed in the result, the context
/// window would fill up with base64.
/// </para>
/// <para>
/// 🚨 The produced audio passes through <c>AttachmentTypeGuard</c> before being
/// written to the attachment store. The store performs no validation
/// (validation lives in the HTTP layer); if the provider returns an unexpected
/// format, the error must surface at WRITE time, not at playback time.
/// </para>
/// </remarks>
internal sealed class SpeakTool : VoiceToolBase
{
    /// <summary>The tool name. Used by this name in agent definitions.</summary>
    public const string ToolName = "speak";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "description": "The text to speak."
            },
            "voiceId": {
              "type": "string",
              "description": "The id of the voice to use. The default voice is used when left empty."
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
        "Converts a piece of text to speech and saves the produced audio file. " +
        "Returns the attachment id as a result; the user plays the audio from the UI.";

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
            // The text is NOT truncated: truncation would produce a sentence
            // the user never hears, and the reason would become invisible.
            throw new AgentPrismException(
                $"The text is {text.Length} characters; the limit is {options.MaxCharactersPerRequest}. " +
                "Shorten the text or raise the `AgentPrism:Voice:MaxCharactersPerRequest` setting.");
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
                $"The produced audio could not be saved: {validation.Error} " +
                $"The provider's output format is '{options.OutputFormat}'. " +
                "The attachment store validates the type from the magic bytes; formats without a container are rejected.");
        }

        var scope = AgentPrismRunContext.Current;

        var descriptor = await store.SaveAsync(
            new AttachmentContent
            {
                TenantId = scope?.TenantId ?? tenantContext.TenantId,

                // 🚨 The session id is REQUIRED. If left empty, the retention
                // policy treats the attachment as orphaned and deletes it after
                // the cutoff date; the audio in the transcript disappears while
                // the session is still alive (docs/28, G1).
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
            ? $", duration={value.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s"
            : string.Empty;

        return $"Audio produced. attachmentId={descriptor.Id}, type={descriptor.MediaType}, " +
               $"size={descriptor.ByteSize} bytes{duration}";
    }

    /// <summary>Attaches the measurement to the current run.</summary>
    /// <remarks>
    /// If reporting fails (the run record is closed), the tool's work is NOT
    /// disrupted; observability does not break functionality.
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
