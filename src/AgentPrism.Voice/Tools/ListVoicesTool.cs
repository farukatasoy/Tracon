using System.Text;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>Kullanilabilir sesleri listeler.</summary>
/// <remarks>
/// Cagri ucret <strong>uretmez</strong>. Liste modele ad ve kimlik olarak
/// verilir; boylece model <c>speak</c> tool'unu dogru ses kimligiyle cagirabilir.
/// </remarks>
internal sealed class ListVoicesTool : VoiceToolBase
{
    /// <summary>Tool adi.</summary>
    public const string ToolName = "list_voices";

    /// <summary>Modele bildirilecek en fazla ses sayisi.</summary>
    /// <remarks>
    /// Saglayici yuzlerce ses dondurebilir; hepsini baglama koymak pencereyi
    /// gereksiz doldurur.
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
        "Seslendirmede kullanilabilecek sesleri listeler. Her ses icin ad ve kimlik doner.";

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
            return "Kullanilabilir ses yok.";
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
        }

        if (voices.Count > MaxListedVoices)
        {
            builder.Append("\n… ve ").Append(voices.Count - MaxListedVoices).Append(" ses daha.");
        }

        return builder.ToString();
    }
}
