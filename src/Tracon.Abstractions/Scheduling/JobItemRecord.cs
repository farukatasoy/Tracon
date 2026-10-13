namespace Tracon;

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
    /// <remarks>
    /// A <see cref="JobItemStatus.Pending"/> item can carry a value too. A handler
    /// that can resume interrupted work writes the run identifier on the item
    /// BEFORE the run starts (a report with <see cref="JobItemStatus.Pending"/>),
    /// so a job leased again finds the run the item was processing. The built-in
    /// workflow handler does this when <c>TraconWorkflowOptions.ResumeInterruptedRuns</c>
    /// is on. When the item finishes, the value names the run that finished it.
    /// </remarks>
    public Guid? RunId { get; init; }

    /// <summary>The item's processing status.</summary>
    public required JobItemStatus Status { get; init; }

    /// <summary>The failure message. Populated only for <see cref="JobItemStatus.Failed"/>.</summary>
    public string? Error { get; init; }
}
