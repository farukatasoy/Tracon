using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

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
/// The synthesizer is the default one, resolved through
/// <see cref="ISpeechProviderResolver"/> on every call: the tenant's egress policy
/// and own credential are checked before any text leaves the process, and a refusal
/// is the tool's error.
/// </para>
/// <para>
/// The produced audio passes through <c>AttachmentTypeGuard</c> before being
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

        // F-298: resolved per call, through the tenant's checks. The resolver was
        // taken from the setup-time provider; the one MAF passes the tool is empty.
        var synthesizer = await Resolve<ISpeechProviderResolver>()
            .GetSynthesizerAsync(name: null, cancellationToken)
            .ConfigureAwait(false);

        // The limit is the BOUND synthesizer's, the value the HTTP endpoint
        // reads too (ISpeechSynthesizer.MaxCharactersPerRequest). The setting
        // is only the built-in client's answer to that question (F-336).
        var maxCharacters = synthesizer.MaxCharactersPerRequest;

        if (text.Length > maxCharacters)
        {
            // The text is NOT truncated: truncation would produce a sentence
            // the user never hears, and the reason would become invisible.
            throw new TraconException(
                $"The text is {text.Length} characters; the limit is {maxCharacters}. " +
                "Shorten the text or raise the speech synthesizer's limit " +
                "(`Tracon:Voice:MaxCharactersPerRequest` for the built-in client).");
        }

        var guard = Resolve<AttachmentTypeGuard>();
        var store = Resolve<IAttachmentStore>();
        var tenantContext = Resolve<ITenantContext>();
        var pricing = Resolve<VoicePricing>();

        // The configured model and format are the DEFAULT synthesizer's settings,
        // and this tool only speaks with the default one. A named synthesizer never
        // receives them: another engine's model id sent to it is a wrong request.
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
            throw new TraconException(
                $"The produced audio could not be saved: {validation.Error} " +
                $"The provider's output format is '{options.OutputFormat}'. " +
                "The attachment store validates the type from the magic bytes; formats without a container are rejected.");
        }

        var scope = TraconRunContext.Current;

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
                FileName = AttachmentFileNames.For("speech", validation.MediaType!, DateTimeOffset.UtcNow),
                MediaType = validation.MediaType!,
                Data = audio.Data,
                CreatedBy = scope?.AgentName,
            },
            cancellationToken).ConfigureAwait(false);

        ReportUsage(audio, pricing, synthesizer, request, text);

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
        ISpeechSynthesizer synthesizer,
        SpeechRequest request,
        string text)
    {
        var characters = audio.CharactersBilled ?? text.Length;

        // The model the synthesizer reports comes first: the price must follow
        // what produced the audio, not a setting.
        var model = audio.ModelId ?? request.ModelId;

        _ = TraconToolUsage.Report(new ToolCallUsage
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = characters,
            Cost = pricing.ForSynthesis(synthesizer.ProviderName, model, characters),
            Currency = pricing.Currency,
            IsEstimated = audio.UsageSource != SpeechUsageSource.Provider,
            Provider = synthesizer.ProviderName,
            Model = model,
        });
    }
}
