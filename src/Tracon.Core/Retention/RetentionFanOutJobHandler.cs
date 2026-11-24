using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Queues one retention job for each tenant <see cref="IRetentionTenantSource"/> names
/// (<see cref="JobHandlerKeys.RetentionFanOut"/>).
/// </summary>
/// <remarks>
/// <para>
/// A retention job cleans the tenant it was queued for, and a schedule belongs to one tenant. This handler is the one
/// place that crosses that line: it runs in the installation's default tenant and queues the work for the others. It
/// deletes nothing itself, and each job it queues reads its own tenant's policy, so a tenant whose policy is off or
/// longer than the default is honoured.
/// </para>
/// <para>
/// It reaches every tenant's retention, so it refuses a job of any other tenant, whoever queued it (a schedule, the
/// HTTP surface or code). The refusal is final: running it again cannot change the tenant.
/// </para>
/// <para>
/// A tenant whose job is still waiting or running is left alone, so a sweep that is slower than the schedule does not
/// pile up behind itself. One tenant whose job cannot be queued does not stop the others; the failures are raised
/// once the last tenant is done, and the job is tried again.
/// </para>
/// </remarks>
internal sealed class RetentionFanOutJobHandler(
    IRetentionTenantSource tenantSource,
    IJobDispatcher dispatcher,
    IJobStore jobStore,
    IOptionsMonitor<TraconOptions> coreOptions,
    ILogger<RetentionFanOutJobHandler>? logger = null) : IJobHandler
{
    // The newest jobs of a tenant under the retention key; an open job of the same target is always among them
    // unless twenty newer ones exist, which a schedule cannot produce.
    private const int OpenJobLookback = 20;

    // The target name that stands for every target (not every tenant).
    private const string AllTargets = "*";

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        RefuseOutsideTheDefaultTenant(context.Job);

        var target = context.Job.TargetName is "*" or "" ? AllTargets : context.Job.TargetName;

        if (!string.Equals(target, AllTargets, StringComparison.Ordinal) && !RetentionTargets.IsKnown(target))
        {
            throw new ArgumentException($"Unknown retention target: '{target}'.", nameof(context));
        }

        var tally = new FanOutTally();

        try
        {
            await FanOutAsync(target, tally, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            tally.Failures.Add(exception);
        }

        Log(context.Job, target, tally);

        if (tally.Failures.Count > 0)
        {
            await ReportAsync(context, JobItemStatus.Failed, "The retention fan-out could not queue every tenant.", cancellationToken).ConfigureAwait(false);

            if (tally.Failures.Count == 1)
            {
                // The failure as it was: its type is what the job records.
                ExceptionDispatchInfo.Capture(tally.Failures[0]).Throw();
            }

            throw new AggregateException(
                $"The retention fan-out could not queue {tally.Failures.Count} tenants.",
                tally.Failures);
        }

        await ReportAsync(context, JobItemStatus.Completed, error: null, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask FanOutAsync(string target, FanOutTally tally, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var candidate in tenantSource.ListTenantsAsync(cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            tally.Listed++;

            if (!AmbientTenantScope.TryNormalize(candidate, out var tenantId))
            {
                tally.Invalid++;

                continue;
            }

            if (!seen.Add(tenantId))
            {
                tally.Repeated++;

                continue;
            }

            try
            {
                if (await HasOpenJobAsync(tenantId, target, cancellationToken).ConfigureAwait(false))
                {
                    tally.AlreadyQueued++;

                    continue;
                }

                await dispatcher.EnqueueAsync(
                    new JobRequest
                    {
                        TenantId = tenantId,
                        HandlerKey = JobHandlerKeys.Retention,
                        TargetName = target,
                        Items = [target],
                    },
                    cancellationToken).ConfigureAwait(false);

                tally.Queued++;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                tally.Failures.Add(exception);

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Could not queue a retention job for tenant '{Tenant}'; the other tenants are still queued.", tenantId);
                }
            }
        }
    }

    private async ValueTask<bool> HasOpenJobAsync(string tenantId, string target, CancellationToken cancellationToken)
    {
        var recent = await jobStore.QueryAsync(
            new JobQuery
            {
                TenantId = tenantId,
                HandlerKey = JobHandlerKeys.Retention,
                Take = OpenJobLookback,
            },
            cancellationToken).ConfigureAwait(false);

        return recent.Any(job =>
            job.Status is JobStatus.Pending or JobStatus.Leased or JobStatus.Running
            && string.Equals(job.TargetName, target, StringComparison.Ordinal));
    }

    private void RefuseOutsideTheDefaultTenant(JobRecord job)
    {
        if (HostOnlyJobHandlerKeys.IsDefaultTenant(job.TenantId, coreOptions.CurrentValue.DefaultTenantId))
        {
            return;
        }

        var correlationId = SafeErrorText.NewCorrelationId();

        if (logger is not null && logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(
                "Job {JobId} of tenant '{Tenant}' asked for the retention fan-out, which runs only in the default tenant, and was refused. (ref: {CorrelationId})",
                job.Id,
                job.TenantId,
                correlationId);
        }

        throw new NonRetryableJobException(
            TraconErrorTypes.JobHostOnlyHandlerRefused,
            correlationId,
            new InvalidOperationException("The retention fan-out runs only in the installation's default tenant."));
    }

    private void Log(JobRecord job, string target, FanOutTally tally)
    {
        if (logger is null || !logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.LogInformation(
            "Retention fan-out {JobId} for target '{Target}': {Listed} tenants listed, {Queued} jobs queued, {AlreadyQueued} already waiting or running, {Invalid} invalid identifiers skipped, {Repeated} repeated identifiers skipped, {Failed} failed.",
            job.Id,
            target,
            tally.Listed,
            tally.Queued,
            tally.AlreadyQueued,
            tally.Invalid,
            tally.Repeated,
            tally.Failures.Count);
    }

    private static async ValueTask ReportAsync(
        JobContext context,
        JobItemStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        if (context.Items.Count == 0)
        {
            return;
        }

        await context.ReportItemAsync(
            new JobItemResult
            {
                JobId = context.Job.Id,
                Seq = context.Items[0].Seq,
                Status = status,
                Error = error,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class FanOutTally
    {
        public int Listed { get; set; }

        public int Queued { get; set; }

        public int AlreadyQueued { get; set; }

        public int Invalid { get; set; }

        public int Repeated { get; set; }

        public List<Exception> Failures { get; } = [];
    }
}
