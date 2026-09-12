namespace Tracon.Core.UnitTests.Scheduling;

public sealed class InMemoryJobScheduleStoreTests
{
    [Fact]
    public async Task SaveAsync_assigns_an_id_to_a_new_schedule()
    {
        var store = new InMemoryJobScheduleStore();

        var saved = await store.SaveAsync(NewSchedule());

        saved.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task SaveAsync_preserves_the_id_for_the_same_name()
    {
        var store = new InMemoryJobScheduleStore();

        var first = await store.SaveAsync(NewSchedule());
        var second = await store.SaveAsync(NewSchedule() with { TargetName = "new-target" });

        second.Id.ShouldBe(first.Id);

        var fetched = await store.GetAsync("tenant", "night-report");
        fetched!.TargetName.ShouldBe("new-target");
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_a_schedule_that_does_not_exist()
    {
        var store = new InMemoryJobScheduleStore();

        (await store.DeleteAsync("tenant", "missing")).ShouldBeFalse();
    }

    [Fact]
    public async Task ListAsync_returns_only_that_tenant()
    {
        var store = new InMemoryJobScheduleStore();
        await store.SaveAsync(NewSchedule() with { TenantId = "a", Name = "s1" });
        await store.SaveAsync(NewSchedule() with { TenantId = "b", Name = "s2" });

        var list = await store.ListAsync("a");

        list.ShouldHaveSingleItem();
        list[0].Name.ShouldBe("s1");
    }

    [Fact]
    public async Task ListDueAsync_returns_only_enabled_cron_schedules_that_are_due()
    {
        var store = new InMemoryJobScheduleStore();
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(NewSchedule() with { Name = "due", NextRunAt = now.AddMinutes(-1) });
        await store.SaveAsync(NewSchedule() with { Name = "not-due", NextRunAt = now.AddMinutes(1) });
        await store.SaveAsync(NewSchedule() with { Name = "disabled", NextRunAt = now.AddMinutes(-1), Enabled = false });
        await store.SaveAsync(NewSchedule() with { Name = "manual-only", Cron = null, NextRunAt = null });

        var due = await store.ListDueAsync(now);

        due.ShouldHaveSingleItem();
        due[0].Name.ShouldBe("due");
    }

    [Fact]
    public async Task TryClaimNextRunAsync_fails_when_the_expected_value_does_not_match()
    {
        var store = new InMemoryJobScheduleStore();
        var now = DateTimeOffset.UtcNow;
        var saved = await store.SaveAsync(NewSchedule() with { NextRunAt = now });

        // As if another instance already advanced it: the expected value is no longer valid.
        var claimed = await store.TryClaimNextRunAsync(saved.Id, now.AddMinutes(-1), now.AddHours(1), now);

        claimed.ShouldBeFalse();

        var current = await store.GetAsync("tenant", "night-report");
        current!.NextRunAt.ShouldBe(now);
    }

    [Fact]
    public async Task TryClaimNextRunAsync_advances_the_next_run_on_success()
    {
        var store = new InMemoryJobScheduleStore();
        var now = DateTimeOffset.UtcNow;
        var saved = await store.SaveAsync(NewSchedule() with { NextRunAt = now });

        var claimed = await store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(1), now);

        claimed.ShouldBeTrue();

        var current = await store.GetAsync("tenant", "night-report");
        current!.NextRunAt.ShouldBe(now.AddDays(1));
        current.LastRunAt.ShouldBe(now);
    }

    [Fact]
    public async Task TryClaimNextRunAsync_prevents_a_conflicting_second_claim()
    {
        // Two concurrent "workers" try to trigger the same schedule; only the
        // first must succeed. This is not proof of true concurrency in the
        // in-memory store (see the real Postgres test in JobStoreContract), but
        // it does verify the CAS logic.
        var store = new InMemoryJobScheduleStore();
        var now = DateTimeOffset.UtcNow;
        var saved = await store.SaveAsync(NewSchedule() with { NextRunAt = now });

        var first = await store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(1), now);
        var second = await store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(2), now);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    private static JobSchedule NewSchedule()
        => new()
        {
            TenantId = "tenant",
            Name = "night-report",
            HandlerKey = JobHandlerKeys.AgentBatch,
            TargetName = "summarizer",
            Cron = "0 3 * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
}
