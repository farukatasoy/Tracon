using AgentPrism.Core.UnitTests.Fakes;
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
