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
}
