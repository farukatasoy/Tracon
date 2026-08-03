namespace AgentPrism;

/// <summary>Bir calistirma istegin deterministik olarak baglandigi deney kolu.</summary>
public sealed record ExperimentAssignment
{
    /// <summary>Deney kimligi.</summary>
    public required Guid ExperimentId { get; init; }

    /// <summary>Atanan kolun adi.</summary>
    public required string Variant { get; init; }

    /// <summary>Atanan kolun sunacagi tanim surumu.</summary>
    public required int Version { get; init; }
}
