using System.Collections.Concurrent;

namespace Tracon;

/// <summary>A store that keeps eval suites, cases, and runs in process memory.</summary>
/// <remarks>
/// <strong>Limits:</strong> process lifetime and a single node. Use
/// <c>Tracon.PostgreSql</c> in production.
/// </remarks>
internal sealed class InMemoryEvalStore : IEvalStore
{
    private readonly ConcurrentDictionary<SuiteKey, EvalSuite> _suites = new();
    private readonly ConcurrentDictionary<Guid, List<EvalCase>> _cases = new();
    private readonly ConcurrentDictionary<Guid, EvalRun> _runs = new();
    private readonly ConcurrentDictionary<Guid, List<EvalCaseResult>> _caseResults = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<EvalSuite>> ListSuitesAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var suites = _suites
            .Where(pair => string.Equals(pair.Key.TenantId, tenantId, StringComparison.Ordinal))
            .Select(static pair => pair.Value)
            .OrderBy(static suite => suite.Name, StringComparer.Ordinal)
            .ToArray();

        return new ValueTask<IReadOnlyList<EvalSuite>>(suites);
    }

    /// <inheritdoc />
    public ValueTask<EvalSuite?> GetSuiteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        _suites.TryGetValue(new SuiteKey(tenantId, name), out var suite);
        return new ValueTask<EvalSuite?>(suite);
    }

    /// <inheritdoc />
    public ValueTask<EvalSuite> SaveSuiteAsync(EvalSuite suite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suite);
        cancellationToken.ThrowIfCancellationRequested();

        var key = new SuiteKey(suite.TenantId, suite.Name);
        var saved = _suites.AddOrUpdate(
            key,
            static (_, value) => Create(value),
            static (_, current, value) => Update(current, value),
            suite);

        return new ValueTask<EvalSuite>(saved);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteSuiteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_suites.TryRemove(new SuiteKey(tenantId, name), out var suite))
        {
            return new ValueTask<bool>(false);
        }

        _cases.TryRemove(suite.Id, out _);

        foreach (var run in _runs.Values.Where(run => run.SuiteId == suite.Id).ToArray())
        {
            _runs.TryRemove(run.Id, out _);
            _caseResults.TryRemove(run.Id, out _);
        }

        return new ValueTask<bool>(true);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<EvalCase>> ListCasesAsync(Guid suiteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_cases.TryGetValue(suiteId, out var cases))
        {
            return new ValueTask<IReadOnlyList<EvalCase>>([]);
        }

        EvalCase[] snapshot;

        lock (cases)
        {
            snapshot = [.. cases];
        }

        Array.Sort(snapshot, static (left, right) => left.Seq.CompareTo(right.Seq));

        return new ValueTask<IReadOnlyList<EvalCase>>(snapshot);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<EvalCase>> ReplaceCasesAsync(
        Guid suiteId,
        IReadOnlyList<EvalCase> cases,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);
        cancellationToken.ThrowIfCancellationRequested();

        var assigned = new List<EvalCase>(cases.Count);

        for (var seq = 0; seq < cases.Count; seq++)
        {
            var candidate = cases[seq];

            assigned.Add(candidate with
            {
                Id = candidate.Id == Guid.Empty ? TraconId.NewId() : candidate.Id,
                SuiteId = suiteId,
                Seq = seq,
            });
        }

        _cases[suiteId] = assigned;

        return new ValueTask<IReadOnlyList<EvalCase>>(assigned);
    }

    /// <inheritdoc />
    public ValueTask<EvalCaseAddResult> AddCaseAsync(
        Guid suiteId,
        EvalCaseDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        var cases = _cases.GetOrAdd(suiteId, static _ => []);

        lock (cases)
        {
            if (draft.SourceRunId is { } sourceRunId)
            {
                var existing = cases.Find(candidate => candidate.SourceRunId == sourceRunId);

                if (existing is not null)
                {
                    return new ValueTask<EvalCaseAddResult>(new EvalCaseAddResult { Case = existing, Created = false });
                }
            }

            var now = DateTimeOffset.UtcNow;
            var seq = cases.Count == 0 ? 0 : cases.Max(static candidate => candidate.Seq) + 1;

            var created = new EvalCase
            {
                Id = TraconId.NewId(now),
                SuiteId = suiteId,
                Seq = seq,
                Query = draft.Query,
                ExpectedOutput = draft.ExpectedOutput,
                ExpectedTools = draft.ExpectedTools,
                Context = draft.Context,
                SourceRunId = draft.SourceRunId,
                SourceKind = draft.SourceKind,
                PromotedAt = now,
            };

            cases.Add(created);

            return new ValueTask<EvalCaseAddResult>(new EvalCaseAddResult { Case = created, Created = true });
        }
    }

    /// <inheritdoc />
    public ValueTask<EvalRun> CreateRunAsync(EvalRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        var record = run with { Id = run.Id == Guid.Empty ? TraconId.NewId() : run.Id };
        _runs[record.Id] = record;

        return new ValueTask<EvalRun>(record);
    }

    /// <inheritdoc />
    public ValueTask MarkRunRunningAsync(
        Guid evalRunId,
        int? agentVersion,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_runs.TryGetValue(evalRunId, out var run))
        {
            _runs[evalRunId] = run with
            {
                Status = EvalRunStatus.Running,
                AgentVersion = agentVersion,
                ModelId = modelId,
            };
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask CompleteRunAsync(EvalRunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        if (_runs.TryGetValue(completion.EvalRunId, out var run))
        {
            _runs[completion.EvalRunId] = run with
            {
                Status = completion.Status,
                CompletedAt = completion.CompletedAt,
                Total = completion.Total,
                Passed = completion.Passed,
                Failed = completion.Failed,
                InputTokens = completion.InputTokens,
                OutputTokens = completion.OutputTokens,
            };
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<EvalRun?> GetRunAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_runs.TryGetValue(evalRunId, out var run) && string.Equals(run.TenantId, tenantId, StringComparison.Ordinal))
        {
            return new ValueTask<EvalRun?>(run);
        }

        return new ValueTask<EvalRun?>(result: null);
    }

    /// <inheritdoc />
    public ValueTask<EvalRun?> GetRunByJobIdAsync(
        string tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var run = _runs.Values.FirstOrDefault(candidate =>
            candidate.JobId == jobId && string.Equals(candidate.TenantId, tenantId, StringComparison.Ordinal));

        return new ValueTask<EvalRun?>(run);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<EvalRun>> QueryRunsAsync(
        EvalRunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = new List<EvalRun>();

        foreach (var run in _runs.Values)
        {
            // EvalRunQuery.TenantId is required, so this filter always runs.
            if (!string.Equals(run.TenantId, query.TenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.SuiteId is { } suiteId && run.SuiteId != suiteId)
            {
                continue;
            }

            matches.Add(run);
        }

        matches.Sort(static (left, right) => right.StartedAt.CompareTo(left.StartedAt));

        var start = Math.Clamp(query.Skip, 0, matches.Count);
        var count = Math.Clamp(query.Take, 0, matches.Count - start);

        return new ValueTask<IReadOnlyList<EvalRun>>(matches.GetRange(start, count));
    }

    /// <inheritdoc />
    public ValueTask RecordCaseResultAsync(EvalCaseResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var record = result.Id == Guid.Empty ? result with { Id = TraconId.NewId() } : result;
        var log = _caseResults.GetOrAdd(record.EvalRunId, static _ => []);

        lock (log)
        {
            log.Add(record);
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<EvalCaseResult>> ListCaseResultsAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_runs.TryGetValue(evalRunId, out var run) || !string.Equals(run.TenantId, tenantId, StringComparison.Ordinal))
        {
            return new ValueTask<IReadOnlyList<EvalCaseResult>>([]);
        }

        if (!_caseResults.TryGetValue(evalRunId, out var results))
        {
            return new ValueTask<IReadOnlyList<EvalCaseResult>>([]);
        }

        lock (results)
        {
            return new ValueTask<IReadOnlyList<EvalCaseResult>>(results.ToArray());
        }
    }

    /// <inheritdoc />
    public async ValueTask<EvalRunDiff?> DiffRunsAsync(
        EvalRunDiffQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var baseline = await GetRunAsync(query.TenantId, query.BaselineRunId, cancellationToken).ConfigureAwait(false);
        var candidate = await GetRunAsync(query.TenantId, query.CandidateRunId, cancellationToken).ConfigureAwait(false);

        if (baseline is null || candidate is null)
        {
            return null;
        }

        var baselineResults = await ListCaseResultsAsync(query.TenantId, baseline.Id, cancellationToken).ConfigureAwait(false);
        var candidateResults = await ListCaseResultsAsync(query.TenantId, candidate.Id, cancellationToken).ConfigureAwait(false);

        return EvalRunDiffBuilder.Build(baseline, candidate, baselineResults, candidateResults, query.Skip, query.Take);
    }

    private static EvalSuite Create(EvalSuite suite)
    {
        var now = DateTimeOffset.UtcNow;
        return suite with
        {
            Id = suite.Id == Guid.Empty ? TraconId.NewId(now) : suite.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static EvalSuite Update(EvalSuite current, EvalSuite suite)
        => suite with
        {
            Id = current.Id,
            CreatedAt = current.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private readonly record struct SuiteKey(string TenantId, string Name);
}
