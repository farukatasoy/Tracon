namespace Tracon;

/// <summary>Structured response validation settings.</summary>
/// <remarks>
/// Read from the <c>Tracon:StructuredResponse</c> configuration section. See
/// <c>TraconServiceCollectionExtensions.AddTracon</c>.
/// </remarks>
public sealed class TraconStructuredResponseOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:StructuredResponse";

    /// <summary>
    /// Whether a run's response is checked against the agent's requested
    /// <see cref="AgentResponseFormat"/> before the run closes. Defaults to
    /// <see langword="false"/>: today's behaviour (a response is never
    /// inspected after the model returns it) does not change unless a setup
    /// opts in.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How many REPAIR turns may follow an invalid response. <c>0</c> (the
    /// default) disables repair entirely: an invalid response fails the run
    /// exactly as it does today. A value of <c>2</c> permits at most THREE
    /// model calls in total — the original turn plus two repairs.
    /// </summary>
    public int MaxRepairAttempts { get; set; }
}
