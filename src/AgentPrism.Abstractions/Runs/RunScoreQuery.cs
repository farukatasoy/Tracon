using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// The filter for a score summary query (<see cref="IRunScoreStore.SummarizeAsync"/>).
/// </summary>
/// <remarks>
/// A query, not a counter: it reads the rows <see cref="IRunScoreStore.UpsertAsync"/>
/// already wrote. There is no offset-based paging beyond <see cref="MaxRows"/> — a
/// summary is a dashboard input, not a list endpoint.
/// </remarks>
public sealed record RunScoreQuery
{
    /// <summary>The tenant filter. The current tenant is used if left empty.</summary>
    public string? TenantId { get; init; }

    /// <summary>Count only scores created at or after this moment.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Count only scores created before this moment.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Count only this score name (<see cref="RunScore.Name"/>).</summary>
    public string? ScoreName { get; init; }

    /// <summary>Count only scores whose run belongs to this agent.</summary>
    public string? AgentName { get; init; }

    /// <summary>Count only this source: <c>human</c>, <c>api</c>, or <c>judge:{name}</c>.</summary>
    public string? Source { get; init; }

    /// <summary>Count only this author.</summary>
    public string? Author { get; init; }

    /// <summary>Whether run-level scores, message-level scores, or both are counted.</summary>
    /// <remarks>
    /// <see cref="RunScore.MessageId"/> itself is never a breakdown dimension — its
    /// cardinality is unbounded, and a message-level score must not fall into the
    /// same bucket as a run-level one.
    /// </remarks>
    public RunScoreTarget Target { get; init; } = RunScoreTarget.Any;

    /// <summary>
    /// The time bucket of the trend series (<see cref="RunScoreSummary.Series"/>).
    /// <see langword="null"/> returns no series, and computes none — the summary
    /// still returns its breakdowns.
    /// </summary>
    public RunScoreBucket? Bucket { get; init; }

    /// <summary>
    /// The maximum number of rows returned in every breakdown
    /// (<see cref="RunScoreSummary.ByName"/>, <see cref="RunScoreSummary.ByAuthor"/>,
    /// <see cref="RunScoreSummary.BySource"/>, <see cref="RunScoreSummary.ByAgent"/>,
    /// and each bucket of <see cref="RunScoreSummary.Series"/>) and of the distinct
    /// categories kept inside one <see cref="RunScoreAggregate.Categories"/> map.
    /// </summary>
    public int MaxRows { get; init; } = 20;
}

/// <summary>Which target a score belongs to — the filter <see cref="RunScoreQuery.Target"/> narrows to.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RunScoreTarget>))]
public enum RunScoreTarget
{
    /// <summary>Both run-level and message-level scores are counted.</summary>
    Any = 0,

    /// <summary>Only run-level scores (<see cref="RunScore.MessageId"/> empty).</summary>
    Run = 1,

    /// <summary>Only message-level scores (<see cref="RunScore.MessageId"/> set).</summary>
    Message = 2,
}

/// <summary>A closed, low-cardinality set of time buckets for a score trend series.</summary>
/// <remarks>
/// Deliberately not a free time span: a free bucket width needs its own
/// date-truncation expression in each of the three SQL providers and would
/// silently drift between them. Every bucket boundary is computed in UTC; a
/// week starts on Monday (ISO 8601), independent of server locale.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunScoreBucket>))]
public enum RunScoreBucket
{
    /// <summary>One hour, truncated to the hour boundary (UTC).</summary>
    Hour = 1,

    /// <summary>One day, truncated to midnight (UTC).</summary>
    Day = 2,

    /// <summary>One week, truncated to the Monday preceding it (UTC).</summary>
    Week = 3,
}
