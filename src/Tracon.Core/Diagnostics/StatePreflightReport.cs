namespace Tracon;

/// <summary>
/// What a read-only state preflight found: how many rows carry each stored
/// schema generation, and how a small sample of them behaved when this build
/// tried to read them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A sample is evidence, not proof.</strong> A report with no failures
/// says the rows that WERE read came back readable — it never says every row
/// is readable. <see cref="SamplePerGeneration"/> is part of the report for
/// exactly that reason: a reader who cannot see how much was checked cannot
/// judge what the absence of failures is worth.
/// </para>
/// <para>
/// The generation counts, by contrast, ARE complete: they come from an
/// aggregate over the whole table, so <see cref="HasUnreadableGeneration"/>
/// covers every stored row.
/// </para>
/// </remarks>
internal sealed record StatePreflightReport
{
    /// <summary>The persistence provider the report was read from, for example <c>PostgreSQL</c>.</summary>
    public required string ProviderName { get; init; }

    /// <summary>The session rows, grouped by the generation they were written with.</summary>
    public required IReadOnlyList<StateGenerationCount> Sessions { get; init; }

    /// <summary>The workflow checkpoint rows, grouped the same way.</summary>
    public required IReadOnlyList<StateGenerationCount> Checkpoints { get; init; }

    /// <summary>How many rows of each generation were sampled, at most.</summary>
    public required int SamplePerGeneration { get; init; }

    /// <summary>
    /// The number of sampled sessions this build genuinely deserialized
    /// through Microsoft Agent Framework.
    /// </summary>
    public required int DecodedSampleCount { get; init; }

    /// <summary>
    /// The number of sampled rows whose payload was only checked for
    /// structure, not decoded.
    /// </summary>
    /// <remarks>
    /// Two rows land here. A workflow checkpoint has no decoder outside a
    /// running workflow, so only its stored shape can be checked. A session
    /// whose state is encrypted at rest cannot be decoded by a process that
    /// holds no content protection key — which a preflight run from the CLI
    /// normally does not. Neither is a failure, and neither is proof of
    /// readability.
    /// </remarks>
    public required int StructureOnlySampleCount { get; init; }

    /// <summary>The sampled rows this build could not read, with the reason for each.</summary>
    public required IReadOnlyList<StateSampleFailure> SampleFailures { get; init; }

    /// <summary>The Microsoft Agent Framework version this process runs.</summary>
    public required string RunningMafVersion { get; init; }

    /// <summary>The total number of rows sampled, however deeply each was checked.</summary>
    public int SampledCount => DecodedSampleCount + StructureOnlySampleCount + SampleFailures.Count;

    /// <summary>The number of sampled rows that failed. The length of <see cref="SampleFailures"/>.</summary>
    /// <remarks>
    /// Derived rather than stored: a count kept beside the list it counts
    /// drifts away from it the first time one of the two is updated alone.
    /// </remarks>
    public int SampleFailureCount => SampleFailures.Count;

    /// <summary>Whether any stored row carries a generation newer than this build understands.</summary>
    public bool HasUnreadableGeneration =>
        Sessions.Any(static generation => !generation.ReadableByThisBuild)
        || Checkpoints.Any(static generation => !generation.ReadableByThisBuild);

    /// <summary>How many stored rows carry a generation newer than this build understands.</summary>
    public long UnreadableRecordCount =>
        Sessions.Concat(Checkpoints)
            .Where(static generation => !generation.ReadableByThisBuild)
            .Sum(static generation => generation.RecordCount);

    /// <summary>Whether nothing was found that would block this build from reading the stored state.</summary>
    public bool IsClean => !HasUnreadableGeneration && SampleFailures.Count == 0;
}

/// <summary>The number of stored rows carrying one schema generation, and whether this build can read them.</summary>
internal sealed record StateGenerationCount
{
    /// <summary>Which table the rows belong to.</summary>
    public required StatePreflightTarget Target { get; init; }

    /// <summary>
    /// The stored Tracon schema generation, or <see langword="null"/> for
    /// rows written before the generation was stamped.
    /// </summary>
    public required int? SchemaGeneration { get; init; }

    /// <summary>The number of rows carrying <see cref="SchemaGeneration"/>.</summary>
    public required long RecordCount { get; init; }

    /// <summary>Whether the running build understands <see cref="SchemaGeneration"/>.</summary>
    /// <remarks>
    /// A row is readable when its generation is at or below the one this build
    /// writes. An unstamped row is readable too — it predates stamping, so it
    /// cannot be from the future.
    /// </remarks>
    public required bool ReadableByThisBuild { get; init; }
}

/// <summary>One sampled row the running build could not read.</summary>
internal sealed record StateSampleFailure
{
    /// <summary>Which table the row belongs to.</summary>
    public required StatePreflightTarget Target { get; init; }

    /// <summary>The row identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The generation the row carries, or <see langword="null"/> if unstamped.</summary>
    public required int? SchemaGeneration { get; init; }

    /// <summary>
    /// The Microsoft Agent Framework version the row was written with, or
    /// <see langword="null"/> if it was written before version stamping existed.
    /// </summary>
    public required string? RecordedMafVersion { get; init; }

    /// <summary>What went wrong, in one sentence, with no connection details in it.</summary>
    public required string Reason { get; init; }
}
