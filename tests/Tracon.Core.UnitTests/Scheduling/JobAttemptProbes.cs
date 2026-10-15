using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The shared setup of the worker's cancel and time-limit tests (phase 196, F-323): a
/// job store that can fail or forget its reads, a handler that records what its token
/// did, and a worker that polls fast.
/// </summary>
internal static class JobAttemptProbes
{
    public const string Tenant = "tenant-a";

    /// <summary>The worker's poll interval in these tests: also the cancel watch's.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

    /// <summary>Builds a worker over <paramref name="store"/> running <paramref name="handler"/> for <see cref="JobHandlerKeys.AgentBatch"/>.</summary>
    /// <remarks>
    /// <paramref name="clock"/> drives the attempt's own timers (the cancel watch, the
    /// deadline, the lease renewal); the worker's poll for new work keeps the real clock.
    /// </remarks>
    public static (JobWorkerBackgroundService Worker, TestJobHandlerHost Host) Worker(
        IJobStore store,
        IJobHandler handler,
        Action<TraconSchedulingOptions>? configure = null,
        Microsoft.Extensions.Logging.ILogger<JobWorkerBackgroundService>? logger = null,
        TimeProvider? clock = null)
    {
        var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        var options = new TraconSchedulingOptions
        {
            MaxConcurrentJobs = 4,
            PollInterval = Poll,
        };

        configure?.Invoke(options);

        var worker = new JobWorkerBackgroundService(
            store,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(options),
            new SchemaReadyGate([]),
            new NotDraining(),
            timeProvider: clock,
            logger: logger ?? NullLogger<JobWorkerBackgroundService>.Instance);

        return (worker, host);
    }

    /// <summary>Queues one job for <see cref="JobHandlerKeys.AgentBatch"/>.</summary>
    public static async Task<Guid> EnqueueAsync(IJobStore store, string target, int? maxAttempts = 1, TimeSpan? maxDuration = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await store.EnqueueAsync(
            new JobRecord
            {
                Id = id,
                TenantId = Tenant,
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = target,
                Status = JobStatus.Pending,
                MaxAttempts = maxAttempts,
                MaxDuration = maxDuration,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        return id;
    }

    /// <summary>Waits for a job to reach one of <paramref name="statuses"/>.</summary>
    public static async Task<JobRecord> WaitForAsync(IJobStore store, Guid id, params JobStatus[] statuses)
        => (await WaitUntil.ValueAsync(
            () => store.GetAsync(Tenant, id, TestContext.Current.CancellationToken).AsTask(),
            job => job is not null && statuses.Contains(job.Status),
            $"job {id} to reach {string.Join(" or ", statuses)}"))!;

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }
}

/// <summary>What a probed handler does with its token, per job target.</summary>
internal enum ProbeBehavior
{
    /// <summary>Waits until its token is cancelled, then lets the cancellation surface.</summary>
    StopOnToken,

    /// <summary>Waits until its token is cancelled, then returns normally.</summary>
    ReturnOnToken,

    /// <summary>Ignores its token and waits for the test to release it.</summary>
    IgnoreToken,

    /// <summary>Returns at once.</summary>
    Return,
}

/// <summary>A handler that records, per job target, when it started and what its token did.</summary>
internal sealed class TokenProbeHandler : IJobHandler
{
    private readonly ConcurrentDictionary<string, Probe> _probes = new(StringComparer.Ordinal);

    /// <summary>Gets the probe of a job target, created on first use.</summary>
    public Probe For(string target) => _probes.GetOrAdd(target, static _ => new Probe());

    /// <summary>Gets or sets what the handler does for a target with no behavior of its own.</summary>
    public ProbeBehavior DefaultBehavior { get; set; } = ProbeBehavior.StopOnToken;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var probe = For(context.Job.TargetName);

        Interlocked.Increment(ref probe.CallCount);
        probe.Signals = context.Signals;
        probe.Token = cancellationToken;

        using var registration = cancellationToken.Register(static state => ((Probe)state!).Cancelled.TrySetResult(), probe);

        probe.Started.TrySetResult();

        switch (probe.Behavior ?? DefaultBehavior)
        {
            case ProbeBehavior.StopOnToken:
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
                break;

            case ProbeBehavior.ReturnOnToken:
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
                }
                catch (OperationCanceledException)
                {
                    // Returns normally: the work it had done stands.
                }

                break;

            case ProbeBehavior.IgnoreToken:
                await probe.Release.Task.ConfigureAwait(false);
                break;
        }
    }

    /// <summary>What one job target's calls did.</summary>
    internal sealed class Probe
    {
        public int CallCount;

        public ProbeBehavior? Behavior { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Token { get; set; }

        public JobAttemptSignals? Signals { get; set; }
    }
}

/// <summary>
/// An <see cref="InMemoryJobStore"/> whose single-job read can be made to fail or to find
/// nothing, and which counts those reads.
/// </summary>
internal sealed class ProbeJobStore(InMemoryJobStore inner) : IJobStore
{
    private int _failures;
    private int _reads;

    /// <summary>Gets how many times <see cref="GetAsync"/> was called.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>Gets or sets a job the store pretends not to know.</summary>
    public Guid? Gone { get; set; }

    /// <summary>Makes the next <paramref name="count"/> reads throw.</summary>
    public void FailNextReads(int count) => Volatile.Write(ref _failures, count);

    /// <summary>
    /// Gets or sets a step that runs after a job is released for another attempt and
    /// before the release returns to the worker, so a test can hold the worker between
    /// the store write and the end of the attempt.
    /// </summary>
    public Func<Guid, Task>? AfterReleaseForRetry { get; set; }

    /// <summary>Gets or sets a step that sees every job the store leases.</summary>
    public Action<JobRecord>? Leased { get; set; }

    /// <inheritdoc />
    public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);

        if (Interlocked.Decrement(ref _failures) >= 0)
        {
            throw new InvalidOperationException("The job store is unavailable (probe).");
        }

        return Gone == jobId ? ValueTask.FromResult<JobRecord?>(null) : inner.GetAsync(tenantId, jobId, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
        => inner.EnqueueAsync(job, items, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<JobRecord?> LeaseAsync(string owner, TimeSpan leaseDuration, IReadOnlyList<string>? lanes, CancellationToken cancellationToken = default)
    {
        var job = await inner.LeaseAsync(owner, leaseDuration, lanes, cancellationToken).ConfigureAwait(false);

        if (job is not null)
        {
            Leased?.Invoke(job);
        }

        return job;
    }

    /// <inheritdoc />
    public ValueTask RenewLeaseAsync(Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        => inner.RenewLeaseAsync(jobId, owner, leaseDuration, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
        => inner.MarkRunningAsync(jobId, owner, cancellationToken);

    /// <inheritdoc />
    public ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
        => inner.CompleteAsync(completion, cancellationToken);

    /// <inheritdoc />
    public async ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
    {
        await inner.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken).ConfigureAwait(false);

        if (AfterReleaseForRetry is { } step)
        {
            await step(jobId).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
        => inner.CancelAsync(tenantId, jobId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
        => inner.QueryAsync(query, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
        => inner.ListItemsAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
        => inner.ReportItemAsync(item, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
        => inner.GetQueueDepthAsync(cancellationToken);
}
