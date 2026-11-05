using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// Behaviour 7: a node that stalls past its job lease cannot close or requeue the attempt of the node that took the
/// job over.
/// </summary>
public abstract partial class TwoNodeAcceptanceContract
{
    private const string LeaseLossHandlerKey = "test.lease-loss";

    /// <summary>
    /// Node Z leases the job and stalls (a long pause, a network cut): none of its calls to the database get through, its
    /// renewals included. Z cannot prove its lease any more and gives the job up on its own, while it is still frozen:
    /// its handler is told. Z's lease runs out, node N leases the job and is still working when Z wakes up. Z must write
    /// nothing. Before the lease was fenced, Z's late completion closed the job while N was still running it; before a
    /// renewal that never answered cost the lease, Z's handler worked on to the end of an attempt it no longer held.
    /// </summary>
    /// <param name="renewalsThawFirst">
    /// <see langword="true"/>: Z's frozen renewal is let go before its writes. <see langword="false"/>: the writes first.
    /// Either way Z's held renewal was cancelled when Z gave the lease up, so it never reaches the database, and Z's worker
    /// has nothing to write for an attempt it lost.
    /// </param>
    /// <returns>A task that completes when the scenario is checked.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_node_that_stalls_past_its_lease_cannot_close_the_job_the_other_node_took_over(bool renewalsThawFirst)
    {
        var probe = new LeaseLossProbe();

        await using var pair = await TwoNodeCluster.StartAsync(
            NewDatabase(),
            configure: builder =>
            {
                builder.AddJobHandler<LeaseLossHandler>(LeaseLossHandlerKey);
                builder.Services.AddSingleton(probe);
                probe.Install(builder.Services);
            },
            configureServices: static services => services.Configure<TraconSchedulingOptions>(static options =>
            {
                options.PollInterval = TimeSpan.FromMilliseconds(100);
                options.LeaseDuration = TimeSpan.FromSeconds(2);
            }));

        var jobs = pair.A.Services.GetRequiredService<IJobStore>();
        var job = await pair.A.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(
            new JobRequest
            {
                TenantId = "default",
                HandlerKey = LeaseLossHandlerKey,
                TargetName = "lease-loss",
                MaxAttempts = 3,
            },
            TestContext.Current.CancellationToken);

        // Attempt 1 starts on one node, which stalls at once; the lease runs out and the other node starts attempt 2.
        var stalled = await probe.FirstAttemptRunning.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var holder = await probe.SecondAttemptRunning.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        string.Equals(holder, stalled, StringComparison.Ordinal).ShouldBeFalse("the stalled node cannot lease the job again while it is stalled");

        var held = (await jobs.GetAsync("default", job.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        held.Status.ShouldBe(JobStatus.Running);
        held.Attempt.ShouldBe(2);
        held.LeaseOwner.ShouldBe(holder);

        // The stalled node knew before it woke up: its renewal never answered, the lease could no longer be proven, and the
        // worker told the handler while every call the node made was still frozen.
        await probe.FirstAttemptCancelled.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // The stalled node wakes up.
        if (renewalsThawFirst)
        {
            probe.ThawRenewals();
            probe.ThawWrites();
        }
        else
        {
            probe.ThawWrites();
            probe.ThawRenewals();
        }

        probe.ResumeFirstAttempt.TrySetResult();
        await probe.FirstAttemptReturned.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Whatever the stalled node was going to write would reach the store at once: it has no signal to wait on, so the
        // time is given for a write that must NOT come.
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken); // delay: negative

        // Nothing the stalled node did changed the job the other node holds.
        var afterWake = (await jobs.GetAsync("default", job.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        afterWake.Status.ShouldBe(JobStatus.Running, "the stalled node closed or requeued the other node's attempt");
        afterWake.Attempt.ShouldBe(2);
        afterWake.LeaseOwner.ShouldBe(holder);
        afterWake.CompletedAt.ShouldBeNull();
        afterWake.ErrorMessage.ShouldBeNull();

        probe.FinishSecondAttempt.TrySetResult();

        var finished = await WaitUntil.ValueAsync(
            async () => await jobs.GetAsync("default", job.Id, TestContext.Current.CancellationToken),
            static current => current?.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Pending,
            "the node that holds the job to close it");

        finished.ShouldNotBeNull().Status.ShouldBe(JobStatus.Completed);
        finished.Attempt.ShouldBe(2);
        finished.ErrorMessage.ShouldBeNull();

        // Closed once, by the node that held the lease; the stalled node's attempt applied nothing. The job reads as closed
        // the moment the store has written it, a little before the worker has noted the answer: wait for the note.
        await probe.WaitForAsync(holder, "complete", applied: true);
        probe.Applied().ShouldBe([$"{holder}:2:complete"]);

        // The stalled node gave the lease up by itself: the renewal it held was cancelled with it and never reached the
        // store, and the worker wrote nothing for an attempt it had lost.
        probe.HasCall(stalled, "renew").ShouldBeFalse();
        probe.HasCall(stalled, "complete").ShouldBeFalse();
        probe.HasCall(stalled, "release").ShouldBeFalse();
        probe.Executions.OrderBy(static execution => execution.Attempt).Select(static execution => execution.Attempt).ShouldBe([1, 2]);
    }

    /// <summary>
    /// Freezes the calls of one node's worker to the database and records what every call came to. The node is named by
    /// its lease owner, which each worker makes unique.
    /// </summary>
    private sealed class LeaseLossProbe
    {
        private readonly TaskCompletionSource _writesThawed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _renewalsThawed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _firstAttemptRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _secondAttemptRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstAttemptCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstAttemptReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<(string Owner, int Attempt, string Call, bool Applied)> _calls = new();
        private readonly ConcurrentQueue<string> _polls = new();
        private string? _stalledOwner;

        public Task<string> FirstAttemptRunning => _firstAttemptRunning.Task;

        /// <summary>Completes when the first attempt's handler token is cancelled.</summary>
        public Task FirstAttemptCancelled => _firstAttemptCancelled.Task;

        /// <summary>Completes when the first attempt's handler has returned.</summary>
        public Task FirstAttemptReturned => _firstAttemptReturned.Task;

        public Task<string> SecondAttemptRunning => _secondAttemptRunning.Task;

        public TaskCompletionSource ResumeFirstAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FinishSecondAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets the gate a handler that has no scenario of its own waits on until the test lets it finish.</summary>
        public TaskCompletionSource ReleaseHandler { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<(string Owner, int Attempt)> Executions { get; } = new();

        /// <summary>Wraps the job store the host already registered; call it after the database is chosen.</summary>
        /// <param name="services">The host's services.</param>
        /// <param name="leaseGate">
        /// A task this host's worker waits for before its first ask for a job, or <see langword="null"/> to ask at once. A
        /// scenario that needs another node to take the job first holds this one back with it.
        /// </param>
        public void Install(IServiceCollection services, Task? leaseGate = null)
        {
            var registered = services.Last(static descriptor => descriptor.ServiceType == typeof(IJobStore));

            services.Remove(registered);
            services.AddSingleton<IJobStore>(provider => new StallableJobStore(
                (IJobStore)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!),
                this,
                leaseGate));
        }

        /// <summary>Freezes the node: its worker's calls wait until they are thawed.</summary>
        /// <param name="owner">The node's lease owner.</param>
        /// <returns><see langword="true"/> if this call froze a node; <see langword="false"/> if one is frozen already.</returns>
        public bool Stall(string owner) => Interlocked.CompareExchange(ref _stalledOwner, owner, null) is null;

        public void ThawWrites() => _writesThawed.TrySetResult();

        public void ThawRenewals() => _renewalsThawed.TrySetResult();

        public void Started(string owner, int attempt)
        {
            Executions.Enqueue((owner, attempt));

            if (attempt == 1)
            {
                _firstAttemptRunning.TrySetResult(owner);
            }
            else if (attempt == 2)
            {
                _secondAttemptRunning.TrySetResult(owner);
            }
        }

        public Task WritesGateAsync(string owner, CancellationToken cancellationToken)
            => IsStalled(owner) ? _writesThawed.Task.WaitAsync(cancellationToken) : Task.CompletedTask;

        public Task RenewalsGateAsync(string owner, CancellationToken cancellationToken)
            => IsStalled(owner) ? _renewalsThawed.Task.WaitAsync(cancellationToken) : Task.CompletedTask;

        public void Record(JobLease lease, string call, bool applied)
            => _calls.Enqueue((lease.Owner, lease.Attempt, call, applied));

        /// <summary>Records that a node's worker asked the store for a job.</summary>
        /// <param name="owner">The node's lease owner.</param>
        public void RecordPoll(string owner) => _polls.Enqueue(owner);

        /// <summary>Counts the times the workers of the nodes other than one asked the store for a job.</summary>
        /// <param name="owner">The lease owner of the node that is left out.</param>
        /// <returns>The count.</returns>
        public int PollsOtherThan(string owner) => _polls.Count(poll => !string.Equals(poll, owner, StringComparison.Ordinal));

        /// <summary>Counts the renewals the store extended for a node.</summary>
        /// <param name="owner">The node's lease owner.</param>
        /// <returns>The count.</returns>
        public int RenewalsBy(string owner)
            => _calls.Count(entry => string.Equals(entry.Owner, owner, StringComparison.Ordinal) && string.Equals(entry.Call, "renew", StringComparison.Ordinal) && entry.Applied);

        /// <summary>Tells the first attempt's handler token was cancelled.</summary>
        public void TokenCancelled() => _firstAttemptCancelled.TrySetResult();

        /// <summary>Tells the first attempt's handler has returned.</summary>
        public void FirstAttemptDone() => _firstAttemptReturned.TrySetResult();

        /// <summary>Tells whether the store answered a node's call, applied or not.</summary>
        /// <param name="owner">The node's lease owner.</param>
        /// <param name="call">The call: <c>renew</c>, <c>complete</c> or <c>release</c>.</param>
        /// <returns><see langword="true"/> if such a call reached the store.</returns>
        public bool HasCall(string owner, string call)
            => _calls.Any(entry => string.Equals(entry.Owner, owner, StringComparison.Ordinal) && string.Equals(entry.Call, call, StringComparison.Ordinal));

        /// <summary>Waits until a node's call came back with the given answer.</summary>
        /// <param name="owner">The node's lease owner.</param>
        /// <param name="call">The call: <c>renew</c>, <c>complete</c> or <c>release</c>.</param>
        /// <param name="applied">The answer the store gave.</param>
        /// <returns>A task that completes when such a call was recorded.</returns>
        public Task WaitForAsync(string owner, string call, bool applied)
            => WaitUntil.TrueAsync(
                () => _calls.Any(entry => string.Equals(entry.Owner, owner, StringComparison.Ordinal) && string.Equals(entry.Call, call, StringComparison.Ordinal) && entry.Applied == applied),
                $"the store to answer {owner}'s {call} with {applied}");

        /// <summary>Lists the lease-bound writes the store applied, as <c>owner:attempt:call</c>.</summary>
        /// <returns>The applied writes, in the order they were answered.</returns>
        public IReadOnlyList<string> Applied()
            => [.. _calls
                .Where(static entry => entry.Applied && entry.Call is "complete" or "release")
                .Select(static entry => $"{entry.Owner}:{entry.Attempt}:{entry.Call}")];

        private bool IsStalled(string owner) => string.Equals(Volatile.Read(ref _stalledOwner), owner, StringComparison.Ordinal);
    }

    private sealed class LeaseLossHandler(LeaseLossProbe probe) : IJobHandler
    {
        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            var owner = context.Job.LeaseOwner!;

            probe.Started(owner, context.Job.Attempt);

            if (context.Job.Attempt == 1)
            {
                // The node stalls with its handler in the middle of the work. A paused process ignores its
                // cancellation token too, so the handler is released by the test, not by the token.
                probe.Stall(owner);

                using var registration = cancellationToken.Register(static state => ((LeaseLossProbe)state!).TokenCancelled(), probe);

                await probe.ResumeFirstAttempt.Task.ConfigureAwait(false);

                probe.FirstAttemptDone();

                return;
            }

            await probe.FinishSecondAttempt.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Delegates to the real store; the calls of a frozen node wait first, and the lease-bound ones are recorded.</summary>
    private sealed class StallableJobStore(IJobStore inner, LeaseLossProbe probe, Task? leaseGate = null) : IJobStore
    {
        public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
            => inner.EnqueueAsync(job, items, cancellationToken);

        public async ValueTask<JobRecord?> LeaseAsync(JobLeaseRequest request, CancellationToken cancellationToken = default)
        {
            if (leaseGate is not null)
            {
                await leaseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            await probe.WritesGateAsync(request.Owner, cancellationToken).ConfigureAwait(false);

            var leased = await inner.LeaseAsync(request, cancellationToken).ConfigureAwait(false);

            probe.RecordPoll(request.Owner);

            return leased;
        }

        public async ValueTask<bool> RenewLeaseAsync(JobLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            await probe.RenewalsGateAsync(lease.Owner, cancellationToken).ConfigureAwait(false);

            var renewed = await inner.RenewLeaseAsync(lease, leaseDuration, cancellationToken).ConfigureAwait(false);

            probe.Record(lease, "renew", renewed);

            return renewed;
        }

        public async ValueTask<bool> MarkRunningAsync(JobLease lease, CancellationToken cancellationToken = default)
        {
            await probe.WritesGateAsync(lease.Owner, cancellationToken).ConfigureAwait(false);

            return await inner.MarkRunningAsync(lease, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default)
        {
            await probe.WritesGateAsync(lease.Owner, cancellationToken).ConfigureAwait(false);

            var applied = await inner.CompleteAsync(lease, completion, cancellationToken).ConfigureAwait(false);

            probe.Record(lease, "complete", applied);

            return applied;
        }

        public async ValueTask<bool> ReleaseForRetryAsync(JobLease lease, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
        {
            await probe.WritesGateAsync(lease.Owner, cancellationToken).ConfigureAwait(false);

            var applied = await inner.ReleaseForRetryAsync(lease, errorMessage, retryAfter, cancellationToken).ConfigureAwait(false);

            probe.Record(lease, "release", applied);

            return applied;
        }

        public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => inner.CancelAsync(tenantId, jobId, cancellationToken);

        public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, jobId, cancellationToken);

        public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
            => inner.QueryAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
            => inner.ListItemsAsync(jobId, cancellationToken);

        public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
            => inner.ReportItemAsync(item, cancellationToken);

        public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
            => inner.GetQueueDepthAsync(cancellationToken);
    }
}
