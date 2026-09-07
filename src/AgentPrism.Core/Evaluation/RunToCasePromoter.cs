namespace AgentPrism;

/// <summary>
/// Promotes a production run to an evaluation case.
/// </summary>
/// <remarks>
/// <para>
/// The query text is read from the <see cref="RunEventType.RunStarted"/> event in
/// <c>run_events</c>; see <c>RunRecordingAgent.ExtractQuery</c>. It is the only durable
/// input location because a session is saved only after successful completion.
/// Therefore, the query of a failed run can never be read from a session. The original
/// session-based design always returned 422 for failed-run promotion.
/// </para>
/// <para>
/// Multi-turn behavior is detected by checking for an earlier run in this run's
/// session. When present, this run's <c>query</c> alone cannot reproduce the original
/// behavior because prior-turn context is missing. which forbids silent context loss.
/// </para>
/// </remarks>
internal sealed class RunToCasePromoter
{
    private readonly IRunStore _runs;
    private readonly IRunScoreStore _scores;
    private readonly IEvalStore _evalStore;

    /// <summary>Initializes a promotion service.</summary>
    public RunToCasePromoter(IRunStore runs, IRunScoreStore scores, IEvalStore evalStore)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(evalStore);

        _runs = runs;
        _scores = scores;
        _evalStore = evalStore;
    }

    /// <summary>Attempts to promote a run to the supplied suite.</summary>
    /// <param name="tenantId">The requesting tenant.</param>
    /// <param name="suite">The target evaluation suite.</param>
    /// <param name="runId">The run to promote.</param>
    /// <param name="sourceKindOverride">
    /// Overrides the promotion reason. When <see langword="null"/>, it is
    /// inferred from the run's status and score.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The promotion outcome.</returns>
    public async ValueTask<RunPromotionOutcome> PromoteAsync(
        string tenantId,
        EvalSuite suite,
        Guid runId,
        EvalCaseSource? sourceKindOverride,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(suite);

        var run = await _runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Not found" and "belongs to another tenant" return the same outcome.
        // A distinct outcome would leak existence, as with RunEndpoints.SaveFeedbackAsync.
        if (run is null || !string.Equals(run.TenantId, tenantId, StringComparison.Ordinal))
        {
            return new RunPromotionOutcome(RunPromotionStatus.RunNotFound, null);
        }

        var sourceKind = await ResolveSourceKindAsync(tenantId, run, sourceKindOverride, cancellationToken).ConfigureAwait(false);

        if (sourceKind is null)
        {
            return new RunPromotionOutcome(RunPromotionStatus.AmbiguousSource, null);
        }

        if (await HasEarlierRunInSameSessionAsync(tenantId, run, cancellationToken).ConfigureAwait(false))
        {
            return new RunPromotionOutcome(RunPromotionStatus.MultiTurn, null);
        }

        var events = await ReadEventsAsync(runId, cancellationToken).ConfigureAwait(false);

        var query = events
            .FirstOrDefault(static runEvent => runEvent.Type == RunEventType.RunStarted)
            ?.Text;

        if (string.IsNullOrWhiteSpace(query))
        {
            return new RunPromotionOutcome(RunPromotionStatus.NoQuery, null);
        }

        string? expectedOutput = null;
        IReadOnlyList<string> expectedTools = [];

        if (sourceKind == EvalCaseSource.ReferenceRun)
        {
            expectedOutput = ExtractOutputText(events);

            var invocations = await _runs.ListToolInvocationsAsync(runId, cancellationToken).ConfigureAwait(false);
            expectedTools = [.. invocations.Select(static invocation => invocation.ToolName).Distinct(StringComparer.Ordinal)];
        }

        var draft = new EvalCaseDraft
        {
            Query = query,
            ExpectedOutput = expectedOutput,
            ExpectedTools = expectedTools,
            SourceRunId = runId,
            SourceKind = sourceKind,
        };

        var added = await _evalStore.AddCaseAsync(suite.Id, draft, cancellationToken).ConfigureAwait(false);

        return new RunPromotionOutcome(
            added.Created ? RunPromotionStatus.Created : RunPromotionStatus.AlreadyExists,
            added.Case);
    }

    private async ValueTask<EvalCaseSource?> ResolveSourceKindAsync(
        string tenantId,
        RunRecord run,
        EvalCaseSource? sourceKindOverride,
        CancellationToken cancellationToken)
    {
        if (sourceKindOverride is { } overrideKind)
        {
            return overrideKind;
        }

        if (run.Status == RunStatus.Failed)
        {
            return EvalCaseSource.FailedRun;
        }

        // Only completed runs infer the source automatically. Running,
        // AwaitingInput, and Canceled require an explicit sourceKind because it
        // is ambiguous whether they are successful or failed.
        if (run.Status != RunStatus.Completed)
        {
            return null;
        }

        var scores = await _scores.ListAsync(tenantId, run.Id, cancellationToken).ConfigureAwait(false);

        // A null Value means no measurement was made, so neither lifted
        // comparison holds and the run is not treated as negatively scored.
        var isNegative = scores.Any(static score =>
            (score.Kind == RunScoreKind.Binary && score.Value == 0) ||
            (score.Kind == RunScoreKind.Stars && score.Value <= 2));

        return isNegative ? EvalCaseSource.NegativeScore : EvalCaseSource.ReferenceRun;
    }

    private async ValueTask<bool> HasEarlierRunInSameSessionAsync(
        string tenantId, RunRecord run, CancellationToken cancellationToken)
    {
        if (run.SessionId is not { Length: > 0 } sessionId)
        {
            return false;
        }

        var siblings = await _runs.QueryRunsAsync(
            new RunQuery
            {
                TenantId = tenantId,
                SessionId = sessionId,
                OnlyRootRuns = false,
                Take = 200,
            },
            cancellationToken).ConfigureAwait(false);

        return siblings.Any(sibling => sibling.Id != run.Id && sibling.StartedAt < run.StartedAt);
    }

    private async ValueTask<List<RunEvent>> ReadEventsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in _runs.ReadEventsAsync(runId, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>
    /// Extracts model output from the event stream.
    /// </summary>
    /// <remarks>
    /// When <see cref="RunEventType.MessageCompleted"/> exists, it is used for a
    /// non-streaming run. Otherwise, <see cref="RunEventType.MessageDelta"/>
    /// fragments are joined for a streaming run. The same pattern appears in
    /// <c>AgentPrism.Testing.RunAssertions.ShouldHaveOutputContaining</c>. Do not
    /// combine both, because that duplicates text for a non-streaming run.
    /// </remarks>
    private static string? ExtractOutputText(IReadOnlyList<RunEvent> events)
    {
        var completed = events
            .Where(static runEvent => runEvent.Type == RunEventType.MessageCompleted)
            .Select(static runEvent => runEvent.Text ?? string.Empty)
            .ToList();

        var output = completed.Count > 0
            ? string.Concat(completed)
            : string.Concat(events
                .Where(static runEvent => runEvent.Type == RunEventType.MessageDelta)
                .Select(static runEvent => runEvent.Text ?? string.Empty));

        return output.Length > 0 ? output : null;
    }
}

/// <summary>Defines the outcome of <see cref="RunToCasePromoter.PromoteAsync"/>.</summary>
public enum RunPromotionStatus
{
    /// <summary>The run does not exist or belongs to another tenant.</summary>
    RunNotFound,

    /// <summary>
    /// The run is neither <see cref="RunStatus.Failed"/> nor
    /// <see cref="RunStatus.Completed"/>, so the promotion reason must be explicit.
    /// </summary>
    AmbiguousSource,

    /// <summary>
    /// The run query could not be read: no <c>RunStarted</c> event exists, or it
    /// has empty text, such as for an old record or an empty input.
    /// </summary>
    NoQuery,

    /// <summary>An earlier run exists in this run's session.</summary>
    MultiTurn,

    /// <summary>The case was created by this call.</summary>
    Created,

    /// <summary>The same run was already promoted, so the existing case was returned.</summary>
    AlreadyExists,
}

/// <summary>Represents the <see cref="RunToCasePromoter.PromoteAsync"/> outcome and its case, when present.</summary>
public sealed record RunPromotionOutcome(RunPromotionStatus Status, EvalCase? Case);
