namespace Tracon.Samples.FileRunStore;

/// <summary>
/// A non-persistent <see cref="IRunScoreStore"/>, used as
/// <see cref="JsonFileRunStore"/>'s default when the caller supplies none.
/// </summary>
/// <remarks>
/// Scores are written far less often than run records and are not the focus
/// of this sample; a real deployment would give <see cref="JsonFileRunStore"/>
/// its own persistent <see cref="IRunScoreStore"/> instead.
/// </remarks>
/// <param name="tenantContext">
/// Resolves the "current tenant" fallback of <see cref="RunScoreQuery.TenantId"/>.
/// Defaults to a fixed <c>"default"</c> tenant when not given.
/// </param>
/// <param name="agentNameResolver">
/// Resolves a run's agent name for the <see cref="RunScoreQuery.AgentName"/>
/// filter and the <see cref="RunScoreSummary.ByAgent"/> breakdown -- a plain
/// <see cref="RunScore"/> carries no agent name of its own. Left
/// <see langword="null"/>, that filter matches nothing and the breakdown
/// stays empty. <see cref="JsonFileRunStore"/> wires this to its own run
/// dictionary when it constructs the default score store.
/// </param>
internal sealed class InMemoryRunScoreStore(
    ITenantContext? tenantContext = null,
    Func<Guid, string?>? agentNameResolver = null) : IRunScoreStore
{
    private readonly Lock _gate = new();
    private readonly List<RunScore> _scores = [];

    /// <inheritdoc />
    public ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        RunScoreRules.Validate(score);

        lock (_gate)
        {
            var id = score.Id == Guid.Empty ? Guid.NewGuid() : score.Id;

            // The same author writing the same NAME onto the same target a
            // second time updates the row instead of opening a new one; a
            // different name, or an author-less score, opens a new row.
            var existingIndex = score.Author is { Length: > 0 }
                ? _scores.FindIndex(candidate =>
                    candidate.RunId == score.RunId
                    && string.Equals(candidate.MessageId, score.MessageId, StringComparison.Ordinal)
                    && string.Equals(candidate.Author, score.Author, StringComparison.Ordinal)
                    && string.Equals(candidate.Name, score.Name, StringComparison.Ordinal))
                : -1;

            var stored = score with { Id = id };

            if (existingIndex >= 0)
            {
                stored = stored with { Id = _scores[existingIndex].Id };
                _scores[existingIndex] = stored;
            }
            else
            {
                _scores.Add(stored);
            }

            return new ValueTask<RunScore>(stored);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunScore>> ListAsync(string tenantId, Guid runId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<RunScore> matches =
            [
                .. _scores.Where(score =>
                    score.RunId == runId
                    && string.Equals(score.TenantId, tenantId, StringComparison.Ordinal)),
            ];

            return new ValueTask<IReadOnlyList<RunScore>>(matches);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string tenantId, Guid scoreId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _scores.FindIndex(score =>
                score.Id == scoreId && string.Equals(score.TenantId, tenantId, StringComparison.Ordinal));

            if (index < 0)
            {
                return new ValueTask<bool>(false);
            }

            _scores.RemoveAt(index);
            return new ValueTask<bool>(true);
        }
    }

    /// <inheritdoc />
    public ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = query.TenantId ?? tenantContext?.TenantId ?? throw new ArgumentException(
            "RunScoreQuery.TenantId is required: this store was created without an ambient ITenantContext.",
            nameof(query));

        var maxRows = Math.Max(query.MaxRows, 0);

        lock (_gate)
        {
            var matches = _scores.Where(score =>
                string.Equals(score.TenantId, tenantId, StringComparison.Ordinal) &&
                (query.From is not { } from || score.CreatedAt >= from) &&
                (query.To is not { } to || score.CreatedAt < to) &&
                (query.ScoreName is not { } name || string.Equals(score.Name, name, StringComparison.Ordinal)) &&
                (query.Source is not { } source || string.Equals(score.Source, source, StringComparison.Ordinal)) &&
                (query.Author is not { } author || string.Equals(score.Author, author, StringComparison.Ordinal)) &&
                query.Target switch
                {
                    RunScoreTarget.Run => score.MessageId is not { Length: > 0 },
                    RunScoreTarget.Message => score.MessageId is { Length: > 0 },
                    _ => true,
                });

            if (query.AgentName is { } agentNameFilter)
            {
                // A filter that cannot be resolved matches nothing -- never
                // silently ignored, which would leak scores past a caller's
                // explicit narrowing.
                matches = agentNameResolver is null
                    ? []
                    : matches.Where(score => string.Equals(agentNameResolver(score.RunId), agentNameFilter, StringComparison.Ordinal));
            }

            var matchList = matches.ToList();

            var byName = BuildAggregates(matchList.Select(static score => (score.Name, score)), maxRows, includeCategories: true);
            var byAuthor = BuildAggregates(
                matchList.Where(static score => score.Author is { Length: > 0 }).Select(static score => (score.Author!, score)),
                maxRows,
                includeCategories: false);
            var bySource = BuildAggregates(matchList.Select(static score => (score.Source, score)), maxRows, includeCategories: false);

            var byAgent = agentNameResolver is null
                ? []
                : BuildAggregates(
                    matchList
                        .Select(score => (AgentName: agentNameResolver(score.RunId), Score: score))
                        .Where(static pair => pair.AgentName is { Length: > 0 })
                        .Select(static pair => (pair.AgentName!, pair.Score)),
                    maxRows,
                    includeCategories: false);

            var series = query.Bucket is { } bucket
                ? matchList
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

            return new ValueTask<RunScoreSummary>(new RunScoreSummary
            {
                ByName = byName,
                ByAuthor = byAuthor,
                BySource = bySource,
                ByAgent = byAgent,
                Series = series,
            });
        }
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
}
