namespace Tracon.Core.UnitTests.Skills;

/// <summary>The limiter holds a script slot for the tenant and the installation, and holds nothing for a tenant that is gone.</summary>
/// <remarks>
/// A tenant identifier is whatever the caller's tenancy settings let through, so a limiter that keeps one entry for
/// every identifier it has ever seen grows with the number of identifiers and never gives the memory back.
/// </remarks>
public sealed class SkillScriptConcurrencyLimiterTests
{
    private static SkillScriptConcurrencyLimiter Limiter(int perTenant, int total)
        => new(new TraconSkillScriptOptions { MaxConcurrentPerTenant = perTenant, MaxConcurrentTotal = total });

    [Fact]
    public async Task A_tenant_at_its_limit_waits_while_another_tenant_does_not()
    {
        using var limiter = Limiter(perTenant: 1, total: 10);

        using var first = await limiter.AcquireAsync("tenant-a", TestContext.Current.CancellationToken);

        var second = limiter.AcquireAsync("tenant-a", TestContext.Current.CancellationToken).AsTask();
        using var other = await limiter.AcquireAsync("tenant-b", TestContext.Current.CancellationToken);

        second.IsCompleted.ShouldBeFalse("a tenant at its limit waits for a slot");

        first.Dispose();

        using var granted = await second.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_installation_limit_holds_across_tenants()
    {
        using var limiter = Limiter(perTenant: 5, total: 1);

        using var first = await limiter.AcquireAsync("tenant-a", TestContext.Current.CancellationToken);

        var second = limiter.AcquireAsync("tenant-b", TestContext.Current.CancellationToken).AsTask();

        second.IsCompleted.ShouldBeFalse("the installation is at its limit");

        first.Dispose();

        using var granted = await second.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_tenant_that_has_released_everything_is_not_tracked()
    {
        using var limiter = Limiter(perTenant: 2, total: 10);

        for (var i = 0; i < 1000; i++)
        {
            using var lease = await limiter.AcquireAsync($"tenant-{i}", TestContext.Current.CancellationToken);
        }

        limiter.TrackedTenantCount.ShouldBe(0, "an entry per tenant ever seen grows with the number of identifiers");
    }

    [Fact]
    public async Task A_wait_that_is_cancelled_leaves_no_entry_and_no_missing_slot()
    {
        using var limiter = Limiter(perTenant: 1, total: 10);
        using var held = await limiter.AcquireAsync("tenant-a", TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource();

        var waiting = limiter.AcquireAsync("tenant-a", cancel.Token).AsTask();
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await waiting);

        held.Dispose();

        limiter.TrackedTenantCount.ShouldBe(0);

        // The slot the cancelled waiter never got is not lost: the tenant can still take its one slot.
        using var again = await limiter.AcquireAsync("tenant-a", TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Disposing_a_lease_twice_frees_one_slot()
    {
        using var limiter = Limiter(perTenant: 1, total: 10);
        var lease = await limiter.AcquireAsync("tenant-a", TestContext.Current.CancellationToken);

        lease.Dispose();
        lease.Dispose();

        using var first = await limiter.AcquireAsync("tenant-a", TestContext.Current.CancellationToken);
        var second = limiter.AcquireAsync("tenant-a", TestContext.Current.CancellationToken).AsTask();

        second.IsCompleted.ShouldBeFalse("a double dispose must not hand out a second slot");

        first.Dispose();
        using var granted = await second.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
    }
}
