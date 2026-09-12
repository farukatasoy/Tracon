namespace Tracon;

/// <summary>The store for evaluation (eval) suites, cases, and runs.</summary>
/// <remarks>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </remarks>
public interface IEvalStore
{
    /// <summary>Lists all of a tenant's suites.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The suites, ordered by name.</returns>
    ValueTask<IReadOnlyList<EvalSuite>> ListSuitesAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the suite matching the given name within the tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The suite name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The suite; <see langword="null"/> if it does not exist.</returns>
    ValueTask<EvalSuite?> GetSuiteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the suite.</summary>
    /// <param name="suite">The suite to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The suite with its identifier and timestamps assigned.</returns>
    ValueTask<EvalSuite> SaveSuiteAsync(EvalSuite suite, CancellationToken cancellationToken = default);

    /// <summary>Deletes the suite and all of its cases/runs (cascade).</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The suite name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the suite was deleted.</returns>
    ValueTask<bool> DeleteSuiteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a suite's cases, ordered by sequence number.</summary>
    /// <param name="suiteId">The suite identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cases.</returns>
    ValueTask<IReadOnlyList<EvalCase>> ListCasesAsync(Guid suiteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces all of a suite's cases with the given list.
    /// </summary>
    /// <param name="suiteId">The suite identifier.</param>
    /// <param name="cases">The new case list. Sequence numbers are assigned by list order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cases with identifiers assigned.</returns>
    ValueTask<IReadOnlyList<EvalCase>> ReplaceCasesAsync(
        Guid suiteId,
        IReadOnlyList<EvalCase> cases,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a SINGLE case to the suite. <c>Seq</c> is generated atomically by
    /// the store; the caller does not compute it. If the same
    /// <see cref="EvalCaseDraft.SourceRunId"/> is added a second time, the
    /// existing case is returned with <c>Created: false</c>.
    /// </summary>
    /// <param name="suiteId">The suite identifier.</param>
    /// <param name="draft">The draft of the case to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The added (or already existing) case, and whether it was created.</returns>
    ValueTask<EvalCaseAddResult> AddCaseAsync(
        Guid suiteId,
        EvalCaseDraft draft,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new run record (state <see cref="EvalRunStatus.Pending"/>).</summary>
    /// <param name="run">The run record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The run record with its identifier assigned.</returns>
    ValueTask<EvalRun> CreateRunAsync(EvalRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a run to <see cref="EvalRunStatus.Running"/> and records
    /// the agent version and model identifier being measured.
    /// </summary>
    /// <param name="evalRunId">The run identifier.</param>
    /// <param name="agentVersion">The agent definition version being measured.</param>
    /// <param name="modelId">The model identifier being measured.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask MarkRunRunningAsync(
        Guid evalRunId,
        int? agentVersion,
        string? modelId,
        CancellationToken cancellationToken = default);

    /// <summary>Finalizes a run and writes its summary counters.</summary>
    /// <param name="completion">The finalization information.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask CompleteRunAsync(EvalRunCompletion completion, CancellationToken cancellationToken = default);

    /// <summary>Fetches a single run record.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="evalRunId">The run identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record; <see langword="null"/> if it does not exist or belongs to another tenant.</returns>
    ValueTask<EvalRun?> GetRunAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the run a job record produced.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record; <see langword="null"/> if it does not exist.</returns>
    ValueTask<EvalRun?> GetRunByJobIdAsync(
        string tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists runs by filter. The newest record is returned first.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records.</returns>
    ValueTask<IReadOnlyList<EvalRun>> QueryRunsAsync(
        EvalRunQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Records a case result.</summary>
    /// <param name="result">The case result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask RecordCaseResultAsync(EvalCaseResult result, CancellationToken cancellationToken = default);

    /// <summary>Lists a run's case results.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="evalRunId">The run identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results.</returns>
    ValueTask<IReadOnlyList<EvalCaseResult>> ListCaseResultsAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default);

    /// <summary>Aligns two completed runs of the same suite, case by case.</summary>
    /// <param name="query">Which two runs to compare, and which page of the result to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The difference; <see langword="null"/> if either run does not exist or
    /// belongs to another tenant.
    /// </returns>
    /// <exception cref="EvalRunDiffUnavailableException">
    /// The two runs exist but cannot be compared: they measure different
    /// suites, one of them has not completed, or one of them holds results for
    /// fewer cases than its summary counts because retention removed some of
    /// them.
    /// <strong>An empty diff is never returned in their place</strong> — it
    /// would read as "nothing changed" and turn a CI gate green over a
    /// regression. Implementations delegate to
    /// <see cref="EvalRunDiffBuilder.Build"/> rather than restating the rules.
    /// </exception>
    ValueTask<EvalRunDiff?> DiffRunsAsync(
        EvalRunDiffQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of <see cref="IEvalStore.AddCaseAsync"/>.</summary>
public sealed record EvalCaseAddResult
{
    /// <summary>The added (or already existing) case.</summary>
    public required EvalCase Case { get; init; }

    /// <summary>
    /// Whether this call newly created the case, or returned an existing
    /// case previously promoted with the same <c>SourceRunId</c>.
    /// </summary>
    public required bool Created { get; init; }
}

/// <summary>A query for filtering the run list.</summary>
public sealed record EvalRunQuery
{
    // 🚨 Required, and deliberately not nullable. It used to be optional, and a
    // null here meant "every tenant" - while the sibling RunQuery.TenantId falls
    // back to the AMBIENT tenant for the same null. Two contracts with opposite
    // meanings for the same value is a trap for a consumer of this package, who
    // would write `new EvalRunQuery { SuiteId = x }` and silently receive other
    // tenants' runs. K-277 settled the principle: the tenant filter is not
    // optional.

    /// <summary>Fetches only this tenant's runs.</summary>
    /// <remarks>
    /// Required. The tenant filter is never optional: a query always states which
    /// tenant's runs it wants.
    /// </remarks>
    public required string TenantId { get; init; }

    /// <summary>Fetches only this suite's runs.</summary>
    public Guid? SuiteId { get; init; }

    /// <summary>The number of records to skip.</summary>
    public int Skip { get; init; }

    /// <summary>The maximum number of records to fetch.</summary>
    public int Take { get; init; } = 50;
}

/// <summary>Which two eval runs to compare, and which page of the result to read.</summary>
public sealed record EvalRunDiffQuery
{
    /// <summary>The tenant both runs must belong to.</summary>
    /// <remarks>
    /// Required. The tenant filter is never optional: a run belonging to
    /// another tenant reads exactly like a run that does not exist.
    /// </remarks>
    public required string TenantId { get; init; }

    /// <summary>The run being compared against (the older, known-good side).</summary>
    public required Guid BaselineRunId { get; init; }

    /// <summary>The run being judged.</summary>
    public required Guid CandidateRunId { get; init; }

    /// <summary>The number of aligned cases to skip.</summary>
    public int Skip { get; init; }

    /// <summary>The maximum number of aligned cases to fetch.</summary>
    /// <remarks>
    /// Paging touches <see cref="EvalRunDiff.Cases"/> only; the counters are
    /// always for the whole diff.
    /// </remarks>
    public int Take { get; init; } = 50;
}

/// <summary>The information needed to finalize a run.</summary>
public sealed record EvalRunCompletion
{
    /// <summary>The run identifier.</summary>
    public required Guid EvalRunId { get; init; }

    /// <summary>The final status.</summary>
    public required EvalRunStatus Status { get; init; }

    /// <summary>The completion time (UTC).</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>The total number of cases.</summary>
    public required int Total { get; init; }

    /// <summary>The number of cases that passed.</summary>
    public required int Passed { get; init; }

    /// <summary>The number of remaining (failed) cases.</summary>
    public required int Failed { get; init; }

    /// <summary>The total input token count.</summary>
    public long? InputTokens { get; init; }

    /// <summary>The total output token count.</summary>
    public long? OutputTokens { get; init; }
}
