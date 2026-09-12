using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Provides shared logic for preview and actual cleanup runs. <see cref="RetentionJobHandler"/>
/// and the <c>RetentionEndpoints</c> "run now" and "preview" endpoints use it.
/// </summary>
public sealed class RetentionExecutor(
    IRetentionPolicyStore policyStore,
    IRetentionStore dataStore,
    RetentionPolicyResolver resolver,
    IOptionsMonitor<TraconRetentionOptions> optionsMonitor,
    IArchiveSink? archiveSink = null,
    TimeProvider? timeProvider = null,
    ILogger<RetentionExecutor>? logger = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <summary>Returns a "what would happen now" preview for one or all targets.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="target">The sole target, or <see langword="null"/> for every known target.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A preview for each target.</returns>
    public async ValueTask<IReadOnlyList<RetentionPreview>> PreviewAsync(
        string tenantId,
        string? target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var targets = ResolveTargetList(target);
        var results = new List<RetentionPreview>(targets.Count);
        var now = _clock.GetUtcNow();

        foreach (var candidate in targets)
        {
            var resolved = await resolver.ResolveAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);

            if (resolved is null)
            {
                results.Add(new RetentionPreview { Target = candidate, Enabled = false });

                continue;
            }

            var cutoff = await ComputeCutoffAsync(tenantId, resolved, now, cancellationToken).ConfigureAwait(false);
            var count = cutoff is null
                ? 0
                : await dataStore.CountOlderThanAsync(candidate, tenantId, cutoff.Value, cancellationToken).ConfigureAwait(false);

            results.Add(new RetentionPreview
            {
                Target = candidate,
                MaxAgeDays = resolved.MaxAgeDays,
                Enabled = true,
                Cutoff = cutoff,
                MatchingRows = count,
            });
        }

        return results;
    }

    /// <summary>Runs actual cleanup for one or all targets.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="target">The sole target, or <see langword="null"/> for every known target.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completed, or no-op, run records.</returns>
    public async ValueTask<IReadOnlyList<RetentionRun>> RunAsync(
        string tenantId,
        string? target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var options = optionsMonitor.CurrentValue;
        var targets = ResolveTargetList(target);
        var completed = new List<RetentionRun>(targets.Count);

        foreach (var candidate in targets)
        {
            var resolved = await resolver.ResolveAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);

            if (resolved is null)
            {
                continue;
            }

            var run = await RunTargetAsync(tenantId, resolved, options, cancellationToken).ConfigureAwait(false);
            completed.Add(run);
        }

        return completed;
    }

    private async ValueTask<RetentionRun> RunTargetAsync(
        string tenantId,
        ResolvedRetentionPolicy policy,
        TraconRetentionOptions options,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        var run = await policyStore.CreateRunAsync(
            new RetentionRun
            {
                Id = TraconId.NewId(),
                TenantId = tenantId,
                Target = policy.Target,
                StartedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        var cutoff = await ComputeCutoffAsync(tenantId, policy, now, cancellationToken).ConfigureAwait(false);

        if (cutoff is null)
        {
            // 🚨 No cutoff was computed: MaxAgeDays is absent and, if MaxRows is
            // set, the table is already within the limit. No delete query runs.
            await policyStore.CompleteRunAsync(run.Id, _clock.GetUtcNow(), null, cancellationToken).ConfigureAwait(false);

            return run;
        }

        if (policy.Archive && archiveSink is null)
        {
            // 🚨 Data that cannot be archived is not discarded (phase 25 decision).
            // The run completes successfully, but no row is deleted.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "Archiving was requested for target '{Target}', but no IArchiveSink is registered; no rows were deleted.",
                    policy.Target);
            }

            await policyStore.CompleteRunAsync(run.Id, _clock.GetUtcNow(), null, cancellationToken).ConfigureAwait(false);

            return run;
        }

        var cutoffValue = cutoff.Value;
        string? error = null;
        long deletedTotal = 0;
        long archivedTotal = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (policy.Archive)
                {
                    var rows = await dataStore
                        .ReadForArchiveAsync(policy.Target, tenantId, cutoffValue, options.BatchSize, cancellationToken)
                        .ConfigureAwait(false);

                    if (rows.Count > 0)
                    {
                        await archiveSink!
                            .WriteAsync(policy.Target, now, rows, cancellationToken)
                            .ConfigureAwait(false);

                        archivedTotal += rows.Count;

                        await policyStore
                            .AppendRunProgressAsync(run.Id, 0, rows.Count, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                var deleted = await dataStore
                    .DeleteBatchAsync(policy.Target, tenantId, cutoffValue, options.BatchSize, cancellationToken)
                    .ConfigureAwait(false);

                if (deleted == 0)
                {
                    break;
                }

                deletedTotal += deleted;

                await policyStore
                    .AppendRunProgressAsync(run.Id, deleted, 0, cancellationToken)
                    .ConfigureAwait(false);

                if (deleted < options.BatchSize)
                {
                    // The final batch returned fewer rows: none remain to match,
                    // so there is no reason to try again.
                    break;
                }

                if (options.BatchDelay > TimeSpan.Zero)
                {
                    await Task.Delay(options.BatchDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var correlationId = SafeErrorText.NewCorrelationId();

            if (logger is not null && logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(exception, "Retention run {RunId} failed. (ref: {CorrelationId})", run.Id, correlationId);
            }

            error = SafeErrorText.ForPersistence(exception, correlationId);

            throw;
        }
        finally
        {
            await policyStore
                .CompleteRunAsync(run.Id, _clock.GetUtcNow(), error, cancellationToken)
                .ConfigureAwait(false);
        }

        return run with { DeletedRows = deletedTotal, ArchivedRows = archivedTotal, CompletedAt = _clock.GetUtcNow() };
    }

    /// <summary>
    /// Resolves age and volume cutoffs together. When both are present, the
    /// newer cutoff, which deletes more, wins. This is the only option that
    /// guarantees both rules are met (36.2).
    /// </summary>
    private async ValueTask<DateTimeOffset?> ComputeCutoffAsync(
        string tenantId,
        ResolvedRetentionPolicy policy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ageCutoff = policy.MaxAgeDays is { } days ? now - TimeSpan.FromDays(days) : (DateTimeOffset?)null;

        var rowCutoff = policy.MaxRows is { } maxRows
            ? await dataStore.FindRowLimitCutoffAsync(policy.Target, tenantId, maxRows, cancellationToken).ConfigureAwait(false)
            : null;

        if (ageCutoff is null)
        {
            return rowCutoff;
        }

        if (rowCutoff is null)
        {
            return ageCutoff;
        }

        return ageCutoff.Value > rowCutoff.Value ? ageCutoff : rowCutoff;
    }

    private static IReadOnlyList<string> ResolveTargetList(string? target)
    {
        if (target is not { Length: > 0 })
        {
            return RetentionTargets.All;
        }

        if (!RetentionTargets.IsKnown(target))
        {
            throw new ArgumentException($"Unknown retention target: '{target}'.", nameof(target));
        }

        return [target];
    }
}
