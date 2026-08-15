namespace AgentPrism;

/// <summary>Defines one experiment variant: its definition version and traffic weight.</summary>
public sealed record ExperimentVariant
{
    /// <summary>Gets the variant name, for example <c>"control"</c> or <c>"v3"</c>. It must be unique within the experiment.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the <see cref="AgentDefinition.Version"/> number to serve.</summary>
    public required int Version { get; init; }

    /// <summary>
    /// Gets the traffic weight from 0 through 100. The weights of all experiment
    /// variants must total exactly 100 or saving is rejected. See <see cref="Experiment"/>.
    /// </summary>
    public required int Weight { get; init; }
}
