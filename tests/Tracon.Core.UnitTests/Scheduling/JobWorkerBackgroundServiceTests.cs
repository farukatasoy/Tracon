using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>Shutdown tests for <see cref="JobWorkerBackgroundService"/>.</summary>
public sealed class JobWorkerBackgroundServiceTests
{
    [Fact]
    public async Task StopAsync_waits_for_a_leased_job_to_release_its_concurrency_slot()
    {
        var jobs = new InMemoryJobStore();
        var handler = new BlockingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        var now = DateTimeOffset.UtcNow;

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-a",
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "blocking-handler",
                Status = JobStatus.Pending,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        using var worker = new JobWorkerBackgroundService(
            jobs,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(5),
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            logger: NullLogger<JobWorkerBackgroundService>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var stopping = worker.StopAsync(TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        stopping.IsCompleted.ShouldBeFalse("the worker owns the semaphore until the running job releases its slot");

        handler.Release();

        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_full_lane_does_not_block_the_default_lanes_job()
    {
        var jobs = new InMemoryJobStore();
        var handler = new LaneAwareHandler(blockedLane: "media");
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        var now = DateTimeOffset.UtcNow;

        // Two jobs in "media" (both block forever, once leased) plus one in
        // "default" (completes immediately). With MaxConcurrentJobsPerLane
        // capping "media" at 1, the second media job never even gets a slot --
        // but the default job must complete regardless, proving the lanes
        // do not share a single queue position (129.4's starvation guarantee).
        await EnqueueAsync(jobs, "media", now);
        await EnqueueAsync(jobs, "media", now);
        var defaultJobId = await EnqueueAsync(jobs, "default", now);

        var options = new TraconSchedulingOptions { MaxConcurrentJobs = 2, PollInterval = TimeSpan.FromMilliseconds(5) };
        options.MaxConcurrentJobsPerLane["media"] = 1;

        using var worker = new JobWorkerBackgroundService(
            jobs,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(options),
            new SchemaReadyGate([]),
            new NotDraining(),
            logger: NullLogger<JobWorkerBackgroundService>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await handler.MediaStarted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        JobRecord? defaultJob = null;

        for (var attempt = 0; attempt < 100 && defaultJob?.Status is not JobStatus.Completed; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
            defaultJob = await jobs.GetAsync("tenant-a", defaultJobId, TestContext.Current.CancellationToken);
        }

        defaultJob.ShouldNotBeNull();
        defaultJob!.Status.ShouldBe(JobStatus.Completed, "the default lane must not wait behind a full 'media' lane");

        handler.ReleaseMedia();
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_worker_scoped_to_one_lane_never_leases_another_lane()
    {
        var jobs = new InMemoryJobStore();
        var handler = new LaneAwareHandler(blockedLane: null);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        var now = DateTimeOffset.UtcNow;

        var defaultJobId = await EnqueueAsync(jobs, "default", now);

        using var worker = new JobWorkerBackgroundService(
            jobs,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(5),
                Lanes = ["media"],
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            logger: NullLogger<JobWorkerBackgroundService>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        var current = await jobs.GetAsync("tenant-a", defaultJobId, TestContext.Current.CancellationToken);
        current!.Status.ShouldBe(JobStatus.Pending, "a worker scoped to 'media' must never lease a 'default' job");
    }

    private static async Task<Guid> EnqueueAsync(InMemoryJobStore jobs, string lane, DateTimeOffset now)
    {
        var id = Guid.NewGuid();

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = id,
                TenantId = "tenant-a",
                HandlerKey = JobHandlerKeys.AgentBatch,
                Lane = lane,
                TargetName = "lane-aware-handler",
                Status = JobStatus.Pending,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        return id;
    }

    [Fact]
    public async Task A_throwing_handlers_own_message_never_reaches_jobs_error_message()
    {
        const string ProviderSecret = "https://internal-provider.local:8443/v1?key=sk-abc123";

        var jobs = new InMemoryJobStore();
        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = jobId,
                TenantId = "tenant-a",
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "throwing-handler",
                Status = JobStatus.Pending,
                MaxAttempts = 1,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        using var host = TestJobHandlerHost.For(
            (JobHandlerKeys.AgentBatch, new ThrowingHandler(new HttpRequestException(ProviderSecret))));

        using var worker = new JobWorkerBackgroundService(
            jobs,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = TimeSpan.FromMilliseconds(5),
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            logger: loggerFactory.CreateLogger<JobWorkerBackgroundService>());

        await worker.StartAsync(TestContext.Current.CancellationToken);

        JobRecord? record = null;

        for (var attempt = 0; attempt < 100 && record?.Status is not JobStatus.Failed; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
            record = await jobs.GetAsync("tenant-a", jobId, TestContext.Current.CancellationToken);
        }

        await worker.StopAsync(TestContext.Current.CancellationToken);

        record.ShouldNotBeNull();
        record!.Status.ShouldBe(JobStatus.Failed);
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldContain(nameof(HttpRequestException));
        record.ErrorMessage.ShouldContain("(ref:");
        record.ErrorMessage.ShouldNotContain(ProviderSecret);

        // Phase 119, 119.3: the persisted safe text and the log entry that carries the
        // FULL exception detail must be found through the SAME correlation id.
        var correlationId = CorrelationIdIn(record.ErrorMessage);

        logs.Entries
            .Any(entry =>
                entry.Message.Contains(correlationId, StringComparison.Ordinal)
                && entry.Exception is not null
                && string.Equals(entry.Exception.Message, ProviderSecret, StringComparison.Ordinal))
            .ShouldBeTrue("no log entry carries both the same correlation id and the full exception detail.");
    }

    private static string CorrelationIdIn(string safeText)
    {
        var match = Regex.Match(safeText, @"\(ref: (?<id>[0-9a-f]+)\)", RegexOptions.None, TimeSpan.FromSeconds(1));

        match.Success.ShouldBeTrue($"'{safeText}' does not carry a '(ref: ...)' correlation id.");

        return match.Groups["id"].Value;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Message, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(string Message, Exception? Exception)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => entries.Enqueue((formatter(state, exception), exception));
        }
    }

    private sealed class ThrowingHandler(Exception exception) : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => throw exception;
    }

    private sealed class BlockingHandler : IJobHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }
    }

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }

    /// <summary>
    /// Blocks forever for a job in <see cref="_blockedLane"/> (until released);
    /// completes immediately for every other lane.
    /// </summary>
    private sealed class LaneAwareHandler(string? blockedLane) : IJobHandler
    {
        private readonly string? _blockedLane = blockedLane;
        private readonly TaskCompletionSource _mediaStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseMedia = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task MediaStarted => _mediaStarted.Task;

        public void ReleaseMedia() => _releaseMedia.TrySetResult();

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(context.Job.Lane, _blockedLane, StringComparison.Ordinal))
            {
                return;
            }

            _mediaStarted.TrySetResult();
            await _releaseMedia.Task.ConfigureAwait(false);
        }
    }
}
