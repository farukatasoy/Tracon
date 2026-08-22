using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Background service that periodically closes <c>Running</c> runs that have
/// not sent a heartbeat for a long time, marking them <c>Failed</c>.
/// </summary>
/// <remarks>
/// <para>
/// If a process crashes in the middle of <c>agent.RunAsync</c> (on the default
/// <c>MaxAttempts = 1</c> path), the row stays <c>Running</c> forever and
/// silently dilutes the denominator of <c>RunStatistics.ErrorRate</c> (
/// <c>settled = CompletedRuns + FailedRuns + CanceledRuns</c>). This service
/// finds that row, closes it, and records the reason.
/// </para>
/// <para>
/// While <see cref="RunReconciliationOptions.Enabled"/> is <see langword="false"/>
/// (the default), no SQL query is issued at all. Even when enabled, it
/// runs only ONE scan across the cluster -- <see cref="SingletonGuard"/> is
/// the SAME pattern <c>McpDiscoveryService</c> uses.
/// </para>
/// <para>
/// Right after a scan closes orphaned <c>Running</c> rows, this is also
/// where automatic continuation (<see cref="AgentPrismRunContinuationOptions"/>)
/// is triggered — see <see cref="TryContinueAsync"/>. While that option is
/// off (the default), a claimed row is closed exactly as it is today;
/// nothing further happens.
/// </para>
/// </remarks>
internal sealed class RunReconciliationService(
    IRunStore runStore,
    ISingletonLeaseStore leaseStore,
    IJobStore jobStore,
    IToolRegistry toolRegistry,
    IOptionsMonitor<RunReconciliationOptions> optionsMonitor,
    IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor,
    IOptionsMonitor<AgentPrismRunContinuationOptions> continuationOptionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<RunReconciliationService>? logger = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!optionsMonitor.CurrentValue.Enabled)
        {
            return;
        }

        // 🚨 Wait for the schema to be ready BEFORE the first SQL attempt (K-354).
        try
        {
            await schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Single-executor selection (Phase 42): when disabled (the default),
        // guard.IsHeld is always true and RunAsync returns immediately without
        // issuing any query to the store.
        var guard = new SingletonGuard(leaseStore, singletonOptionsMonitor, "run-reconciliation", logger);
        var guardTask = guard.RunAsync(stoppingToken);

        using var timer = new PeriodicTimer(optionsMonitor.CurrentValue.ScanInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (guard.IsHeld)
                {
                    await TickAsync(stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            await guardTask.ConfigureAwait(false);
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var staleBefore = _clock.GetUtcNow() - options.OrphanThreshold;

        try
        {
            var claimed = await runStore
                .ClaimOrphanedRunsAsync(staleBefore, options.MaxRunsPerScan, stoppingToken)
                .ConfigureAwait(false);

            if (claimed.Count > 0 && logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "{Count} orphaned run(s) closed (threshold: {StaleBefore:O}).",
                    claimed.Count,
                    staleBefore);
            }

            foreach (var record in claimed)
            {
                await TryContinueAsync(record, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A reconciliation pass must never crash the process: a failure
            // is retried on the next tick.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Orphaned run reconciliation pass failed.");
            }
        }
    }

    /// <summary>
    /// Decides whether a just-closed orphaned run is continued, and either
    /// enqueues it or records why not.
    /// </summary>
    /// <remarks>
    /// Checked in order: enabled AND session-bound AND within
    /// <see cref="AgentPrismRunContinuationOptions.MaxAttempts"/>, then
    /// whether every tool the run called may safely run again. Any step
    /// failing closed just RETURNS — the row is already correctly
    /// <see cref="RunStatus.Failed"/> from the claim.
    /// </remarks>
    private async Task TryContinueAsync(RunRecord record, CancellationToken cancellationToken)
    {
        var continuation = continuationOptionsMonitor.CurrentValue;

        if (!continuation.Enabled ||
            record.SessionId is not { Length: > 0 } ||
            record.TenantId is not { Length: > 0 } tenantId)
        {
            return;
        }

        if (!await WithinContinuationLimitAsync(record, continuation.MaxAttempts, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // 🚨 ListToolInvocationsAsync reads its tenant IMPLICITLY from the
        // ambient ITenantContext (unlike QueryRunsAsync); this background
        // loop carries no ambient tenant on its own, so it is opened here for
        // the source run's OWN tenant. Without this, the call silently
        // returns empty and every run would look tool-free (K memory note).
        string? unsafeTool;

        using (AmbientTenantScope.Begin(tenantId))
        {
            unsafeTool = await FindUnsafeToolAsync(record.Id, cancellationToken).ConfigureAwait(false);
        }

        if (unsafeTool is not null)
        {
            await WriteBlockedEventAsync(record, unsafeTool, cancellationToken).ConfigureAwait(false);
            return;
        }

        await EnqueueContinuationAsync(record, tenantId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Walks <see cref="RunRecord.ContinuedFromRunId"/> backward, bounded by
    /// <paramref name="maxAttempts"/>.
    /// </summary>
    /// <remarks>
    /// Stops as soon as the count reaches the limit — for the common
    /// <c>MaxAttempts = 1</c> case, a run that is already itself a
    /// continuation (<see cref="RunRecord.ContinuedFromRunId"/> set) is
    /// rejected WITHOUT fetching anything.
    /// </remarks>
    private async ValueTask<bool> WithinContinuationLimitAsync(RunRecord record, int maxAttempts, CancellationToken cancellationToken)
    {
        var priorContinuations = 0;
        var cursor = record.ContinuedFromRunId;

        while (cursor is { } ancestorId)
        {
            priorContinuations++;

            if (priorContinuations >= maxAttempts)
            {
                return false;
            }

            var ancestor = await runStore.GetRunAsync(ancestorId, cancellationToken).ConfigureAwait(false);
            cursor = ancestor?.ContinuedFromRunId;
        }

        return true;
    }

    /// <summary>
    /// Finds the first tool call in the run's ledger whose effect cannot be
    /// safely repeated. <see langword="null"/> if the run is safe to continue.
    /// </summary>
    /// <remarks>
    /// A tool no longer in the registry (renamed or removed since the run)
    /// is treated as unsafe: its effect and <c>SafeToRepeat</c> declaration
    /// can no longer be read, so safety cannot be verified.
    /// </remarks>
    private async ValueTask<string?> FindUnsafeToolAsync(Guid runId, CancellationToken cancellationToken)
    {
        var invocations = await runStore.ListToolInvocationsAsync(runId, cancellationToken).ConfigureAwait(false);

        if (invocations.Count == 0)
        {
            return null;
        }

        var descriptors = toolRegistry.List();

        foreach (var invocation in invocations)
        {
            var descriptor = descriptors.FirstOrDefault(
                candidate => string.Equals(candidate.Name, invocation.ToolName, StringComparison.Ordinal));

            if (descriptor is null || (descriptor.Effect is ToolEffect.Destructive or ToolEffect.External && !descriptor.SafeToRepeat))
            {
                return invocation.ToolName;
            }
        }

        return null;
    }

    private async Task WriteBlockedEventAsync(RunRecord record, string unsafeTool, CancellationToken cancellationToken)
    {
        try
        {
            var sequence = -1L;

            await foreach (var runEvent in runStore.ReadEventsAsync(record.Id, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                sequence = runEvent.Sequence;
            }

            await runStore.AppendEventAsync(
                new RunEvent
                {
                    RunId = record.Id,
                    Sequence = sequence + 1,
                    Type = RunEventType.RunContinuationBlocked,
                    Text = $"Automatic continuation was refused: tool '{unsafeTool}' may not be safely repeated " +
                           "(destructive or external effect, not declared SafeToRepeat).",
                    Timestamp = _clock.GetUtcNow(),
                    TenantId = record.TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Could not record why continuation was refused: {RunId}.", record.Id);
            }
        }
    }

    private async Task EnqueueContinuationAsync(RunRecord record, string tenantId, CancellationToken cancellationToken)
    {
        var continuationRunId = AgentPrismId.NewId();
        var now = _clock.GetUtcNow();

        // A placeholder row, same rationale as the HTTP queued-run path
        // (AgentEndpoints.RunQueuedAsync): if enqueuing below fails, there
        // must be a row to close back to Failed.
        await runStore.StartRunAsync(
            new RunStartInfo
            {
                RunId = continuationRunId,
                AgentName = record.AgentName,
                Status = RunStatus.Queued,
                StartedAt = now,
                TenantId = tenantId,
                SessionId = record.SessionId,
                ContinuedFromRunId = record.Id,
            },
            cancellationToken).ConfigureAwait(false);

        try
        {
            await jobStore.EnqueueAsync(
                new JobRecord
                {
                    Id = continuationRunId,
                    TenantId = tenantId,
                    Kind = JobKind.RunContinuation,
                    TargetName = record.AgentName,
                    Status = JobStatus.Pending,
                    Payload = BuildContinuationPayload(record.Id, continuationRunId),
                    MaxAttempts = 1,
                    ScheduledFor = now,
                    CreatedAt = now,
                },
                [],
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Could not enqueue continuation for run {RunId}.", record.Id);
            }

            try
            {
                await runStore.CompleteRunAsync(
                    new RunCompletion
                    {
                        RunId = continuationRunId,
                        Status = RunStatus.Failed,
                        CompletedAt = _clock.GetUtcNow(),
                        Error = new RunError { Type = exception.GetType().Name, Message = exception.Message },
                        TenantId = tenantId,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception completionException) when (completionException is not OperationCanceledException)
            {
                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(
                        completionException,
                        "Could not close the un-enqueued continuation placeholder: {RunId}.",
                        continuationRunId);
                }
            }
        }
    }

    /// <summary>
    /// Builds the payload for a <see cref="JobKind.RunContinuation"/> job.
    /// Parsing happens in <c>RunContinuationJobHandler.ParsePayload</c>
    /// (hand-written, AOT-compatible); this is written by hand for the same reason.
    /// </summary>
    private static System.Text.Json.JsonElement BuildContinuationPayload(Guid sourceRunId, Guid continuationRunId)
    {
        using var buffer = new MemoryStream();

        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("sourceRunId", sourceRunId);
            writer.WriteString("continuationRunId", continuationRunId);
            writer.WriteEndObject();
        }

        using var document = System.Text.Json.JsonDocument.Parse(buffer.ToArray());

        return document.RootElement.Clone();
    }
}
