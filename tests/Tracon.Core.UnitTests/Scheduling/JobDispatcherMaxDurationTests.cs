using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// <see cref="IJobDispatcher"/> carries a job's own time limit to the store and refuses one
/// a timer cannot arm (F-323, the F-280 rule): the worker arms it as the attempt's
/// deadline, long after the call that queued the job returned.
/// </summary>
public sealed class JobDispatcherMaxDurationTests
{
    [Fact]
    public async Task A_valid_limit_reaches_the_stored_job()
    {
        var store = new InMemoryJobStore();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, new NoopHandler()));
        var dispatcher = Dispatcher(store, host);

        var queued = await dispatcher.EnqueueAsync(Request(TimeSpan.FromMinutes(30)), TestContext.Current.CancellationToken);
        var stored = await store.GetAsync("tenant-a", queued.Id, TestContext.Current.CancellationToken);

        queued.MaxDuration.ShouldBe(TimeSpan.FromMinutes(30));
        stored!.MaxDuration.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task No_limit_stays_no_limit()
    {
        var store = new InMemoryJobStore();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, new NoopHandler()));

        var queued = await Dispatcher(store, host).EnqueueAsync(Request(maxDuration: null), TestContext.Current.CancellationToken);

        queued.MaxDuration.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    [InlineData(4_294_967_295L)]
    public async Task A_limit_a_timer_cannot_arm_is_refused_before_anything_is_queued(long milliseconds)
    {
        var store = new InMemoryJobStore();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, new NoopHandler()));

        var failure = await Should.ThrowAsync<ArgumentException>(async () =>
            await Dispatcher(store, host).EnqueueAsync(Request(TimeSpan.FromMilliseconds(milliseconds)), TestContext.Current.CancellationToken));

        failure.Message.ShouldContain(nameof(JobRequest.MaxDuration));
        (await store.QueryAsync(new JobQuery { TenantId = "tenant-a" }, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    private static JobDispatcher Dispatcher(IJobStore store, TestJobHandlerHost host)
        => new(store, host.Registry, new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions()));

    private static JobRequest Request(TimeSpan? maxDuration)
        => new()
        {
            TenantId = "tenant-a",
            HandlerKey = JobHandlerKeys.AgentBatch,
            TargetName = "agent",
            MaxDuration = maxDuration,
        };

    private sealed class NoopHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
