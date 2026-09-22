
namespace Tracon.Testing.Contracts.Storage;

/// <summary>Behavior tests for the <see cref="IJobScheduleStore"/> contract.</summary>
public abstract class JobScheduleStoreContract : TenantIsolationContract<IJobScheduleStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        await Store.SaveAsync(TestData.Schedule(tenantId, name), cancellationToken);
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListAsync(tenantId, cancellationToken)).Count;

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

    /// <summary>
    /// A schedule saved with no payload round-trips through every provider.
    /// </summary>
    /// <remarks>
    /// An unset payload used to be written as the literal
    /// <c>null</c>, which SQL Server's <c>CHECK (ISJSON(payload) = 1)</c>
    /// rejects, while Postgres and SQLite accepted it - a provider divergence
    /// no in-memory test could see. The empty array satisfies all three, and
    /// the read-back has to agree with what was written.
    /// </remarks>
    [Fact]
    public async Task SaveAsync_round_trips_a_schedule_that_carries_no_payload()
    {
        var saved = await Store.SaveAsync(TestData.Schedule() with { Payload = default });

        saved.Payload.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
        saved.Payload.GetArrayLength().ShouldBe(0);

        var fetched = await Store.GetAsync("default", "night-report");
        fetched!.Payload.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
        fetched.Payload.GetArrayLength().ShouldBe(0);
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
        var now = PrecisionSafeUtcNow();

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
        var now = PrecisionSafeUtcNow();
        var saved = await Store.SaveAsync(TestData.Schedule() with { NextRunAt = now });

        var claimed = await Store.TryClaimNextRunAsync(saved.Id, now.AddMinutes(-1), now.AddHours(1), now);

        claimed.ShouldBeFalse();
        (await Store.GetAsync("default", "night-report"))!.NextRunAt.ShouldBe(now);
    }

    [Fact]
    public async Task TryClaimNextRunAsync_a_second_claim_prevents_the_conflict()
    {
        var now = PrecisionSafeUtcNow();
        var saved = await Store.SaveAsync(TestData.Schedule() with { NextRunAt = now });

        var first = await Store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(1), now);
        var second = await Store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(2), now);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    private static DateTimeOffset PrecisionSafeUtcNow()
        => DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
