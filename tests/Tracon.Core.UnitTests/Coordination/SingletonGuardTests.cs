using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Coordination;

/// <summary>
/// Verifies <see cref="SingletonGuard"/>'s lease/renew state transitions
/// Does not require a real timer: <c>TickAsync</c> is called
/// directly, so the state machine is tested independently of PeriodicTimer.
/// </summary>
public sealed class SingletonGuardTests
{
    private const string LeaseName = "test-lease";

    [Fact]
    public async Task No_query_reaches_the_store_while_disabled_and_IsHeld_is_always_true()
    {
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = false }), LeaseName);

        guard.IsHeld.ShouldBeTrue();

        await guard.RunAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
        store.AcquireCalls.ShouldBe(0);
        store.RenewCalls.ShouldBe(0);
        store.ReleaseCalls.ShouldBe(0);
    }

    [Fact]
    public async Task First_TickAsync_acquires_an_empty_lease()
    {
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        guard.IsHeld.ShouldBeFalse();

        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
        store.AcquireCalls.ShouldBe(1);
        store.RenewCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Held_lease_is_renewed_on_the_next_tick_not_reacquired()
    {
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        await guard.TickAsync(CancellationToken.None);
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
        store.AcquireCalls.ShouldBe(1);
        store.RenewCalls.ShouldBe(1);
    }

    [Fact]
    public async Task IsHeld_stays_false_when_someone_else_holds_it()
    {
        var store = new CountingSingletonLeaseStore { AcquireResult = false };
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task IsHeld_becomes_false_when_the_lease_is_lost_and_retries_on_the_next_tick()
    {
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        await guard.TickAsync(CancellationToken.None);
        guard.IsHeld.ShouldBeTrue();

        // 🚨 The lease moved to someone else: RenewAsync now returns false.
        store.RenewResult = false;
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeFalse();

        // On the next tick TryAcquireAsync is retried (not RenewAsync).
        store.RenewResult = true;
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
        store.AcquireCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Loop_does_not_die_when_the_store_throws_and_continues_on_the_next_tick()
    {
        var store = new CountingSingletonLeaseStore { ThrowOnAcquire = true };
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        await guard.TickAsync(CancellationToken.None);
        guard.IsHeld.ShouldBeFalse();

        store.ThrowOnAcquire = false;
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public async Task One_failed_renewal_does_not_give_the_lease_up()
    {
        var clock = new ManualTimeProvider();
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(Held(TimeSpan.FromSeconds(30))), LeaseName, timeProvider: clock);

        await guard.TickAsync(CancellationToken.None);

        store.ThrowOnRenew = true;
        clock.Advance(TimeSpan.FromSeconds(10));
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue("one renewal that failed is inside the margin the lease leaves");

        store.ThrowOnRenew = false;
        clock.Advance(TimeSpan.FromSeconds(10));
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public async Task Renewals_that_keep_failing_past_the_whole_lease_give_it_up()
    {
        var clock = new ManualTimeProvider();
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(Held(TimeSpan.FromSeconds(30))), LeaseName, timeProvider: clock);

        await guard.TickAsync(CancellationToken.None);
        guard.IsHeld.ShouldBeTrue();

        // The store cannot be reached from now on; the lease is the other instance's after 30 seconds.
        store.ThrowOnRenew = true;

        for (var tick = 0; tick < 3; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            await guard.TickAsync(CancellationToken.None);
        }

        guard.IsHeld.ShouldBeFalse("this instance went on acting as the executor on a lease it could no longer prove");
    }

    [Fact]
    public async Task A_renewal_that_never_returns_cannot_keep_the_lease_past_its_duration()
    {
        // The store call hangs, so no tick ends and none of the catch paths runs: the answer to
        // "do I hold it" must come from the clock, not from the loop.
        var clock = new ManualTimeProvider();
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(Held(TimeSpan.FromSeconds(30))), LeaseName, timeProvider: clock);

        await guard.TickAsync(CancellationToken.None);
        guard.IsHeld.ShouldBeTrue();

        clock.Advance(TimeSpan.FromSeconds(29));
        guard.IsHeld.ShouldBeTrue();

        clock.Advance(TimeSpan.FromSeconds(2));
        guard.IsHeld.ShouldBeFalse();
    }

    private static SingletonExecutionOptions Held(TimeSpan leaseDuration)
        => new() { Enabled = true, LeaseDuration = leaseDuration };

    private static StaticOptionsMonitor<SingletonExecutionOptions> Options(SingletonExecutionOptions value) => new(value);

    /// <summary>Fake lease store that tracks call counts and has configurable behavior.</summary>
    private sealed class CountingSingletonLeaseStore : ISingletonLeaseStore
    {
        public int AcquireCalls { get; private set; }

        public int RenewCalls { get; private set; }

        public int ReleaseCalls { get; private set; }

        public bool AcquireResult { get; set; } = true;

        public bool RenewResult { get; set; } = true;

        public bool ThrowOnAcquire { get; set; }

        public bool ThrowOnRenew { get; set; }

        public ValueTask<bool> TryAcquireAsync(
            string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            AcquireCalls++;

            if (ThrowOnAcquire)
            {
                throw new InvalidOperationException("Test: intentional failure.");
            }

            return new ValueTask<bool>(AcquireResult);
        }

        public ValueTask<bool> RenewAsync(
            string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            RenewCalls++;

            if (ThrowOnRenew)
            {
                throw new InvalidOperationException("Test: intentional failure.");
            }

            return new ValueTask<bool>(RenewResult);
        }

        public ValueTask ReleaseAsync(string name, string ownerId, CancellationToken cancellationToken = default)
        {
            ReleaseCalls++;

            return default;
        }
    }
}
