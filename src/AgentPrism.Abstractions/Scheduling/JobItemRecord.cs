namespace AgentPrism;

/// <summary>A single input of a batch job and that input's processing result.</summary>
public sealed record JobItemRecord
{
    /// <summary>The item identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The identifier of the job it belongs to.</summary>
    public required Guid JobId { get; init; }

    /// <summary>The sequence number within the job (starts at 0).</summary>
    public required int Seq { get; init; }

    /// <summary>This item's input text.</summary>
    public required string Input { get; init; }

    /// <summary>
    /// The identifier of the run record created while processing this item.
    /// <see langword="null"/> if the item has not been processed yet.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>The item's processing status.</summary>
    public required JobItemStatus Status { get; init; }

    /// <summary>The failure message. Populated only for <see cref="JobItemStatus.Failed"/>.</summary>
    public string? Error { get; init; }
}
