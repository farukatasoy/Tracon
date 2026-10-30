namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The per-tenant gate inside one process.
/// </summary>
/// <remarks>
/// The limiter is the part of the tenant limit a run waits on, so the cases are about what it does with
/// waiters and with the table it keeps: a canceled waiter gives nothing back, a slot given back twice is
/// given back once, and a tenant with no run left leaves no entry behind.
/// </remarks>
public sealed class TenantSlotLimiterTests
{
    [Fact]
    public async Task A_tenant_gets_its_limit_of_slots_and_the_next_run_waits()
    {
        var limiter = new TenantSlotLimiter(2);

        using var first = await limiter.AcquireAsync("acme", TestContext.Current.CancellationToken);
        using var second = await limiter.AcquireAsync("acme", TestContext.Current.CancellationToken);

        limiter.IsFull("acme").ShouldBeTrue();

        var third = limiter.AcquireAsync("acme", TestContext.Current.CancellationToken).AsTask();

        third.IsCompleted.ShouldBeFalse();

        first.Dispose();

        using var slot = await third.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task One_tenant_at_its_limit_does_not_fill_another_tenants_slots()
    {
        var limiter = new TenantSlotLimiter(1);

        using var acme = await limiter.AcquireAsync("acme", TestContext.Current.CancellationToken);

        limiter.IsFull("acme").ShouldBeTrue();
        limiter.IsFull("globex").ShouldBeFalse();

        using var globex = await limiter.AcquireAsync("globex", TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_waiter_that_is_canceled_leaves_the_slot_with_the_run_that_holds_it()
    {
        var limiter = new TenantSlotLimiter(1);

        using var holder = await limiter.AcquireAsync("acme", TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource();

        var waiting = limiter.AcquireAsync("acme", cancel.Token).AsTask();
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await waiting);

        // The canceled run took nothing and gave nothing back: the tenant is still at its limit.
        limiter.IsFull("acme").ShouldBeTrue();

        var next = limiter.AcquireAsync("acme", TestContext.Current.CancellationToken).AsTask();

        next.IsCompleted.ShouldBeFalse();

        holder.Dispose();

        using var slot = await next.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_slot_given_back_twice_is_given_back_once()
    {
        var limiter = new TenantSlotLimiter(1);

        var slot = await limiter.AcquireAsync("acme", TestContext.Current.CancellationToken);

        slot.Dispose();
        slot.Dispose();

        using var first = await limiter.AcquireAsync("acme", TestContext.Current.CancellationToken);

        // A second Dispose that released again would have opened a second slot for a limit of one.
        limiter.IsFull("acme").ShouldBeTrue();
    }

    [Fact]
    public async Task A_tenant_with_no_run_left_leaves_no_entry()
    {
        var limiter = new TenantSlotLimiter(2);

        for (var tenant = 0; tenant < 50; tenant++)
        {
            using var slot = await limiter.AcquireAsync($"tenant-{tenant}", TestContext.Current.CancellationToken);
        }

        limiter.TrackedTenants.ShouldBe(0);

        // A canceled waiter leaves none either.
        using var holder = await limiter.AcquireAsync("busy", TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await limiter.AcquireAsync("gone", cancel.Token));

        limiter.TrackedTenants.ShouldBe(1);
    }

    [Fact]
    public async Task A_tenant_slot_taken_again_after_the_entry_was_dropped_still_limits()
    {
        var limiter = new TenantSlotLimiter(1);

        (await limiter.AcquireAsync("acme", TestContext.Current.CancellationToken)).Dispose();
        limiter.TrackedTenants.ShouldBe(0);

        using var again = await limiter.AcquireAsync("acme", TestContext.Current.CancellationToken);

        limiter.IsFull("acme").ShouldBeTrue();
    }
}
