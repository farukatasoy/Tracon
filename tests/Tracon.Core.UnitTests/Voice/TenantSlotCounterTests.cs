namespace Tracon.Core.UnitTests.Voice;

/// <summary>
/// The per-tenant slot counter behind the voice connection limit and the live session registry keeps an entry
/// only while its tenant holds a slot.
/// </summary>
/// <remarks>
/// The tenant identifier is taken from a request. A tenant that reserved a slot and gave it back used to leave a
/// zero in the map for good, so an installation that accepts any identifier let a caller grow the map by one entry
/// for every identifier it made up.
/// </remarks>
public sealed class TenantSlotCounterTests
{
    [Fact]
    public void A_tenant_that_gave_every_slot_back_leaves_no_entry_whatever_the_number_of_tenants()
    {
        var counter = new TenantSlotCounter();

        for (var tenant = 0; tenant < 10_000; tenant++)
        {
            counter.TryReserve($"tenant-{tenant}", limit: 2).ShouldBeTrue();
            counter.TryReserve($"tenant-{tenant}", limit: 2).ShouldBeTrue();
            counter.Release($"tenant-{tenant}");
            counter.Release($"tenant-{tenant}");
        }

        counter.TrackedTenants.ShouldBe(0);
    }

    [Fact]
    public void A_tenant_that_still_holds_a_slot_keeps_its_entry_and_its_count()
    {
        var counter = new TenantSlotCounter();

        counter.TryReserve("tenant-a", limit: 3).ShouldBeTrue();
        counter.TryReserve("tenant-a", limit: 3).ShouldBeTrue();
        counter.Release("tenant-a");

        counter.CountFor("tenant-a").ShouldBe(1);
        counter.TrackedTenants.ShouldBe(1);
    }

    [Fact]
    public void A_reservation_over_the_limit_is_refused_and_a_limit_under_one_leaves_no_entry()
    {
        var counter = new TenantSlotCounter();

        counter.TryReserve("tenant-a", limit: 1).ShouldBeTrue();
        counter.TryReserve("tenant-a", limit: 1).ShouldBeFalse();

        counter.TryReserve("tenant-b", limit: 0).ShouldBeFalse();
        counter.TryReserve("tenant-c", limit: -4).ShouldBeFalse();

        counter.TrackedTenants.ShouldBe(1, "only tenant-a holds a slot");
    }

    [Fact]
    public void Giving_back_a_slot_that_was_never_taken_changes_nothing()
    {
        var counter = new TenantSlotCounter();

        counter.Release("tenant-a");
        counter.Release("tenant-a");

        counter.TrackedTenants.ShouldBe(0);
        counter.CountFor("tenant-a").ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_reservations_and_releases_never_pass_the_limit_and_leave_nothing_behind()
    {
        const int limit = 4;
        var counter = new TenantSlotCounter();
        var passed = 0;
        var held = 0;

        // Few tenants and many threads: the same entry is removed and added again all the time, which is the
        // race a remove-at-zero has to survive.
        var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            var random = new Random(worker);

            for (var step = 0; step < 50_000; step++)
            {
                var tenant = $"tenant-{random.Next(3)}";

                if (!counter.TryReserve(tenant, limit))
                {
                    continue;
                }

                var count = counter.CountFor(tenant);

                if (count is < 1 or > limit)
                {
                    Interlocked.Increment(ref passed);
                }

                Interlocked.Increment(ref held);
                counter.Release(tenant);
            }
        })).ToArray();

        await Task.WhenAll(workers);

        passed.ShouldBe(0, "a tenant never held more than the limit, and never less than the slot just reserved");
        held.ShouldBeGreaterThan(0);
        counter.TrackedTenants.ShouldBe(0);
    }
}
