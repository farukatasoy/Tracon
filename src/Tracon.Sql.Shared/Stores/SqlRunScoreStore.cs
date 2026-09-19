using System.Data.Common;

namespace Tracon;

/// <summary>Persists run and message scores.</summary>
/// <remarks>
/// The behavior contract is identical to <c>InMemoryRunScoreStore</c>;
/// it is guarded by the shared contract tests.
/// </remarks>
internal sealed class SqlRunScoreStore : IRunScoreStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new score store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">
    /// Resolves the "current tenant" fallback of <see cref="RunScoreQuery.TenantId"/>
    /// in <see cref="SummarizeAsync"/> -- every other member takes the tenant
    /// as an explicit parameter and does not need it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlRunScoreStore(SqlStoreContext context, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <summary>The upper bound on retries after a live UPDATE/INSERT race (see <see cref="UpsertAsync"/>).</summary>
    private const int MaxUpsertAttempts = 5;

    /// <summary>The upper bound (exclusive) of the random retry delay, in milliseconds.</summary>
    private const int MaxRetryDelayMilliseconds = 15;

    /// <inheritdoc />
    /// <remarks>
    /// The UPDATE-then-INSERT pattern this store's SQL Server dialect uses has
    /// a narrow window: two concurrent upserts of the SAME target can both
    /// find zero matching rows and both attempt to INSERT, and the loser trips
    /// the table's uniqueness index as a live <see cref="DbException"/>, same
    /// as every other two-branch upsert in this codebase that retries on
    /// <see cref="SqlDialect.IsUniqueViolation"/>. The retry re-runs the WHOLE
    /// upsert: its own UPDATE branch now finds the row the other writer just
    /// committed and updates it instead of inserting a second one.
    /// </remarks>
    public async ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        RunScoreRules.Validate(score);

        var id = score.Id == Guid.Empty ? TraconId.NewId() : score.Id;

        for (var attempt = 1; ; attempt++)
        {
            var command = _context.CreateCommand(_sql.UpsertRunScore);
            DbHelpers.Add(command, "id", id);
            DbHelpers.AddTenant(command, score.TenantId);
            DbHelpers.Add(command, "run_id", score.RunId);
            Dialect.AddText(command, "message_id", score.MessageId);
            DbHelpers.Add(command, "kind", (short)score.Kind);
            Dialect.AddDouble(command, "value", score.Value);
            DbHelpers.Add(command, "name", score.Name);
            Dialect.AddText(command, "text_value", score.TextValue);
            Dialect.AddText(command, "comment", score.Comment);
            DbHelpers.Add(command, "source", score.Source);
            Dialect.AddText(command, "author", score.Author);
            Dialect.AddTimestamp(command, "created_at", score.CreatedAt);
            Dialect.AddText(command, "evaluator_version", score.EvaluatorVersion);

            try
            {
                var saved = await DbHelpers.ReadSingleAsync(command, Read, cancellationToken).ConfigureAwait(false);

                return saved ?? score;
            }
            catch (DbException ex) when (Dialect.IsUniqueViolation(ex) && attempt < MaxUpsertAttempts)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Random.Shared.Next(1, MaxRetryDelayMilliseconds)),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RunScore>> ListAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = _context.CreateCommand(_sql.SelectRunScores);
        DbHelpers.AddTenant(command, tenantId);
        DbHelpers.Add(command, "run_id", runId);

        return await DbHelpers.ReadListAsync(command, Read, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid scoreId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = _context.CreateCommand(_sql.DeleteRunScore);
        DbHelpers.Add(command, "id", scoreId);
        DbHelpers.AddTenant(command, tenantId);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>Reads one row, addressing the columns of <c>RunScoreColumns</c> by ordinal.</summary>
    /// <remarks>
    /// The ordinals follow <c>SqlQueriesBase.RunScoreColumns</c> exactly.
    /// </remarks>
    // 🚨 A column added to RunScoreColumns has to be APPENDED: inserting one in
    // the middle shifts every field below, and the shift is SILENT whenever the
    // neighbouring types happen to agree. Phase 152 and phase 176 both appended.
    private static RunScore Read(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            RunId = reader.GetGuid(2),
            MessageId = DbHelpers.GetNullableString(reader, 3),
            Kind = (RunScoreKind)reader.GetInt16(4),
            Value = DbHelpers.GetNullableDouble(reader, 5),
            Comment = DbHelpers.GetNullableString(reader, 6),
            Source = reader.GetString(7),
            Author = DbHelpers.GetNullableString(reader, 8),
            CreatedAt = DbHelpers.GetTimestamp(reader, 9),
            Name = reader.GetString(10),
            TextValue = DbHelpers.GetNullableString(reader, 11),
            EvaluatorVersion = DbHelpers.GetNullableString(reader, 12),
        };

    /// <inheritdoc />
    public async ValueTask<RunScoreSummary> SummarizeAsync(RunScoreQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var maxRows = Math.Max(query.MaxRows, 0);

        List<RunScoreAggregate> byName;
        var categoriesByName = new Dictionary<string, List<(string Category, long Count)>>(StringComparer.Ordinal);
        List<RunScoreAggregate> byAuthor;
        List<RunScoreAggregate> bySource;
        List<RunScoreAggregate> byAgent;

        var summaryCommand = _context.CreateCommand(_sql.SelectRunScoreSummary);
        AddFilterParameters(summaryCommand, query);
        DbHelpers.Add(summaryCommand, "max_rows", maxRows);
        DbHelpers.Add(summaryCommand, "kind_categorical", (short)RunScoreKind.Categorical);

        await using (summaryCommand.ConfigureAwait(false))
        {
            var reader = await summaryCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                // First result set: the by-name breakdown.
                byName = await ReadAggregatesAsync(reader, cancellationToken).ConfigureAwait(false);

                // Second result set: category counts for Categorical names,
                // unbounded -- capped per name below, since the cap is PER
                // GROUP, not over the whole result set.
                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var name = reader.GetString(0);
                        var category = reader.GetString(1);
                        var count = reader.GetInt64(2);

                        if (!categoriesByName.TryGetValue(name, out var list))
                        {
                            list = [];
                            categoriesByName[name] = list;
                        }

                        list.Add((category, count));
                    }
                }

                byName = [.. byName.Select(aggregate => ApplyCategories(aggregate, categoriesByName, maxRows))];

                // Third result set: breakdown by author.
                byAuthor = await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)
                    ? await ReadAggregatesAsync(reader, cancellationToken).ConfigureAwait(false)
                    : [];

                // Fourth result set: breakdown by source.
                bySource = await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)
                    ? await ReadAggregatesAsync(reader, cancellationToken).ConfigureAwait(false)
                    : [];

                // Fifth result set: breakdown by agent.
                byAgent = await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)
                    ? await ReadAggregatesAsync(reader, cancellationToken).ConfigureAwait(false)
                    : [];
            }
        }

        IReadOnlyList<RunScoreBucketAggregate> series = [];

        // No cost is paid when no bucket was asked for (154, open question 5):
        // the series command is never even created.
        if (query.Bucket is { } bucket)
        {
            var seriesCommand = _context.CreateCommand(_sql.SelectRunScoreSeries);
            AddFilterParameters(seriesCommand, query);
            DbHelpers.Add(seriesCommand, "bucket_unit", BucketUnit(bucket));

            var byBucket = new List<(DateTimeOffset BucketStart, List<RunScoreAggregate> Groups)>();

            await using (seriesCommand.ConfigureAwait(false))
            {
                var reader = await seriesCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var bucketStart = DbHelpers.GetTimestamp(reader, 0);
                        var aggregate = ReadAggregate(reader, keyOrdinal: 1, kindOrdinal: 2, countOrdinal: 3);

                        if (byBucket.Count == 0 || byBucket[^1].BucketStart != bucketStart)
                        {
                            byBucket.Add((bucketStart, []));
                        }

                        byBucket[^1].Groups.Add(aggregate);
                    }
                }
            }

            // Each bucket's own groups are already ordered by count DESC (the
            // SQL ORDER BY), so a per-bucket Take is enough -- the same
            // ceiling that bounds every other breakdown (RunScoreQuery.MaxRows).
            series = [.. byBucket.Select(entry => new RunScoreBucketAggregate
            {
                BucketStart = entry.BucketStart,
                Groups = [.. entry.Groups
                    .OrderByDescending(static aggregate => aggregate.Count)
                    .ThenBy(static aggregate => aggregate.Key, StringComparer.Ordinal)
                    .Take(maxRows)],
            })];
        }

        return new RunScoreSummary
        {
            ByName = byName,
            ByAuthor = byAuthor,
            BySource = bySource,
            ByAgent = byAgent,
            Series = series,
        };
    }

    private void AddFilterParameters(DbCommand command, RunScoreQuery query)
    {
        DbHelpers.AddTenant(command, query.TenantId ?? _tenantContext.TenantId);
        Dialect.AddTimestamp(command, "from_ts", query.From);
        Dialect.AddTimestamp(command, "to_ts", query.To);
        Dialect.AddText(command, "score_name", query.ScoreName);
        Dialect.AddText(command, "source", query.Source);
        Dialect.AddText(command, "author", query.Author);
        Dialect.AddText(command, "agent_name", query.AgentName);
        DbHelpers.Add(command, "target", (short)query.Target);
    }

    private static string BucketUnit(RunScoreBucket bucket) => bucket switch
    {
        RunScoreBucket.Hour => "hour",
        RunScoreBucket.Day => "day",
        RunScoreBucket.Week => "week",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, message: null),
    };

    /// <summary>Reads every remaining row of the current result set as a (key, kind) aggregate.</summary>
    private static async ValueTask<List<RunScoreAggregate>> ReadAggregatesAsync(
        DbDataReader reader, CancellationToken cancellationToken)
    {
        var result = new List<RunScoreAggregate>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadAggregate(reader, keyOrdinal: 0, kindOrdinal: 1, countOrdinal: 2));
        }

        return result;
    }

    private static RunScoreAggregate ReadAggregate(DbDataReader reader, int keyOrdinal, int kindOrdinal, int countOrdinal)
        => new()
        {
            Key = reader.GetString(keyOrdinal),
            Kind = (RunScoreKind)reader.GetInt16(kindOrdinal),
            Count = reader.GetInt64(countOrdinal),
            NoValueCount = reader.GetInt64(countOrdinal + 1),
            Average = DbHelpers.GetNullableDouble(reader, countOrdinal + 2),
            Minimum = DbHelpers.GetNullableDouble(reader, countOrdinal + 3),
            Maximum = DbHelpers.GetNullableDouble(reader, countOrdinal + 4),
        };

    /// <summary>Attaches this name's category counts, capped at <paramref name="maxRows"/>.</summary>
    private static RunScoreAggregate ApplyCategories(
        RunScoreAggregate aggregate,
        Dictionary<string, List<(string Category, long Count)>> categoriesByName,
        int maxRows)
    {
        if (aggregate.Kind != RunScoreKind.Categorical || !categoriesByName.TryGetValue(aggregate.Key, out var categories))
        {
            return aggregate;
        }

        var kept = categories.Take(maxRows)
            .ToDictionary(static pair => pair.Category, static pair => pair.Count, StringComparer.Ordinal);

        return aggregate with
        {
            Categories = kept,
            TruncatedCategoryCount = Math.Max(0, categories.Count - kept.Count),
        };
    }
}
