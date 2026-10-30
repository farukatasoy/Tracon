using Microsoft.Extensions.AI;

namespace Tracon.Api;

/// <summary>
/// A sample tool that makes no sound and bills what the local "tone" engine would charge for the text
/// it narrates, so a run shows a priced tool call.
/// </summary>
/// <remarks>
/// <para>
/// It is the tool counterpart of the <c>speak</c> workflow node. The tool reports the characters it
/// billed from its own body (<see cref="TraconToolUsage"/>), and the amount joins the run's budget at
/// once. When the call tree's budget is spent, the next <c>narrate</c> call does not start.
/// </para>
/// <para>
/// The price table is read at setup, from the service provider the registration factory receives:
/// Microsoft Agent Framework passes a tool an empty provider at call time.
/// </para>
/// </remarks>
internal static class NarrateTool
{
    /// <summary>The tool name an agent definition lists in <c>ToolNames</c>.</summary>
    public const string Name = "narrate";

    /// <summary>Builds the tool with the host's price table.</summary>
    /// <param name="rates">What the local engine charges.</param>
    /// <returns>The tool.</returns>
    public static AIFunction Create(TonePricing rates)
    {
        ArgumentNullException.ThrowIfNull(rates);

        return AIFunctionFactory.Create(
            (string text) =>
            {
                _ = TraconToolUsage.Report(rates.Characters(text.Length, isEstimated: true));

                return $"Narrated {text.Length} characters.";
            },
            Name,
            "Narrates a text with the local tone engine and reports the characters it billed.");
    }
}
