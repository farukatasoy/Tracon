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
    public async Task A_job_cancelled_while_its_handler_fails_for_good_stays_cancelled()
    {
        var store = new InMemoryJobStore();
        var handler = new ThrowsOnTokenHandler(static () => new JobPermanentFailureException("order.not-found"));
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "order-42", maxAttempts: 3);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            await store.CancelAsync(Tenant, id, TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Cancelled);

            await handler.Thrown.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Cancelled);
            (await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken))!.ErrorMessage.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_handler_that_fails_for_good_at_its_time_limit_keeps_its_own_code()
    {
        // The limit only cancelled the token; the handler then decided the work cannot succeed. That decision is the
        // cause, not the limit, so the job does not read as a timeout and is not run again.
        var limit = TimeSpan.FromSeconds(30);
        var clock = new ManualClock();
        var store = new InMemoryJobStore();
        var handler = new ThrowsOnTokenHandler(static () => new JobPermanentFailureException("order.not-found"));
        var (worker, host) = Worker(store, handler, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "order-42", maxAttempts: 3, maxDuration: limit);

            await StartOnClockAsync(worker, clock);
            await handler.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            clock.Advance(limit);

            var job = await WaitForAsync(store, id, JobStatus.Failed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Attempt.ShouldBe(1);
            job.ErrorMessage.ShouldNotBeNull().ShouldStartWith("order.not-found (ref: ");
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
