namespace Tracon;

/// <summary>Represents the experiment variant to which a run request is deterministically assigned.</summary>
public sealed record ExperimentAssignment
{
    /// <summary>Gets the experiment identifier.</summary>
    public required Guid ExperimentId { get; init; }

    /// <summary>Gets the assigned variant name.</summary>
    public required string Variant { get; init; }

    /// <summary>Gets the definition version served by the assigned variant.</summary>
    public required int Version { get; init; }
}
