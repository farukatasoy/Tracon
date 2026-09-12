namespace Tracon;

/// <summary>The result of <see cref="IRunScoreStore.SummarizeAsync"/>.</summary>
/// <remarks>Every list is empty, never <see langword="null"/>, when nothing matches the filter.</remarks>
public sealed record RunScoreSummary
{
    /// <summary>The breakdown by score name — the primary breakdown, always populated.</summary>
    public required IReadOnlyList<RunScoreAggregate> ByName { get; init; }

    /// <summary>The breakdown by author. Scores with no author (an identity-less setup) are excluded.</summary>
    public IReadOnlyList<RunScoreAggregate> ByAuthor { get; init; } = [];

    /// <summary>The breakdown by source (<c>human</c>, <c>api</c>, <c>judge:{name}</c>).</summary>
    public IReadOnlyList<RunScoreAggregate> BySource { get; init; } = [];

    /// <summary>The breakdown by the agent that produced the scored run.</summary>
    public IReadOnlyList<RunScoreAggregate> ByAgent { get; init; } = [];

    /// <summary>
    /// The trend series, one entry per time bucket. Empty when
    /// <c>Bucket</c> (<see cref="RunScoreQuery"/>) was not given. Buckets with no matching
    /// score are NOT included — this is a sparse series, not a filled one.
    /// </summary>
    public IReadOnlyList<RunScoreBucketAggregate> Series { get; init; } = [];
}

/// <summary>
/// One aggregated group: every score sharing a breakdown key AND a
/// <see cref="RunScoreKind"/>.
/// </summary>
/// <remarks>
/// The key is deliberately compound. Two different <see cref="RunScoreKind"/>
/// values never share a group — averaging a 1-5 star rating together with a
/// 0-100 numeric score under the same name would be meaningless. This means the
/// same score name (or author, source, agent) can appear more than once in a
/// breakdown list: once per <see cref="RunScoreKind"/> it was actually written
/// with.
/// </remarks>
public sealed record RunScoreAggregate
{
    /// <summary>
    /// The breakdown key: a score name, an author, a source, or an agent name,
    /// depending on which list this entry is in.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>The shape every score in this group shares.</summary>
    public required RunScoreKind Kind { get; init; }

    /// <summary>The number of scores in the group, INCLUDING those carrying no value.</summary>
    public required long Count { get; init; }

    /// <summary>
    /// The number of scores in the group carrying no value
    /// (<c>Value</c> is <see langword="null"/>). A
    /// <see cref="RunScoreKind.Categorical"/> score always carries no value, so
    /// for that kind this equals <c>Count</c>. Never counted into
    /// <c>Average</c>.
    /// </summary>
    public long NoValueCount { get; init; }

    /// <summary>
    /// The average value. <see langword="null"/> for
    /// <see cref="RunScoreKind.Categorical"/>, or when every score in the group
    /// carries no value.
    /// </summary>
    public double? Average { get; init; }

    /// <summary>The smallest value. Same null rule as <c>Average</c>.</summary>
    public double? Minimum { get; init; }

    /// <summary>The largest value. Same null rule as <c>Average</c>.</summary>
    public double? Maximum { get; init; }

    /// <summary>
    /// Count per category (<c>TextValue</c>), highest count
    /// first. Populated only on <see cref="RunScoreSummary.ByName"/> entries
    /// whose <see cref="Kind"/> is <see cref="RunScoreKind.Categorical"/>; every
    /// other group carries an empty map.
    /// </summary>
    public IReadOnlyDictionary<string, long> Categories { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>
    /// The number of distinct categories that did NOT fit inside
    /// <see cref="Categories"/> because <c>MaxRows</c> (<see cref="RunScoreQuery"/>) was
    /// reached. Zero when nothing was cut. Reported instead of silently
    /// dropping categories, which would read as "these categories do not exist".
    /// </summary>
    public long TruncatedCategoryCount { get; init; }
}

/// <summary>One time bucket of the trend series (<see cref="RunScoreSummary.Series"/>).</summary>
public sealed record RunScoreBucketAggregate
{
    /// <summary>The bucket's start time (UTC).</summary>
    public required DateTimeOffset BucketStart { get; init; }

    /// <summary>
    /// The name/kind groups scored inside this bucket — grouped exactly like
    /// <see cref="RunScoreSummary.ByName"/>, bounded by
    /// <see cref="RunScoreQuery.MaxRows"/>. Every entry's <see cref="RunScoreAggregate.Categories"/>
    /// is empty; a categorical breakdown over time is out of scope for this series.
    /// </summary>
    public required IReadOnlyList<RunScoreAggregate> Groups { get; init; }
}
