namespace Tracon;

/// <summary>The outcome of a data subject erasure request.</summary>
public sealed record DataSubjectErasureResult
{
    /// <summary>
    /// Gets whether this was a preview: <see langword="true"/> means
    /// <see cref="RowsByTarget"/> shows what WOULD be deleted and nothing was
    /// actually removed.
    /// </summary>
    public required bool DryRun { get; init; }

    /// <summary>Gets the number of rows removed (or, for a preview, that would be removed), by target table.</summary>
    public required IReadOnlyDictionary<string, int> RowsByTarget { get; init; }

    /// <summary>
    /// Gets the subject's runs that are still <c>Running</c> or <c>Queued</c>. A run in
    /// progress writes its session and its checkpoints back after an erasure commits,
    /// so a real erasure is refused with <c>409</c> while this is not empty, unless it
    /// is forced. Empty when no run of the subject is in progress.
    /// </summary>
    public IReadOnlyList<Guid> ActiveRunIds { get; init; } = [];
}
