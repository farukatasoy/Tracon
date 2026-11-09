using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Coordination;

/// <summary>
/// What two single-executor guards believe when the clock the lease STORE reads is stepped forward past the lease while the
/// owner's own monotonic clock did not move.
/// </summary>
/// <remarks>
/// A lease is a span of time, and the in-memory store ends it on a monotonic reading: a wall clock that steps an hour
/// ahead (as a node whose clock runs an hour fast would read it) does not make a live lease look over, so the other guard
/// cannot take it and two executors never believe they hold it. PostgreSQL and SQL Server read the database server's clock
/// inside the lease statements, so a node's wall clock never reaches their leases; the clock-skew contract has those
/// scenarios.
/// </remarks>
public sealed class LeaseStealUnderClockStepTests
{
    [Fact]
    public async Task A_wall_clock_stepped_past_the_lease_does_not_let_a_second_guard_take_it_while_the_owner_still_holds_it()
    {
        var ownerClock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var storeClock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemorySingletonLeaseStore(storeClock);
        var options = new StaticOptionsMonitor<SingletonExecutionOptions>(new SingletonExecutionOptions
        {
            Enabled = true,
            LeaseDuration = TimeSpan.FromSeconds(30),
        });

        var owner = new SingletonGuard(store, options, "probe", timeProvider: ownerClock);
        var other = new SingletonGuard(store, options, "probe", timeProvider: new ManualClock());

        await owner.TickAsync(TestContext.Current.CancellationToken);
        await other.TickAsync(TestContext.Current.CancellationToken);

        owner.IsHeld.ShouldBeTrue();
        other.IsHeld.ShouldBeFalse("the lease is live on every clock");

        // The clock the store reads goes an hour ahead (as a node whose clock runs an hour fast would read it).
        storeClock.StepWallClock(TimeSpan.FromHours(1));
        await other.TickAsync(TestContext.Current.CancellationToken);

        owner.IsHeld.ShouldBeTrue("the owner still holds the lease it took a moment ago");
        other.IsHeld.ShouldBeFalse("the lease did not end: a wall clock step does not end a lease");
    }
}
