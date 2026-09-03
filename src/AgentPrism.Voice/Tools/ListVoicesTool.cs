using System.Text;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>Lists the available voices.</summary>
/// <remarks>
/// The call <strong>incurs no cost</strong>. The list is given to the model as
/// name and id, so the model can call the <c>speak</c> tool with the right voice id.
/// </remarks>
internal sealed class ListVoicesTool : VoiceToolBase
{
    /// <summary>Tool name.</summary>
    public const string ToolName = "list_voices";

    /// <summary>Maximum number of voices reported to the model.</summary>
    /// <remarks>
    /// The provider can return hundreds of voices; putting all of them in the
    /// context fills the window needlessly.
    /// </remarks>
    internal const int MaxListedVoices = 50;

    private const string Schema = """
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """;

    public ListVoicesTool(IServiceProvider services)
        : base(services, Schema)
    {
    }

    /// <inheritdoc />
    public override string Name => ToolName;

    /// <inheritdoc />
    public override string Description =>
        "Lists the voices available for speech synthesis. Returns a name, id, " +
        "and known safe attributes (for example gender) for each voice.";

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var synthesizer = Resolve<ISpeechSynthesizer>();
        var voices = await synthesizer.ListVoicesAsync(cancellationToken).ConfigureAwait(false);

        if (voices.Count == 0)
        {
            return "No voices available.";
        }

        var builder = new StringBuilder();

        foreach (var voice in voices.Take(MaxListedVoices))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(voice.Name).Append(" (").Append(voice.VoiceId).Append(')');

            if (voice.Category is { Length: > 0 } category)
            {
                builder.Append(" — ").Append(category);
            }

            if (voice.Attributes.TryGetValue(VoiceAttributeNames.Gender, out var gender))
            {
                builder.Append(" — ").Append(gender);
            }
        }

        if (voices.Count > MaxListedVoices)
        {
            builder.Append("\n… and ").Append(voices.Count - MaxListedVoices).Append(" more voices.");
        }

        return builder.ToString();
    }
}
