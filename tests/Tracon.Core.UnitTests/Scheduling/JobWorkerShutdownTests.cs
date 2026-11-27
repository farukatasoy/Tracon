using Microsoft.Extensions.Logging;
using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// What the job worker writes for the jobs that are running when the host stops: the attempt a handler hands back, the
/// completion of a handler that finished anyway, and the writes that must not hold the stop.
/// </summary>
/// <remarks>
/// <para>
/// A host that stops is not a crash, but the worker used to treat both alike: the cut-off attempt stayed counted, so a job
/// limited to one attempt failed on the next host without its handler running, and a handler that returned normally while
/// the host was stopping lost its completion (the write was bound to the token that had already fallen).
/// </para>
/// <para>
/// The worker never gives an attempt back on its own. It does so when the handler says it had not begun its work
/// (<see cref="JobInterruptedException"/>) and only while it is stopping; a handler that began its work keeps the attempt.
/// </para>
/// </remarks>
public sealed class JobWorkerShutdownTests
{
    [Fact]
    public async Task A_handler_that_stopped_before_its_work_gives_the_attempt_back_and_the_next_worker_runs_the_job()
    {
        var store = new InMemoryJobStore();
        var interrupted = new InterruptedOnStopHandler();
        var (first, firstHost) = Worker(store, interrupted);
        Guid id;

        using (firstHost)
        using (first)
        {
            id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await first.StartAsync(TestContext.Current.CancellationToken);
            await interrupted.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            await first.StopAsync(TestContext.Current.CancellationToken);
        }

        // Back in the queue as if it had never been leased: the attempt the cut-off lease counted is the job's again.
        var parked = (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        parked.Status.ShouldBe(JobStatus.Pending);
        parked.Attempt.ShouldBe(0);
        parked.LeaseOwner.ShouldBeNull();

        // The next host runs the handler, which a job limited to one attempt could not have had after a crash.
        var next = new CompletingHandler();
        var (second, secondHost) = Worker(store, next);

        using (secondHost)
        using (second)
        {
            await second.StartAsync(TestContext.Current.CancellationToken);

            var done = await WaitForAsync(store, id, JobStatus.Completed, JobStatus.Failed);

            await second.StopAsync(TestContext.Current.CancellationToken);

            next.Calls.ShouldBe(1);
            done.Status.ShouldBe(JobStatus.Completed);
            done.Attempt.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Without_the_handlers_word_a_job_cut_off_by_the_stop_keeps_its_attempt()
    {
        // The worker never gives an attempt back on its own: this handler may have done work before it was cut off.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new StopsThenThrowsHandler(static () => new InvalidOperationException("It failed on its way out."));
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            await worker.StopAsync(TestContext.Current.CancellationToken);

            var job = (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            job.Status.ShouldBe(JobStatus.Running);
            job.Attempt.ShouldBe(1);
            store.ReleaseInterruptedCalls.ShouldBe(0);
        }
    }

    [Fact]
    public async Task An_interrupted_exception_while_the_worker_is_not_stopping_is_an_ordinary_failed_attempt_and_is_logged()
    {
        // A handler cannot use it to run its job again for nothing while the process stays up.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new ThrowsAtOnceHandler(static () => new JobInterruptedException("Not begun, it said."));
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Failed, JobStatus.Completed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Failed);
            job.Attempt.ShouldBe(1);
            job.ErrorMessage.ShouldNotBeNull().ShouldNotContain("Not begun", Case.Sensitive);
            store.ReleaseInterruptedCalls.ShouldBe(0, "a worker that is not stopping gave an attempt back");
            logs.Entries.ShouldContain(static entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("not stopping", StringComparison.Ordinal) &&
                entry.Message.Contains("Not begun, it said.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_release_the_store_fails_leaves_the_job_to_its_lease_and_the_stop_goes_on()
    {
        var store = new ProbeJobStore(new InMemoryJobStore())
        {
            ReleaseInterruptedFailure = new TimeoutException("The store did not answer."),
        };
        var handler = new InterruptedOnStopHandler();
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            await worker.StopAsync(TestContext.Current.CancellationToken);

            // The old rule applies: the job runs until its lease ends, and that lease counts as the attempt.
            var job = (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            job.Status.ShouldBe(JobStatus.Running);
            job.Attempt.ShouldBe(1);
            store.ReleaseInterruptedCalls.ShouldBe(1, "one try, no retry loop");
            logs.Entries.ShouldContain(static entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("lease", StringComparison.Ordinal) &&
                entry.Exception is TimeoutException);
        }
    }

    [Fact]
    public async Task A_handler_that_returns_normally_while_the_worker_stops_completes_its_job()
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        handler.For("work").Behavior = ProbeBehavior.IgnoreToken;
        var publisher = new RecordingWebhookPublisher();
        var (worker, host) = Worker(store, handler, webhookPublisher: publisher);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("work").Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            var stopping = worker.StopAsync(TestContext.Current.CancellationToken);

            // The token has fallen; the handler, which ignored it, finishes its work now.
            await handler.For("work").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            handler.For("work").Release.TrySetResult();

            await stopping.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            handler.For("work").Signals!.Reason.ShouldBe(JobStopReason.WorkerStopping);

            var job = (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            job.Status.ShouldBe(JobStatus.Completed, "its work was done; the job would have run again");
            job.Attempt.ShouldBe(1);
            job.ErrorMessage.ShouldBeNull();
            publisher.Published.ShouldBe([WebhookEvents.JobCompleted]);
        }
    }

    [Fact]
    public async Task A_completion_that_fails_while_the_worker_stops_is_tried_once_and_the_job_is_left_to_its_lease()
    {
        // Retrying belongs to a running worker: its window is the lease. A stopping one makes one try.
        var store = new ProbeJobStore(new InMemoryJobStore())
        {
            BeforeComplete = static _ => throw new TimeoutException("The store did not answer."),
        };
        var handler = new TokenProbeHandler();
        handler.For("work").Behavior = ProbeBehavior.IgnoreToken;
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("work").Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            var stopping = worker.StopAsync(TestContext.Current.CancellationToken);

            await handler.For("work").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            handler.For("work").Release.TrySetResult();

            await stopping.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            store.CompleteCalls.ShouldBe(1, "a stopping worker tried the completion again");
            (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken))!.Status.ShouldBe(JobStatus.Running);
            logs.Entries.ShouldContain(static entry =>
                entry.Level >= LogLevel.Warning &&
                entry.Message.Contains("lease", StringComparison.Ordinal) &&
                entry.Exception is TimeoutException);
        }
    }

    [Fact]
    public async Task A_completion_the_store_never_answers_does_not_hold_the_stop_past_the_stop_call_budget()
    {
        // A stopping worker gives each store call four seconds, not the lease duration a running one is given: the host's
        // own shutdown timeout is thirty seconds, and the calls of one finalization follow each other.
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore())
        {
            BeforeComplete = static token => Task.Delay(Timeout.InfiniteTimeSpan, token), // delay: simulated
        };
        var handler = new TokenProbeHandler();
        handler.For("work").Behavior = ProbeBehavior.IgnoreToken;
        var (worker, host) = Worker(store, handler, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await StartOnClockAsync(worker, clock);
            await handler.For("work").Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            var stopping = worker.StopAsync(TestContext.Current.CancellationToken);

            await handler.For("work").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            handler.For("work").Release.TrySetResult();

            await WaitUntil.TrueAsync(() => store.CompleteCalls == 1, "the completion to be asked for");
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(TimeSpan.FromSeconds(4)), "the stop's call budget to be armed");

            clock.Pending.ShouldNotContain(TimeSpan.FromMinutes(5), "a stopping worker waited a lease duration for one call");
            stopping.IsCompleted.ShouldBeFalse("the stop waits for the completion up to its budget");

            clock.Advance(TimeSpan.FromSeconds(4));

            await stopping.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            store.CompleteCalls.ShouldBe(1);
            (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken))!.Status.ShouldBe(JobStatus.Running);
        }
    }

    [Fact]
    public async Task A_webhook_publisher_that_never_answers_does_not_hold_the_stop_past_the_stop_call_budget()
    {
        // The completion is written; the event of it is the last call, and the publisher has no time limit of its own.
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        handler.For("work").Behavior = ProbeBehavior.IgnoreToken;
        var publisher = new HangingWebhookPublisher();
        var (worker, host) = Worker(store, handler, clock: clock, webhookPublisher: publisher);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await StartOnClockAsync(worker, clock);
            await handler.For("work").Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            var stopping = worker.StopAsync(TestContext.Current.CancellationToken);

            await handler.For("work").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            handler.For("work").Release.TrySetResult();

            await WaitUntil.TrueAsync(() => publisher.Calls == 1, "the event to be published");
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(TimeSpan.FromSeconds(4)), "the stop's call budget to be armed");

            stopping.IsCompleted.ShouldBeFalse("the stop waits for the publisher up to its budget");

            clock.Advance(TimeSpan.FromSeconds(4));

            await stopping.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken))!.Status.ShouldBe(JobStatus.Completed);
        }
    }

    [Fact]
    public async Task A_stop_that_lands_in_the_middle_of_the_completion_does_not_lose_the_work_that_was_done()
    {
        // The handler returned while the worker was not stopping, so the writes are the ones of a running worker, bound to the
        // stop's token. The stop arrives while the completion is in flight: that write ends in the stop's cancellation, and
        // the job used to stay Running and run its handler again. The worker tries once more, under no token of the stop's.
        var store = new ProbeJobStore(new InMemoryJobStore());
        store.BeforeComplete = async token =>
        {
            // The first call is the one the stop interrupts; the next one is the worker's last try.
            if (store.CompleteCalls == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token); // delay: simulated
            }
        };
        var handler = new CompletingHandler();
        var publisher = new RecordingWebhookPublisher();
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs, webhookPublisher: publisher);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await WaitUntil.TrueAsync(() => store.CompleteCalls == 1, "the completion to be in flight");

            await worker.StopAsync(TestContext.Current.CancellationToken);

            handler.Calls.ShouldBe(1);
            store.CompleteCalls.ShouldBe(2, "the interrupted write was not tried again");

            var job = (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            job.Status.ShouldBe(JobStatus.Completed, "the work was done; a stop that landed in its completion lost it");
            job.Attempt.ShouldBe(1);
            publisher.Published.ShouldBe([WebhookEvents.JobCompleted]);
            logs.Entries.ShouldNotContain(static entry => entry.Level >= LogLevel.Warning);
        }
    }

    [Fact]
    public async Task A_stop_that_lands_in_the_middle_of_the_completion_is_not_taken_for_a_lost_lease_when_the_write_had_applied()
    {
        // The cancelled call may have applied the completion before the cancellation reached the worker. The last try then
        // finds the job closed and is refused; the worker reads the job back before it calls that a lost lease, and counts
        // the job as the completion it is.
        var inner = new InMemoryJobStore();
        var store = new ProbeJobStore(inner);
        JobRecord? leased = null;
        store.Leased = job => leased = job;
        store.BeforeComplete = async token =>
        {
            if (store.CompleteCalls == 1)
            {
                // Applies the write itself, as a store that committed and then lost the answer does, and never answers.
                (await inner.CompleteAsync(
                    JobLease.Of(leased!),
                    new JobCompletion { Status = JobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow },
                    CancellationToken.None)).ShouldBeTrue();

                await Task.Delay(Timeout.InfiniteTimeSpan, token); // delay: simulated
            }
        };
        var handler = new CompletingHandler();
        var publisher = new RecordingWebhookPublisher();
        var (worker, host) = Worker(store, handler, webhookPublisher: publisher);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await WaitUntil.TrueAsync(() => store.CompleteCalls == 1, "the completion to be in flight");

            await worker.StopAsync(TestContext.Current.CancellationToken);

            (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken))!.Status.ShouldBe(JobStatus.Completed);
            publisher.Published.ShouldBe([WebhookEvents.JobCompleted], "the completion was counted as a lost lease and its event dropped");
        }
    }

    [Fact]
    public async Task A_stop_with_the_lease_already_lost_gives_no_attempt_back()
    {
        // The store refused a renewal, so the job is another worker's: whatever the handler says on its way out, this
        // worker writes nothing for the attempt, an interrupted release included.
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore()) { RefuseRenewals = true };
        var handler = new HoldsThenInterruptedHandler();
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await StartOnClockAsync(worker, clock);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            // The lease runs for five minutes and is renewed at a third of that: the first renewal is refused.
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(TimeSpan.FromSeconds(100)), "the renewal timer to be armed");
            clock.Advance(TimeSpan.FromSeconds(100));
            await WaitUntil.TrueAsync(
                () => logs.Entries.Any(static entry => entry.Message.Contains("no longer holds the lease", StringComparison.Ordinal)),
                "the worker to learn that the lease is lost");

            // Now the host stops, and the handler, which had not begun, says so.
            var stopping = worker.StopAsync(TestContext.Current.CancellationToken);
            handler.Release.TrySetResult();

            await stopping.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            store.ReleaseInterruptedCalls.ShouldBe(0, "a worker that lost its lease gave an attempt back that is another worker's");
            store.CompleteCalls.ShouldBe(0);

            var job = (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            job.Status.ShouldBe(JobStatus.Running);
            job.Attempt.ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_job_cancelled_before_the_stop_is_not_given_an_attempt_back_and_nothing_is_logged_as_a_problem()
    {
        // The cancel watch never ticks on this clock, so the worker meets the cancel at the release: the store answers
        // false because cancelling cleared the lease. That is an answer, not an error.
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new InterruptedOnStopHandler();
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "work", maxAttempts: 1);

            await StartOnClockAsync(worker, clock);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            (await store.CancelAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldBeTrue();

            await worker.StopAsync(TestContext.Current.CancellationToken);

            store.ReleaseInterruptedCalls.ShouldBe(1);

            var job = (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            job.Status.ShouldBe(JobStatus.Cancelled, "a cancelled job went back to the queue");
            job.Attempt.ShouldBe(1);
            logs.Entries.ShouldContain(static entry =>
                entry.Level == LogLevel.Information &&
                entry.Message.Contains("was not given back", StringComparison.Ordinal));
            logs.Entries.ShouldNotContain(static entry => entry.Level >= LogLevel.Warning);
        }
    }

    /// <summary>Waits for its token to be cancelled, and says it had not begun its work.</summary>
    private sealed class InterruptedOnStopHandler : IJobHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }
            catch (OperationCanceledException)
            {
                throw new JobInterruptedException("The host stopped before the first call out.");
            }
        }
    }

    /// <summary>Holds its job until the test releases it, and then says it had not begun its work.</summary>
    private sealed class HoldsThenInterruptedHandler : IJobHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            await Release.Task.ConfigureAwait(false);

            throw new JobInterruptedException("The host stopped before the first call out.");
        }
    }

    /// <summary>Waits for its token to be cancelled and then throws what <paramref name="failure"/> builds.</summary>
    private sealed class StopsThenThrowsHandler(Func<Exception> failure) : IJobHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }
            catch (OperationCanceledException)
            {
                throw failure();
            }
        }
    }

    /// <summary>Throws what <paramref name="failure"/> builds, on its first call.</summary>
    private sealed class ThrowsAtOnceHandler(Func<Exception> failure) : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => throw failure();
    }

    /// <summary>Counts its calls and succeeds.</summary>
    private sealed class CompletingHandler : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A publisher that takes the event and never answers, as a queue that stopped responding does.</summary>
    private sealed class HangingWebhookPublisher : IWebhookPublisher
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask<int> PublishAsync(string tenantId, string eventType, WebhookEventPayload payload, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

            return 0;
        }
    }

    private sealed class RecordingWebhookPublisher : IWebhookPublisher
    {
        private readonly List<string> _published = [];

        public IReadOnlyList<string> Published
        {
            get
            {
                lock (_published)
                {
                    return [.. _published];
                }
            }
        }

        public ValueTask<int> PublishAsync(string tenantId, string eventType, WebhookEventPayload payload, CancellationToken cancellationToken = default)
        {
            lock (_published)
            {
                _published.Add(eventType);
            }

            return new ValueTask<int>(1);
        }
    }
}
