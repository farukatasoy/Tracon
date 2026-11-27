using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// A handler that knows another attempt cannot succeed fails its job with <see cref="JobPermanentFailureException"/>:
/// on the attempt that threw, with the code it named and a log reference, whatever attempts are left.
/// </summary>
/// <remarks>
/// Any other exception a handler throws counts as a failed attempt and is run again, so the first case here is the
/// one a consumer cannot get any other way. The free text of the exception is for the log: the job's error message is
/// stored, returned over HTTP and shown in the UI.
/// </remarks>
public sealed class JobWorkerPermanentFailureTests
{
    private const string Reason = "Order 42 of Jane Doe is not in the database.";

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_handler_that_fails_for_good_fails_its_job_on_the_attempt_that_threw(int maxAttempts)
    {
        var store = new InMemoryJobStore();
        var handler = new ThrowingHandler(static _ => new JobPermanentFailureException("order.not-found", Reason, new KeyNotFoundException("no row")));
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "order-42", maxAttempts);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Failed, JobStatus.Completed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            handler.Calls.ShouldBe(1, "a handler that said it fails for good was run again");
            job.Status.ShouldBe(JobStatus.Failed);
            job.Attempt.ShouldBe(1);

            var message = job.ErrorMessage.ShouldNotBeNull();

            Regex.IsMatch(message, @"^order\.not-found \(ref: [0-9a-f]{8}\)$", RegexOptions.None, TimeSpan.FromSeconds(5))
                .ShouldBeTrue($"the stored message was '{message}'");
            message.ShouldNotContain("Jane");

            // The free text is not lost: the log entry under the same reference carries it, and the exception.
            var reference = message[(message.IndexOf("(ref: ", StringComparison.Ordinal) + 6)..^1];
            var entry = logs.Entries.Where(e => e.Message.Contains(reference, StringComparison.Ordinal)).ToList().ShouldHaveSingleItem();

            entry.Level.ShouldBe(LogLevel.Error);
            entry.Message.ShouldContain("order.not-found");
            entry.Message.ShouldContain(Reason);
            entry.Exception.ShouldBeOfType<JobPermanentFailureException>().InnerException.ShouldBeOfType<KeyNotFoundException>();
        }
    }

    [Fact]
    public async Task A_failure_that_is_not_marked_permanent_is_still_run_for_every_attempt()
    {
        var store = new InMemoryJobStore();
        var handler = new ThrowingHandler(static _ => new InvalidOperationException("The order is not ready."));
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "order-42", maxAttempts: 3);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Failed, JobStatus.Completed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            handler.Calls.ShouldBe(3);
            job.Status.ShouldBe(JobStatus.Failed);
            job.Attempt.ShouldBe(3);
            job.ErrorMessage.ShouldNotBeNull().ShouldStartWith("InvalidOperationException failed.");
        }
    }

    [Fact]
    public async Task A_job_cancelled_while_its_handler_fails_for_good_stays_cancelled_and_the_worker_writes_nothing()
    {
        // The cancel watch saw the cancel and the worker does not treat the exception as a failure at all: no completion is
        // written and nothing is logged as an error. The store's terminal guard is not what keeps the job cancelled here.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new ThrowsOnTokenHandler(static () => new JobPermanentFailureException("order.not-found"));
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "order-42", maxAttempts: 3);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            await store.CancelAsync(Tenant, id, TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Cancelled);

            await handler.Thrown.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            // The attempt is over once the worker holds no job: a stop that came earlier would outrank the cancel.
            await WaitUntil.TrueAsync(() => worker.RunningJobs == 0, "the worker to finish with the attempt");
            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Cancelled);
            (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken))!.ErrorMessage.ShouldBeNull();
            store.CompleteCalls.ShouldBe(0, "the worker wrote a completion for a cancelled job");
            logs.Entries.Where(static entry => entry.Level >= LogLevel.Error).Select(static entry => entry.Message).ToList()
                .ShouldBeEmpty("a cancelled job was logged as an error");
        }
    }

    [Fact]
    public async Task A_job_cancelled_before_the_watch_saw_it_gets_no_failed_for_good_entry_and_no_completion()
    {
        // The handler cancels its own job and throws at once, on a clock that never ticks the watch: the worker reaches
        // its failure path and finds the job cancelled there. The "failed for good" error is the record of a failure, and
        // this job did not fail: it is written after that check, so a cancelled job gets none.
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new CancelsThenThrowsHandler(store, static () => new JobPermanentFailureException("order.not-found", Reason));
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "order-42", maxAttempts: 3);

            await StartOnClockAsync(worker, clock);
            await handler.Thrown.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            // The attempt is over once the worker holds no job: a stop that came earlier would outrank what is under test.
            await WaitUntil.TrueAsync(() => worker.RunningJobs == 0, "the worker to finish with the attempt");
            await worker.StopAsync(TestContext.Current.CancellationToken);

            var job = (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            job.Status.ShouldBe(JobStatus.Cancelled);
            job.ErrorMessage.ShouldBeNull();
            store.CompleteCalls.ShouldBe(0, "the worker wrote a completion for a cancelled job");
            logs.Entries.ShouldNotContain(
                static entry => entry.Message.Contains("failed for good", StringComparison.Ordinal),
                "a cancelled job was logged as failed for good");
            logs.Entries.Where(static entry => entry.Level >= LogLevel.Error).Select(static entry => entry.Message).ToList()
                .ShouldBeEmpty("a cancelled job was logged as an error");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_permanent_failure_thrown_after_the_time_limit_does_not_override_the_time_limit(int maxAttempts)
    {
        // The limit cancelled the handler's token and the attempt failed with the limit's code, whatever the handler threw
        // on its way out. A handler that wraps every exception into a permanent failure must not turn a timeout into one:
        // the time limit decides, the job is leased again while attempts are left, and the log says the code was set aside.
        var limit = TimeSpan.FromSeconds(30);
        var clock = new ManualClock();
        var store = new InMemoryJobStore();
        var handler = new ThrowsOnTokenHandler(static () => new JobPermanentFailureException("order.not-found"));
        var logs = new WorkerLogCollector();
        var (worker, host) = Worker(store, handler, logger: logs, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "order-42", maxAttempts, maxDuration: limit);

            await StartOnClockAsync(worker, clock);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            clock.Advance(limit);

            var job = await WaitUntil.ValueAsync(
                () => store.GetAsync(Tenant, id, TestContext.Current.CancellationToken).AsTask(),
                static job => job?.ErrorMessage is not null,
                "the attempt to be written");

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job!.Attempt.ShouldBe(1);
            job.ErrorMessage.ShouldNotBeNull().ShouldStartWith($"{JobErrorCodes.MaxDurationExceeded} (ref: ");
            job.Status.ShouldBe(maxAttempts == 1 ? JobStatus.Failed : JobStatus.Pending, "the time limit decides: attempts left, the job is leased again");
            logs.Entries.ShouldContain(static entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("order.not-found", StringComparison.Ordinal) &&
                entry.Message.Contains("time limit", StringComparison.Ordinal));
        }
    }

    /// <summary>Throws what <paramref name="failure"/> builds, on every call.</summary>
    private sealed class ThrowingHandler(Func<int, Exception> failure) : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => throw failure(Interlocked.Increment(ref _calls));
    }

    /// <summary>Cancels its own job in the store, then throws what <paramref name="failure"/> builds, at once.</summary>
    private sealed class CancelsThenThrowsHandler(IJobStore store, Func<Exception> failure) : IJobHandler
    {
        public TaskCompletionSource Thrown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            _ = await store.CancelAsync(context.Job.TenantId, context.Job.Id, CancellationToken.None);

            Thrown.TrySetResult();

            throw failure();
        }
    }

    /// <summary>Waits for its token to be cancelled, whatever the reason, and then throws what <paramref name="failure"/> builds.</summary>
    private sealed class ThrowsOnTokenHandler(Func<Exception> failure) : IJobHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Thrown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }
            catch (OperationCanceledException)
            {
                Thrown.TrySetResult();

                throw failure();
            }
        }
    }
}
