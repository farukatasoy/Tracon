using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Scheduling;

/// <summary>Shutdown tests for <see cref="JobWorkerBackgroundService"/>.</summary>
public sealed class JobWorkerBackgroundServiceTests
{
    [Fact]
    public async Task StopAsync_waits_for_a_leased_job_to_release_its_concurrency_slot()
    {
        var jobs = new InMemoryJobStore();
        var handler = new BlockingHandler();
        var now = DateTimeOffset.UtcNow;

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-a",
                Kind = JobKind.AgentBatch,
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
            [handler],
            new StaticOptionsMonitor<AgentPrismSchedulingOptions>(new AgentPrismSchedulingOptions
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
                Kind = JobKind.AgentBatch,
                TargetName = "throwing-handler",
                Status = JobStatus.Pending,
                MaxAttempts = 1,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        using var worker = new JobWorkerBackgroundService(
            jobs,
            new InMemoryJobScheduleStore(),
            [new ThrowingHandler(new HttpRequestException(ProviderSecret))],
            new StaticOptionsMonitor<AgentPrismSchedulingOptions>(new AgentPrismSchedulingOptions
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
        public JobKind Kind => JobKind.AgentBatch;

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => throw exception;
    }

    private sealed class BlockingHandler : IJobHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public JobKind Kind => JobKind.AgentBatch;

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }
    }

    private sealed class NotDraining : IAgentPrismDrainState
    {
        public bool IsDraining => false;
    }
}
