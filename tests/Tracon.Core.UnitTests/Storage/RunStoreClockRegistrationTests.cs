using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// The clock a host registers with the container is the clock the in-memory run store ages a heartbeat with.
/// </summary>
/// <remarks>
/// <para>
/// The heartbeat writer marks a run with the registered clock, and the store decides whether the mark is old with its own
/// clock. A store that read the wall clock while the writer read a registered one (a fake clock in a test host, a clock a
/// consumer controls) closed every run that was beating as orphaned on the first scan, because the mark and the age were
/// read from two different times. The check goes through the registration a host uses.
/// </para>
/// </remarks>
public sealed class RunStoreClockRegistrationTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task A_heartbeat_marked_with_the_registered_clock_is_aged_by_that_clock()
    {
        var clock = new ManualClock();
        await using var provider = Build(clock);
        var runs = provider.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(Start(runId, clock), TestContext.Current.CancellationToken);
        await runs.TouchHeartbeatAsync([runId], clock.GetUtcNow(), TestContext.Current.CancellationToken);

        // The mark is fresh on the clock it was written with; the wall clock is weeks past it.
        (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a heartbeat marked a moment ago on the registered clock is not old");

        clock.Advance(Threshold + TimeSpan.FromMinutes(1));

        var claimed = (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        claimed.Id.ShouldBe(runId);
        claimed.CompletedAt.ShouldBe(clock.GetUtcNow(), "the run is closed at the registered clock's time, which is after its start");
    }

    [Fact]
    public async Task A_run_that_never_beat_is_aged_from_its_start_on_the_registered_clock()
    {
        var clock = new ManualClock();
        await using var provider = Build(clock);
        var runs = provider.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(Start(runId, clock), TestContext.Current.CancellationToken);

        (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a run that started a moment ago on the registered clock is not old");

        clock.Advance(Threshold + TimeSpan.FromMinutes(1));

        (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Id.ShouldBe(runId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public async Task A_threshold_of_zero_or_less_counts_a_run_that_was_marked_a_moment_ago(int staleAfterSeconds)
    {
        var clock = new ManualClock();
        await using var provider = Build(clock);
        var runs = provider.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(Start(runId, clock), TestContext.Current.CancellationToken);
        await runs.TouchHeartbeatAsync([runId], clock.GetUtcNow(), TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromMilliseconds(1));

        (await runs.ClaimOrphanedRunsAsync(TimeSpan.FromSeconds(staleAfterSeconds), 10, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Id.ShouldBe(runId);
    }

    private static RunStartInfo Start(Guid runId, TimeProvider clock)
        => new() { RunId = runId, AgentName = "clock-agent", StartedAt = clock.GetUtcNow() };

    private static ServiceProvider Build(TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddTracon();

        return services.BuildServiceProvider();
    }
}
