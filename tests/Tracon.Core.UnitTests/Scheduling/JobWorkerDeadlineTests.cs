using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// A job's time limit (F-323, failure mode 15): the attempt fails with a stable code,
/// counts like any failed attempt, and neither a shutdown nor a cancel is mistaken for
/// it.
/// </summary>
/// <remarks>
/// <para>
/// The handler is given one token linked to three sources. The outcome is read from
/// the source that fired, never from the exception the handler threw on its way out:
/// that exception is the handler noticing its token, not the cause (K-737).
/// </para>
/// <para>
/// Most cases run on real time with short limits; their bounds are failure bounds. The
/// case that proves WHEN the limit fires advances the worker's clock
/// (<see cref="ManualClock"/>): a tick before the limit nothing happens, at the limit the
/// token falls.
/// </para>
/// </remarks>
public sealed class JobWorkerDeadlineTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task An_attempt_past_its_limit_on_its_last_attempt_fails_the_job_with_the_code()
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        var logs = new CapturingLogs();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var (worker, host) = Worker(store, handler, logger: loggerFactory.CreateLogger<JobWorkerBackgroundService>());

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow", maxAttempts: 1, maxDuration: Limit);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Failed);

            // Read before the worker stops: a stopping worker outranks every other reason.
            handler.For("slow").Signals!.Reason.ShouldBe(JobStopReason.MaxDurationExceeded);
            handler.For("slow").Signals!.MaxDuration.ShouldBe(Limit);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.ErrorMessage.ShouldNotBeNull();
            job.ErrorMessage.ShouldStartWith($"{JobErrorCodes.MaxDurationExceeded} (ref: ");

            // The persisted code and the log entry that carries the handler's
            // exception share one reference.
            var reference = job.ErrorMessage[(job.ErrorMessage.IndexOf("(ref: ", StringComparison.Ordinal) + 6)..^1];

            logs.Entries.ShouldContain(entry =>
                entry.Message.Contains(reference, StringComparison.Ordinal) &&
                entry.Message.Contains("time limit", StringComparison.Ordinal) &&
                entry.Exception is OperationCanceledException);
        }
    }

    [Fact]
    public async Task The_limit_fires_on_the_workers_clock_at_the_jobs_max_duration_and_not_before()
    {
        // Below the lease renewal's interval (a third of the five-minute lease), so the
        // advance fires only the watch and the deadline.
        var limit = TimeSpan.FromSeconds(30);
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        var (worker, host) = Worker(store, handler, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow", maxAttempts: 1, maxDuration: limit);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("slow").Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            clock.Pending.ShouldContain(limit);

            clock.Advance(limit - TimeSpan.FromTicks(1));
            handler.For("slow").Token.IsCancellationRequested.ShouldBeFalse("a tick before the limit");

            clock.Advance(TimeSpan.FromTicks(1));

            await handler.For("slow").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            handler.For("slow").Signals!.Reason.ShouldBe(JobStopReason.MaxDurationExceeded);

            var job = await WaitForAsync(store, id, JobStatus.Failed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.ErrorMessage.ShouldNotBeNull().ShouldStartWith($"{JobErrorCodes.MaxDurationExceeded} (ref: ");
        }
    }

    [Fact]
    public async Task With_attempts_left_the_timed_out_attempt_is_released_and_the_next_attempt_gets_the_whole_limit()
    {
        // Open question 7-A: a timed-out attempt is a failed attempt, leased again
        // while attempts are left, and the limit is per attempt.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new RetryOnceHandler();
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow", maxAttempts: 2, maxDuration: Limit);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Completed, JobStatus.Failed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Completed);
            job.Attempt.ShouldBe(2);
            handler.Calls.ShouldBe(2);
            handler.FirstAttemptReleasedWith.ShouldNotBeNull().ShouldStartWith(JobErrorCodes.MaxDurationExceeded);
        }
    }

    [Fact]
    public async Task A_handler_that_returns_normally_past_its_limit_completes_the_job()
    {
        // Open question 8-A: the work it did stands.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler { DefaultBehavior = ProbeBehavior.ReturnOnToken };
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow", maxAttempts: 1, maxDuration: Limit);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Completed, JobStatus.Failed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Completed);
            job.ErrorMessage.ShouldBeNull();
            handler.For("slow").Token.IsCancellationRequested.ShouldBeTrue("the limit did cancel the token");
        }
    }

    [Fact]
    public async Task The_map_bounds_a_job_without_its_own_limit_and_the_jobs_own_limit_wins()
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        handler.For("own").Behavior = ProbeBehavior.IgnoreToken;
        var (worker, host) = Worker(store, handler, static options => options.MaxDurationByHandlerKey[JobHandlerKeys.AgentBatch] = Limit);

        using (host)
        using (worker)
        {
            var mapped = await EnqueueAsync(store, "mapped", maxAttempts: 1);
            var own = await EnqueueAsync(store, "own", maxAttempts: 1, maxDuration: TimeSpan.FromMinutes(10));

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var cut = await WaitForAsync(store, mapped, JobStatus.Failed);

            cut.ErrorMessage.ShouldNotBeNull().ShouldStartWith(JobErrorCodes.MaxDurationExceeded);
            handler.For("mapped").Signals!.MaxDuration.ShouldBe(Limit);

            // The job's own ten minutes, not the map's limit.
            handler.For("own").Signals!.MaxDuration.ShouldBe(TimeSpan.FromMinutes(10));
            handler.For("own").Token.IsCancellationRequested.ShouldBeFalse();

            handler.For("own").Release.TrySetResult();
            (await WaitForAsync(store, own, JobStatus.Completed)).Status.ShouldBe(JobStatus.Completed);

            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_worker_shutdown_is_not_a_time_limit()
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow", maxAttempts: 1, maxDuration: TimeSpan.FromMinutes(10));

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("slow").Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            handler.For("slow").Signals!.Reason.ShouldBe(JobStopReason.WorkerStopping);

            // Left to its lease, exactly as before: no outcome was written.
            var job = await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken);

            job!.Status.ShouldBe(JobStatus.Running);
            job.ErrorMessage.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_cancel_before_the_limit_is_not_a_time_limit()
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow", maxAttempts: 1, maxDuration: TimeSpan.FromMinutes(10));

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("slow").Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            await store.CancelAsync(Tenant, id, TestContext.Current.CancellationToken);
            await handler.For("slow").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            handler.For("slow").Signals!.Reason.ShouldBe(JobStopReason.Cancelled);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            var job = await store.GetAsync(Tenant, id, TestContext.Current.CancellationToken);

            job!.Status.ShouldBe(JobStatus.Cancelled);
            job.ErrorMessage.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_stored_limit_a_timer_cannot_arm_runs_without_a_limit_and_is_logged()
    {
        // The dispatcher refuses such a value; another writer may not have.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler { DefaultBehavior = ProbeBehavior.Return };
        var logs = new CapturingLogs();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var (worker, host) = Worker(store, handler, logger: loggerFactory.CreateLogger<JobWorkerBackgroundService>());

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "plain", maxAttempts: 1, maxDuration: TimeSpan.Zero);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Completed, JobStatus.Failed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Completed);
            handler.For("plain").Signals!.MaxDuration.ShouldBeNull();
            logs.Entries.ShouldContain(static entry => entry.Level == LogLevel.Error && entry.Message.Contains("runs without a limit", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_stored_limit_a_timer_cannot_arm_falls_back_to_the_map_and_is_logged()
    {
        // F-323 (audit): the operator's map still bounds a job whose own value is bad;
        // the bad value used to switch every limit off.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        var logs = new CapturingLogs();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var (worker, host) = Worker(
            store,
            handler,
            static options => options.MaxDurationByHandlerKey[JobHandlerKeys.AgentBatch] = Limit,
            logger: loggerFactory.CreateLogger<JobWorkerBackgroundService>());

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow", maxAttempts: 1, maxDuration: TimeSpan.FromDays(60));

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Failed);

            handler.For("slow").Signals!.MaxDuration.ShouldBe(Limit, "the map's limit, not none");

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(JobErrorCodes.MaxDurationExceeded);
            logs.Entries.ShouldContain(static entry =>
                entry.Level == LogLevel.Error &&
                entry.Message.Contains("cannot arm", StringComparison.Ordinal) &&
                entry.Message.Contains("MaxDurationByHandlerKey", StringComparison.Ordinal));
        }
    }

    /// <summary>Times out on its first call (waiting on its token), returns at once on the second.</summary>
    private sealed class RetryOnceHandler : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string? FirstAttemptReleasedWith { get; private set; }

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }

            // The second attempt sees what the first one was released with.
            FirstAttemptReleasedWith ??= context.Job.ErrorMessage;
        }
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new Logger(_entries);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue((logLevel, formatter(state, exception), exception));
        }
    }
}
