using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// A store that keeps run and message scores in process memory.
/// </summary>
/// <remarks>
/// Its behavior contract exactly matches persistent implementations such as
/// <c>SqlRunScoreStore</c>, and shared contract tests protect it. Use
/// <c>Tracon.PostgreSql</c>, SQL Server, or SQLite in production.
/// </remarks>
/// <param name="tenantContext">
/// Resolves the "current tenant" fallback of <see cref="RunScoreQuery.TenantId"/>.
/// Optional: a caller that always sets <see cref="RunScoreQuery.TenantId"/>
/// explicitly does not need one — <see cref="SummarizeAsync"/> throws only if
/// both are missing.
/// </param>
/// <param name="agentNameResolver">
/// Resolves a run's agent name for the <see cref="RunScoreQuery.AgentName"/>
/// filter and the <see cref="RunScoreSummary.ByAgent"/> breakdown. A plain
/// <see cref="RunScore"/> carries no agent name of its own -- scores follow a
/// separate lifecycle from <c>IRunStore</c>'s hot path -- so this store
/// cannot look it up on its own without creating a
/// constructor-time circular dependency on <c>IRunStore</c> (which itself
/// depends on <see cref="IRunScoreStore"/> for its own statistics). Left
/// <see langword="null"/>, <see cref="RunScoreQuery.AgentName"/> matches
/// nothing (never silently ignored) and <see cref="RunScoreSummary.ByAgent"/>
/// stays empty. The production DI registration wires this to
/// <c>IRunStore.GetRunAsync</c>, resolved lazily so the two singletons can
/// still reference each other.
/// </param>
internal sealed class InMemoryRunScoreStore(
    ITenantContext? tenantContext = null,
    Func<Guid, CancellationToken, ValueTask<string?>>? agentNameResolver = null) : IRunScoreStore
{
    private readonly ConcurrentDictionary<Guid, RunScore> _scores = new();

    /// <inheritdoc />
    public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        RunScoreRules.Validate(score);

        // When the same author writes the same NAME onto the same target, run or
        // message, a second time, update the existing row. A different name from the
        // same author opens a new row. If the author is empty in an anonymous
        // deployment, the rule does not apply and each call creates a new row. This
        // matches the unique index in SQL providers. See the run_scores migration.
        if (score.Author is { Length: > 0 })
        {
            var existing = _scores.Values.FirstOrDefault(candidate => IsSameTarget(candidate, score));

            if (existing is not null)
            {
                var updated = score with { Id = existing.Id };
                _scores[existing.Id] = updated;

                return new ValueTask<RunScore>(updated);
            }
        }

        var created = score with { Id = score.Id == Guid.Empty ? TraconId.NewId() : score.Id };
        _scores[created.Id] = created;

        return new ValueTask<RunScore>(created);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunScore>> ListAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        IReadOnlyList<RunScore> result =
        [
            .. _scores.Values.Where(score =>
                score.RunId == runId && string.Equals(score.TenantId, tenantId, StringComparison.Ordinal)),
        ];

        return new ValueTask<IReadOnlyList<RunScore>>(result);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid scoreId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (_scores.TryGetValue(scoreId, out var existing)
            && string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal))
        {
            return new ValueTask<bool>(_scores.TryRemove(scoreId, out _));
        }

        return new ValueTask<bool>(false);
    }

    /// <inheritdoc />
    public async ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = query.TenantId ?? tenantContext?.TenantId ?? throw new ArgumentException(
            "RunScoreQuery.TenantId is required: this store was created without an ambient ITenantContext.",
            nameof(query));

        var maxRows = Math.Max(query.MaxRows, 0);

        List<RunScore> matches = [];

        foreach (var score in _scores.Values)
        {
            if (!string.Equals(score.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.From is { } from && score.CreatedAt < from)
            {
                continue;
            }

            if (query.To is { } to && score.CreatedAt >= to)
            {
                continue;
            }

            if (query.ScoreName is { } name && !string.Equals(score.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.Source is { } source && !string.Equals(score.Source, source, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.Author is { } author && !string.Equals(score.Author, author, StringComparison.Ordinal))
            {
                continue;
            }

            // 🚨 MessageId itself is never a breakdown dimension (unbounded
            // cardinality); Target only ever narrows to "has one" or "has none".
            var isMessageScore = score.MessageId is { Length: > 0 };

            if ((query.Target == RunScoreTarget.Run && isMessageScore) ||
                (query.Target == RunScoreTarget.Message && !isMessageScore))
            {
                continue;
            }

            matches.Add(score);
        }

        // Resolved once per distinct run, reused by both the AgentName filter
        // and the ByAgent breakdown, whether or not the filter is set.
        Dictionary<Guid, string?> agentNames = [];

        if (agentNameResolver is not null)
        {
            foreach (var runId in matches.Select(static score => score.RunId).Distinct())
            {
                agentNames[runId] = await agentNameResolver(runId, cancellationToken).ConfigureAwait(false);
            }
        }

        if (query.AgentName is { } agentNameFilter)
        {
            // A filter that cannot be resolved matches nothing -- never
            // silently ignored, which would leak scores past a caller's
            // explicit narrowing.
            matches = agentNameResolver is null
                ? []
                : [.. matches.Where(score =>
                    string.Equals(agentNames.GetValueOrDefault(score.RunId), agentNameFilter, StringComparison.Ordinal))];
        }

        var byName = BuildAggregates(matches.Select(static score => (score.Name, score)), maxRows, includeCategories: true);

        var byAuthor = BuildAggregates(
            matches.Where(static score => score.Author is { Length: > 0 }).Select(static score => (score.Author!, score)),
            maxRows,
            includeCategories: false);

        var bySource = BuildAggregates(matches.Select(static score => (score.Source, score)), maxRows, includeCategories: false);

        var byAgent = agentNameResolver is null
            ? []
            : BuildAggregates(
                matches
                    .Where(score => agentNames.GetValueOrDefault(score.RunId) is { Length: > 0 })
                    .Select(score => (agentNames[score.RunId]!, score)),
                maxRows,
                includeCategories: false);

        var series = query.Bucket is { } bucket
            ? matches
                .GroupBy(score => RunScoreBucketing.Truncate(score.CreatedAt, bucket))
                .OrderBy(static group => group.Key)
                .Select(group => new RunScoreBucketAggregate
                {
                    BucketStart = group.Key,
                    Groups = BuildAggregates(
                        group.Select(static score => (score.Name, score)), maxRows, includeCategories: false),
                })
                .ToList()
            : (IReadOnlyList<RunScoreBucketAggregate>)[];

        return new RunScoreSummary
        {
            ByName = byName,
            ByAuthor = byAuthor,
            BySource = bySource,
            ByAgent = byAgent,
            Series = series,
        };
    }

    /// <summary>Groups scores by (key, kind), aggregates each group, and caps the result at <paramref name="maxRows"/>.</summary>
    private static List<RunScoreAggregate> BuildAggregates(
        IEnumerable<(string Key, RunScore Score)> items,
        int maxRows,
        bool includeCategories)
        => [.. items
            .GroupBy(static item => (item.Key, item.Score.Kind))
            .Select(group =>
            {
                var scores = group.Select(static item => item.Score).ToList();
                var withValue = scores.Where(static score => score.Value is not null).ToList();

                var aggregate = new RunScoreAggregate
                {
                    Key = group.Key.Key,
                    Kind = group.Key.Kind,
                    Count = scores.Count,
                    NoValueCount = scores.Count - withValue.Count,
                    Average = withValue.Count > 0 ? withValue.Average(static score => score.Value!.Value) : null,
                    Minimum = withValue.Count > 0 ? withValue.Min(static score => score.Value!.Value) : null,
                    Maximum = withValue.Count > 0 ? withValue.Max(static score => score.Value!.Value) : null,
                };

                if (!includeCategories || group.Key.Kind != RunScoreKind.Categorical)
                {
                    return aggregate;
                }

                var categoryCounts = scores
                    .Where(static score => score.TextValue is { Length: > 0 })
                    .GroupBy(static score => score.TextValue!, StringComparer.Ordinal)
                    .Select(static categoryGroup => (Category: categoryGroup.Key, Count: (long)categoryGroup.Count()))
                    .OrderByDescending(static pair => pair.Count)
                    .ThenBy(static pair => pair.Category, StringComparer.Ordinal)
                    .ToList();

                var kept = categoryCounts.Take(maxRows)
                    .ToDictionary(static pair => pair.Category, static pair => pair.Count, StringComparer.Ordinal);

                return aggregate with
                {
                    Categories = kept,
                    TruncatedCategoryCount = Math.Max(0, categoryCounts.Count - kept.Count),
                };
            })
            .OrderByDescending(static aggregate => aggregate.Count)
            .ThenBy(static aggregate => aggregate.Key, StringComparer.Ordinal)
            .Take(maxRows)];

    private static bool IsSameTarget(RunScore left, RunScore right)
        => string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal)
           && left.RunId == right.RunId
           && string.Equals(left.MessageId ?? string.Empty, right.MessageId ?? string.Empty, StringComparison.Ordinal)
           && string.Equals(left.Author, right.Author, StringComparison.Ordinal)
           && string.Equals(left.Name, right.Name, StringComparison.Ordinal);
}
