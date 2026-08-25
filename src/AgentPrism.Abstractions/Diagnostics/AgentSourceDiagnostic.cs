namespace AgentPrism;

/// <summary>Describes one agent source registered in the catalog.</summary>
public sealed record AgentSourceDiagnostic
{
    /// <summary>Gets the source name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the source priority.</summary>
    public required int Priority { get; init; }

    /// <summary>Gets the implementation type name.</summary>
    public required string Implementation { get; init; }
}
