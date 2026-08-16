
namespace AgentPrism.StoreContracts;

/// <summary>Behavior tests for the <see cref="IJobScheduleStore"/> contract.</summary>
public abstract class JobScheduleStoreContract : TenantIsolationContract<IJobScheduleStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.SaveAsync(TestData.Schedule(tenantId, name));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    [Fact]
    public async Task SaveAsync_assigns_an_id_to_a_new_schedule()
    {
        var saved = await Store.SaveAsync(TestData.Schedule());

        saved.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task SaveAsync_keeps_the_id_and_updates_for_the_same_name()
    {
        var first = await Store.SaveAsync(TestData.Schedule());
        var second = await Store.SaveAsync(TestData.Schedule() with { TargetName = "new-target" });

        second.Id.ShouldBe(first.Id);

        var fetched = await Store.GetAsync("default", "night-report");
        fetched!.TargetName.ShouldBe("new-target");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_another_tenant()
    {
        await Store.SaveAsync(TestData.Schedule(tenantId: "tenant-a"));

        (await Store.GetAsync("tenant-b", "night-report")).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_removes_an_existing_schedule()
    {
        await Store.SaveAsync(TestData.Schedule());

        (await Store.DeleteAsync("default", "night-report")).ShouldBeTrue();
        (await Store.GetAsync("default", "night-report")).ShouldBeNull();
    }

    [Fact]
    public async Task ListAsync_returns_only_that_tenant()
    {
        await Store.SaveAsync(TestData.Schedule(tenantId: "tenant-a", name: "s1"));
        await Store.SaveAsync(TestData.Schedule(tenantId: "tenant-b", name: "s2"));

        var list = await Store.ListAsync("tenant-a");

        list.ShouldHaveSingleItem();
        list[0].Name.ShouldBe("s1");
    }

    [Fact]
    public async Task ListDueAsync_returns_only_enabled_and_due_schedules()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.SaveAsync(TestData.Schedule(name: "due") with { NextRunAt = now.AddMinutes(-1) });
        await Store.SaveAsync(TestData.Schedule(name: "not-due") with { NextRunAt = now.AddMinutes(5) });
        await Store.SaveAsync(TestData.Schedule(name: "disabled") with { NextRunAt = now.AddMinutes(-1), Enabled = false });

        var due = await Store.ListDueAsync(now);

        due.ShouldHaveSingleItem();
        due[0].Name.ShouldBe("due");
    }

    [Fact]
    public async Task TryClaimNextRunAsync_fails_when_the_expected_value_does_not_match()
    {
        var now = DateTimeOffset.UtcNow;
        var saved = await Store.SaveAsync(TestData.Schedule() with { NextRunAt = now });

        var claimed = await Store.TryClaimNextRunAsync(saved.Id, now.AddMinutes(-1), now.AddHours(1), now);

        claimed.ShouldBeFalse();
        (await Store.GetAsync("default", "night-report"))!.NextRunAt.ShouldBe(now);
    }

    [Fact]
    public async Task TryClaimNextRunAsync_a_second_claim_prevents_the_conflict()
    {
        var now = DateTimeOffset.UtcNow;
        var saved = await Store.SaveAsync(TestData.Schedule() with { NextRunAt = now });

        var first = await Store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(1), now);
        var second = await Store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(2), now);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }
}
