using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Executes <see cref="JobKind.OnlineEval"/> jobs: scores a sampled production
/// run with every registered <see cref="IRunJudge"/>.
/// </summary>
/// <remarks>
/// <para>
/// A judge's failure does NOT affect the run being scored. The run has
/// already completed; this job runs in the background. This rule is a direct
/// application of the principle that observability must not break functionality.
/// </para>
/// <para>
/// The score row's <see cref="RunScore.Author"/> field is INTENTIONALLY filled
/// with <c>judge:{name}</c> (NOT <see langword="null"/> like a human score):
/// this way the <c>run_scores</c> table's <c>(tenant_id, run_id, message_id, author)</c>
/// uniqueness constraint kicks in, and retrying this job or manually
/// repeating it via <c>POST /api/runs/{id}/judge</c> produces an update of the
/// existing row for the SAME judge, NOT a second row.
/// </para>
/// <para>
/// <see cref="JudgeRunAsync"/> is the shared core used by BOTH the queued job
/// (<see cref="ExecuteAsync"/>) AND the manual scoring endpoint
/// (<c>POST /api/runs/{id}/judge</c>); it SKIPS sampling and expects a
/// <see cref="RunRecord"/> the caller has already resolved.
/// </para>
/// <para>
/// On a retry attempt (<see cref="JobRecord.Attempt"/> &gt; 1), a judge that
/// already wrote a score for this run in an earlier attempt is NOT called
/// again; its existing row is read back and returned unchanged. This is a
/// per-judge durable checkpoint built on the <c>run_scores</c> uniqueness
/// constraint described above, not a new table. The manual scoring endpoint
/// never skips: a caller invoking it has explicitly asked for a fresh score.
/// </para>
/// </remarks>
internal sealed class OnlineEvalJobHandler(
    IRunStore runStore,
    IRunInputStore runInputStore,
    IRunScoreStore scoreStore,
    IEnumerable<IRunJudge> judges,
    ITenantContext tenantContext,
    AgentPrismMetrics? metrics = null,
    OnlineEvalSummaryService? summaryService = null,
    TimeProvider? timeProvider = null,
    ILogger<OnlineEvalJobHandler>? logger = null,
    IOptionsMonitor<OnlineEvaluationOptions>? optionsMonitor = null) : IJobHandler
{
    /// <summary>
    /// The prefix written into <see cref="RunScore.Author"/> and
    /// <see cref="RunScore.Source"/> for every judge-authored score. Also the
    /// marker <see cref="ReadAlreadyScoredJudgesAsync"/> uses to recognize a
    /// judge's own row among a run's scores (as opposed to a human's).
    /// </summary>
    private const string JudgeAuthorPrefix = "judge:";

    /// <inheritdoc />
    public JobKind Kind => JobKind.OnlineEval;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.Count == 0 || !Guid.TryParse(context.Items[0].Input, out var runId))
        {
            throw new AgentPrismException("The online evaluation job does not carry a valid run id.");
        }

        var run = await runStore.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // The run was not found (e.g. a retention sweep may have deleted it) or
        // belongs to another tenant: not an error, completes silently.
        if (run is null || !string.Equals(run.TenantId, context.Job.TenantId, StringComparison.Ordinal))
        {
            await CompleteItemAsync(context, JobItemStatus.Completed, error: null, cancellationToken).ConfigureAwait(false);
            return;
        }

        var (_, failures) = await JudgeRunAsync(run, skipAlreadyScored: context.Job.Attempt > 1, cancellationToken).ConfigureAwait(false);

        var retryableFailures = failures.Where(static failure => failure.IsRetryable).ToArray();

        if (retryableFailures.Length > 0)
        {
            // The next attempt evaluates the complete judge list. Score upserts
            // are idempotent, but implementations must make their own effects safe.
            var delay = BackoffFor(context.Job.Attempt);

            throw new JobRetryException(
                $"{retryableFailures.Length} judge(s) failed for run '{runId}': {FormatFailures(retryableFailures)}")
            {
                RetryAfter = delay,
            };
        }

        if (failures.Count > 0)
        {
            await CompleteItemAsync(context, JobItemStatus.Failed, FormatFailures(failures), cancellationToken).ConfigureAwait(false);
            return;
        }

        await CompleteItemAsync(context, JobItemStatus.Completed, error: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Scores the given run with every registered <see cref="IRunJudge"/>.
    /// </summary>
    /// <param name="run">The run to score. The caller must have already performed the tenant/entity check.</param>
    /// <param name="skipAlreadyScored">
    /// When <see langword="true"/>, a judge that already has a
    /// <c>run_scores</c> row for this run (written by an earlier attempt) is
    /// not called again; its existing row is read back and returned instead.
    /// Pass <see langword="true"/> only for a retry attempt
    /// (<see cref="JobRecord.Attempt"/> &gt; 1); the manual scoring endpoint
    /// leaves this <see langword="false"/> so a caller-requested re-judgment
    /// always runs. If reading existing scores fails, the failure is logged
    /// and every judge runs, matching the rule that observability must not
    /// break functionality.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The scores written, and the name/message of any judges that failed.
    /// Both are empty (not an error) when there is no <c>run_inputs</c> record,
    /// no registered judge, or the output cannot be read.
    /// </returns>
    public async ValueTask<(IReadOnlyList<RunScore> Scores, IReadOnlyList<JudgeFailure> Failures)> JudgeRunAsync(
        RunRecord run,
        bool skipAlreadyScored = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var judgeList = judges as IReadOnlyCollection<IRunJudge> ?? [.. judges];

        if (judgeList.Count == 0)
        {
            return ([], []);
        }

        var input = await runInputStore.GetAsync(tenantContext.TenantId, run.Id, cancellationToken).ConfigureAwait(false);

        // When there is no run_inputs record, the judge would run blind; a
        // silent half-scoring is not performed (Open Question 1).
        if (input is null)
        {
            return ([], []);
        }

        var events = await ReadEventsAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var output = ExtractOutputText(events);

        if (output is null)
        {
            return ([], []);
        }

        var invocations = await runStore.ListToolInvocationsAsync(run.Id, cancellationToken).ConfigureAwait(false);

        var judgeContext = new RunJudgeContext
        {
            RunId = run.Id,
            TenantId = tenantContext.TenantId,
            AgentName = run.AgentName,
            Input = input.Messages,
            Output = output,
            ToolNames = [.. invocations.Select(static invocation => invocation.ToolName).Distinct(StringComparer.Ordinal)],
        };

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var scores = new List<RunScore>();
        var failures = new List<JudgeFailure>();
        var alreadyScored = skipAlreadyScored
            ? await ReadAlreadyScoredJudgesAsync(run, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, RunScore>(StringComparer.Ordinal);

        foreach (var judge in judgeList)
        {
            if (alreadyScored.TryGetValue(judge.Name, out var existing))
            {
                scores.Add(existing);
                logger?.LogDebug("Judge '{Judge}' already scored run {RunId} in an earlier attempt; skipping.", judge.Name, run.Id);
                continue;
            }

            await JudgeOneAsync(judge, judgeContext, run, now, scores, failures, cancellationToken).ConfigureAwait(false);
        }

        return (scores, failures);
    }

    /// <summary>
    /// Reads a run's existing <see cref="RunScore"/> rows and returns the
    /// judges that already scored it, keyed by <see cref="IRunJudge.Name"/>.
    /// </summary>
    /// <remarks>
    /// If the read fails, the failure is logged and an empty map is returned
    /// so every judge runs — a checkpoint read must not make the job brittle.
    /// A caller-requested cancellation is NOT swallowed here.
    /// </remarks>
    private async ValueTask<Dictionary<string, RunScore>> ReadAlreadyScoredJudgesAsync(RunRecord run, CancellationToken cancellationToken)
    {
        try
        {
            var existingScores = await scoreStore.ListAsync(tenantContext.TenantId, run.Id, cancellationToken).ConfigureAwait(false);
            var result = new Dictionary<string, RunScore>(StringComparer.Ordinal);

            foreach (var score in existingScores)
            {
                if (!string.IsNullOrEmpty(score.Author) && score.Author.StartsWith(JudgeAuthorPrefix, StringComparison.Ordinal))
                {
                    result[score.Author[JudgeAuthorPrefix.Length..]] = score;
                }
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Failed to read existing scores for run {RunId}; no judge will be skipped.", run.Id);
            return new Dictionary<string, RunScore>(StringComparer.Ordinal);
        }
    }

    private async ValueTask JudgeOneAsync(
        IRunJudge judge,
        RunJudgeContext judgeContext,
        RunRecord run,
        DateTimeOffset now,
        List<RunScore> scores,
        List<JudgeFailure> failures,
        CancellationToken cancellationToken)
    {
        RunJudgment judgment;
        Task<RunJudgment> invocation;
        var timeout = optionsMonitor?.CurrentValue.JudgeTimeout ?? TimeSpan.FromSeconds(60);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeout);

        try
        {
            using var samplingSuppression = AmbientSamplingSuppressionScope.Begin();
            invocation = judge.JudgeAsync(judgeContext, budget.Token).AsTask();

            var timeoutTask = Task.Delay(timeout, timeProvider ?? TimeProvider.System, CancellationToken.None);
            var callerCancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var winner = await Task.WhenAny(invocation, timeoutTask, callerCancellation).ConfigureAwait(false);

            if (winner != invocation)
            {
                ObserveLateJudge(invocation, judge, judgeContext);
                cancellationToken.ThrowIfCancellationRequested();

                failures.Add(Failure(judge.Name, JudgeFailureTypes.Timeout, retryable: true));
                return;
            }

            judgment = await invocation.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            var timedOut = budget.IsCancellationRequested;
            failures.Add(Failure(judge.Name, timedOut
                ? JudgeFailureTypes.Timeout
                : JudgeFailureTypes.Failed, retryable: true));
            LogFailure(judge, judgeContext, exception);
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failures.Add(Failure(judge.Name, JudgeFailureTypes.Failed, retryable: true));
            LogFailure(judge, judgeContext, exception);
            return;
        }

        // 🚨 No decision reached: a silent 0 is NOT written. A zero is a
        // measurement, not the absence of one.
        if (judgment.Score is not { } score)
        {
            return;
        }

        if (score is < 0 or > 100)
        {
            failures.Add(Failure(judge.Name, JudgeFailureTypes.Contract, retryable: false));
            logger?.LogWarning("Judge '{Judge}' returned an out-of-range score for run {RunId}.", judge.Name, judgeContext.RunId);
            return;
        }

        var reason = NormalizeReason(judgment.Reason);

        var saved = await scoreStore.UpsertAsync(
            new RunScore
            {
                TenantId = tenantContext.TenantId,
                RunId = judgeContext.RunId,
                Kind = RunScoreKind.Numeric,
                Value = score,
                Comment = reason,
                Source = $"{JudgeAuthorPrefix}{judge.Name}",
                Author = $"{JudgeAuthorPrefix}{judge.Name}",
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        scores.Add(saved);

        metrics?.RecordJudgeScore(judge.Name, run.AgentName, tenantContext.TenantId, score);

        if (summaryService is not null)
        {
            await summaryService.RecordScoreAsync(tenantContext.TenantId, score, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask CompleteItemAsync(
        JobContext context,
        JobItemStatus status,
        string? error,
        CancellationToken cancellationToken)
        => await context.ReportItemAsync(
            new JobItemResult
            {
                JobId = context.Job.Id,
                Seq = context.Items[0].Seq,
                Status = status,
                Error = error,
            },
            cancellationToken).ConfigureAwait(false);

    private static JudgeFailure Failure(string judgeName, string errorType, bool retryable)
        => new() { JudgeName = judgeName, ErrorType = errorType, IsRetryable = retryable };

    private static string FormatFailures(IEnumerable<JudgeFailure> failures)
        => string.Join("; ", failures.Select(static failure => $"{failure.JudgeName} ({failure.ErrorType})"));

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return null;
        }

        const string truncation = "… [truncated]";
        return reason.Length <= RunJudgment.MaxReasonLength
            ? reason
            : string.Concat(reason.AsSpan(0, RunJudgment.MaxReasonLength - truncation.Length), truncation);
    }

    private void LogFailure(IRunJudge judge, RunJudgeContext context, Exception exception)
    {
        if (logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(exception, "Judge '{Judge}' failed for run {RunId}.", judge.Name, context.RunId);
        }
    }

    private void ObserveLateJudge(Task<RunJudgment> invocation, IRunJudge judge, RunJudgeContext context)
    {
        _ = invocation.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    logger?.LogWarning(
                        task.Exception,
                        "Judge '{Judge}' faulted after its timeout was reported for run {RunId}.",
                        judge.Name,
                        context.RunId);
                }
                else
                {
                    logger?.LogDebug(
                        "Judge '{Judge}' finished after its timeout was reported for run {RunId}.",
                        judge.Name,
                        context.RunId);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static TimeSpan BackoffFor(int attempt)
    {
        var seconds = Math.Min(30 * Math.Pow(2, Math.Max(attempt - 1, 0)), 600);
        return TimeSpan.FromSeconds(seconds);
    }

    private static class JudgeFailureTypes
    {
        internal const string Failed = "judge_failed";
        internal const string Timeout = "judge_timeout";
        internal const string Contract = "judge_contract";
    }

    private async ValueTask<List<RunEvent>> ReadEventsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runStore.ReadEventsAsync(runId, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>
    /// Extracts the model output from the event stream.
    /// </summary>
    /// <remarks>
    /// The SAME pattern as <c>ExtractOutputText</c> in <see cref="RunToCasePromoter"/>:
    /// if <see cref="RunEventType.MessageCompleted"/> is present (non-streaming
    /// run), it is used; otherwise (streaming run) the
    /// <see cref="RunEventType.MessageDelta"/> chunks are concatenated.
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
