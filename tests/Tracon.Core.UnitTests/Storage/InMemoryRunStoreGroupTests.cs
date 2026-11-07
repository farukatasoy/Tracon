namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// What the in-memory store asks of a group of events beyond what <c>RunStoreContract</c> checks for every store: the group
/// is one run's, and a group the store refuses leaves nothing of itself behind.
/// </summary>
public sealed class InMemoryRunStoreGroupTests
{
    private static RunEvent Event(Guid runId, long sequence, string? tenantId = null)
        => new() { RunId = runId, Sequence = sequence, Type = RunEventType.MessageDelta, Timestamp = DateTimeOffset.UtcNow, Text = $"chunk {sequence}", TenantId = tenantId };

    [Fact]
    public async Task A_group_of_two_runs_is_a_caller_error_and_writes_nothing()
    {
        var store = new InMemoryRunStore();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await store.StartRunAsync(new RunStartInfo { RunId = first, AgentName = "agent", StartedAt = DateTimeOffset.UtcNow });
        await store.StartRunAsync(new RunStartInfo { RunId = second, AgentName = "agent", StartedAt = DateTimeOffset.UtcNow });

        await Should.ThrowAsync<ArgumentException>(async () => await store.AppendEventsAsync([Event(first, 0), Event(second, 0)]));

        (await store.GetLastEventSequenceAsync(first)).ShouldBeNull();
        (await store.GetLastEventSequenceAsync(second)).ShouldBeNull();
    }

    [Fact]
    public async Task A_group_of_two_tenants_is_a_caller_error()
    {
        var store = new InMemoryRunStore();
        var runId = Guid.NewGuid();

        await store.StartRunAsync(new RunStartInfo { RunId = runId, AgentName = "agent", StartedAt = DateTimeOffset.UtcNow });

        await Should.ThrowAsync<ArgumentException>(
            async () => await store.AppendEventsAsync([Event(runId, 0, "tenant-a"), Event(runId, 1, "tenant-b")]));

        (await store.GetLastEventSequenceAsync(runId)).ShouldBeNull();
    }

    [Fact]
    public async Task A_cancelled_token_stops_a_group_before_it_is_written()
    {
        var store = new InMemoryRunStore();
        var runId = Guid.NewGuid();

        await store.StartRunAsync(new RunStartInfo { RunId = runId, AgentName = "agent", StartedAt = DateTimeOffset.UtcNow });

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await store.AppendEventsAsync([Event(runId, 0)], cancelled.Token));

        (await store.GetLastEventSequenceAsync(runId)).ShouldBeNull();
    }
}
