using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// What a group of events does that a single event does not: it travels as one JSON document, and the store checks that
/// the events are one run's.
/// </summary>
public sealed class RunEventBatchTests(SqliteFixture fixture)
{
    [Fact]
    public async Task A_text_that_holds_half_of_a_surrogate_pair_costs_the_group_nothing_but_that_character()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var runId = Guid.NewGuid();

        await context.Runs.StartRunAsync(TestData.Run(runId));

        // A model or a tool can hand over text that is not valid UTF-16. The JSON writer refuses such text, and one
        // refused text would otherwise cost every event beside it: the store writes the half as U+FFFD.
        await context.Runs.AppendEventsAsync(
        [
            TestData.Event(runId, 0) with { Text = "before" },
            TestData.Event(runId, 1) with { Text = "high \uD800 alone", Payload = "low \uDC00 alone" },
            TestData.Event(runId, 2) with { Text = "pair 😀 whole" },
            TestData.Event(runId, 3) with { Text = "after" },
        ]);

        var events = await context.Runs.ReadEventsAsync(runId).ToListAsync();

        events.Select(static e => e.Text).ShouldBe(["before", "high � alone", "pair 😀 whole", "after"], StringComparer.Ordinal);
        events[1].Payload.ShouldBe("low � alone");
    }

    [Fact]
    public async Task A_group_of_two_runs_or_two_tenants_is_a_caller_error_and_writes_nothing()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await context.Runs.StartRunAsync(TestData.Run(first));
        await context.Runs.StartRunAsync(TestData.Run(second));

        await Should.ThrowAsync<ArgumentException>(
            async () => await context.Runs.AppendEventsAsync([TestData.Event(first, 0), TestData.Event(second, 0)]));

        await Should.ThrowAsync<ArgumentException>(
            async () => await context.Runs.AppendEventsAsync(
            [
                TestData.Event(first, 0) with { TenantId = "tenant-a" },
                TestData.Event(first, 1) with { TenantId = "tenant-b" },
            ]));

        (await context.Runs.ReadEventsAsync(first).ToListAsync()).ShouldBeEmpty();
        (await context.Runs.ReadEventsAsync(second).ToListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancelled_token_stops_a_group_before_it_is_written()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var runId = Guid.NewGuid();

        await context.Runs.StartRunAsync(TestData.Run(runId));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await context.Runs.AppendEventsAsync([TestData.Event(runId, 0), TestData.Event(runId, 1)], cancelled.Token));

        (await context.Runs.ReadEventsAsync(runId).ToListAsync()).ShouldBeEmpty();
    }
}
