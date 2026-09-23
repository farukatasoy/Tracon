using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Coordination;

/// <summary>
/// End-to-end scenario where two <see cref="SingletonGuard"/> instances share
/// the SAME lease store (Phase 42): only one holds the lease, and the other
/// takes over when the owner drops. The store's clock is a manual one
/// (Phase 184): the takeover test used to sleep 200 ms past a 20 ms lease.
/// </summary>
public sealed class SingletonLeaseTakeoverTests
{
    private const string LeaseName = "shared-lease";

    [Fact]
    public async Task Only_one_of_two_instances_holds_the_lease()
    {
        var store = new InMemorySingletonLeaseStore();
        var options = Options(new SingletonExecutionOptions { Enabled = true, LeaseDuration = TimeSpan.FromMinutes(5) });

        var guardA = new SingletonGuard(store, options, LeaseName);
        var guardB = new SingletonGuard(store, options, LeaseName);

        await guardA.TickAsync(CancellationToken.None);
        await guardB.TickAsync(CancellationToken.None);

        guardA.IsHeld.ShouldBeTrue();
        guardB.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Other_instance_takes_over_when_the_owner_stops()
    {
        var clock = new ManualTimeProvider();
        var store = new InMemorySingletonLeaseStore(clock);
        var lease = Options(new SingletonExecutionOptions { Enabled = true, LeaseDuration = TimeSpan.FromSeconds(30) });

        var guardA = new SingletonGuard(store, lease, LeaseName);
        var guardB = new SingletonGuard(store, lease, LeaseName);

        await guardA.TickAsync(CancellationToken.None);
        guardA.IsHeld.ShouldBeTrue();

        (await store.TryAcquireAsync(LeaseName, "someone-else", TimeSpan.FromMinutes(5))).ShouldBeFalse();

        // A stops (it never renews again). Move past the end of its lease.
        clock.Advance(TimeSpan.FromSeconds(31));

        await guardB.TickAsync(CancellationToken.None);

        guardB.IsHeld.ShouldBeTrue();
    }

    private static StaticOptionsMonitor<SingletonExecutionOptions> Options(SingletonExecutionOptions value) => new(value);
}
