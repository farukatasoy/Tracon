using Microsoft.Extensions.Logging;

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
    ILogger<OnlineEvalJobHandler>? logger = null) : IJobHandler
{
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
            await CompleteItemAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var (_, failures) = await JudgeRunAsync(run, cancellationToken).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            // Successful judges' scores are already written (inside
            // JudgeRunAsync, the upsert is idempotent); only the job for the
            // FAILED judges is re-queued with backoff so it retries on the
            // next attempt (K-160).
            var delay = BackoffFor(context.Job.Attempt);

            throw new JobRetryException(
                $"{failures.Count} judge(s) failed for run '{runId}': {string.Join("; ", failures)}")
            {
                RetryAfter = delay,
            };
        }

        await CompleteItemAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Scores the given run with every registered <see cref="IRunJudge"/>.
    /// </summary>
    /// <param name="run">The run to score. The caller must have already performed the tenant/entity check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The scores written, and the name/message of any judges that failed.
    /// Both are empty (not an error) when there is no <c>run_inputs</c> record,
    /// no registered judge, or the output cannot be read.
    /// </returns>
    public async ValueTask<(IReadOnlyList<RunScore> Scores, IReadOnlyList<string> Failures)> JudgeRunAsync(
        RunRecord run,
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
        var failures = new List<string>();

        foreach (var judge in judgeList)
        {
            await JudgeOneAsync(judge, judgeContext, run, now, scores, failures, cancellationToken).ConfigureAwait(false);
        }

        return (scores, failures);
    }

    private async ValueTask JudgeOneAsync(
        IRunJudge judge,
        RunJudgeContext judgeContext,
        RunRecord run,
        DateTimeOffset now,
        List<RunScore> scores,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        RunJudgment judgment;

        try
        {
            judgment = await judge.JudgeAsync(judgeContext, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failures.Add($"{judge.Name}: {exception.Message}");

            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Judge '{Judge}' failed for run {RunId}.",
                    judge.Name,
                    judgeContext.RunId);
            }

            return;
        }

        // 🚨 No decision reached: a silent 0 is NOT written. A zero is a
        // measurement, not the absence of one.
        if (judgment.Score is not { } score)
        {
            return;
        }

        var saved = await scoreStore.UpsertAsync(
            new RunScore
            {
                TenantId = tenantContext.TenantId,
                RunId = judgeContext.RunId,
                Kind = RunScoreKind.Numeric,
                Value = score,
                Comment = judgment.Reason,
                Source = $"judge:{judge.Name}",
                Author = $"judge:{judge.Name}",
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

    private static async ValueTask CompleteItemAsync(JobContext context, CancellationToken cancellationToken)
        => await context.ReportItemAsync(
            new JobItemResult
            {
                JobId = context.Job.Id,
                Seq = context.Items[0].Seq,
                Status = JobItemStatus.Completed,
            },
            cancellationToken).ConfigureAwait(false);

    private static TimeSpan BackoffFor(int attempt)
    {
        var seconds = Math.Min(30 * Math.Pow(2, Math.Max(attempt - 1, 0)), 600);
        return TimeSpan.FromSeconds(seconds);
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
