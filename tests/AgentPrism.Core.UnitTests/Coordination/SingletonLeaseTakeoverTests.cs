using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Coordination;

/// <summary>
/// End-to-end scenario where two <see cref="SingletonGuard"/> instances share
/// the SAME lease store (Phase 42): only one holds the lease, and the other
/// takes over when the owner drops. Uses real time (short lease + short wait)
/// — same rationale as <c>JobStoreContract</c>'s lease-expiry test.
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
        var store = new InMemorySingletonLeaseStore();
        var shortLease = Options(new SingletonExecutionOptions { Enabled = true, LeaseDuration = TimeSpan.FromMilliseconds(20) });

        var guardA = new SingletonGuard(store, shortLease, LeaseName);
        var guardB = new SingletonGuard(store, shortLease, LeaseName);

        await guardA.TickAsync(CancellationToken.None);
        guardA.IsHeld.ShouldBeTrue();

        (await store.TryAcquireAsync(LeaseName, "someone-else", TimeSpan.FromMinutes(5))).ShouldBeFalse();

        // A stops (it never renews again). Wait until the lease expires.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        await guardB.TickAsync(CancellationToken.None);

        guardB.IsHeld.ShouldBeTrue();
    }

    private static StaticOptionsMonitor<SingletonExecutionOptions> Options(SingletonExecutionOptions value) => new(value);
}
