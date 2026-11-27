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
    public async Task A_completion_the_store_never_answers_does_not_hold_the_stop_past_one_call_budget()
    {
        // The bound is one lease duration on the worker's clock: a store that hangs would otherwise hold the host open.
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
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(TimeSpan.FromMinutes(5)), "the call budget to be armed");

            stopping.IsCompleted.ShouldBeFalse("the stop waits for the completion up to its budget");

            clock.Advance(TimeSpan.FromMinutes(5));

            await stopping.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            store.CompleteCalls.ShouldBe(1);
            (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken))!.Status.ShouldBe(JobStatus.Running);
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
