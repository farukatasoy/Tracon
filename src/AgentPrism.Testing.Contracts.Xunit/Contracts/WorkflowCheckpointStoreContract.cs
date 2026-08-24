using System.Text.Json;

namespace AgentPrism.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IWorkflowCheckpointStore"/> contract.
/// </summary>
/// <remarks>
/// The most critical scenario is reading back a <em>polymorphic payload
/// without corruption</em>: the Microsoft Agent Framework checkpoint JSON
/// carries a <c>$type</c> discriminator that must be the first property of
/// the object it appears in. A store that reorders object keys on write can
/// silently take the discriminator out of first place, so a compliant
/// implementation must preserve key order.
/// </remarks>
public abstract class WorkflowCheckpointStoreContract : TenantIsolationContract<IWorkflowCheckpointStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.CreateAsync(Record(tenantId, name, "c-1") with { RunId = IsolationRunId });
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var sessionId = (string)key;
        var state = await Store.ReadAsync(tenantId, sessionId, "c-1");

        var byRun = await Store.ListByRunAsync(tenantId, IsolationRunId);
        (byRun.Count > 0).ShouldBe(state is not null);

        return state is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId, "secret")).Count + (await Store.ListAsync(tenantId, "shared-name")).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key) > 0;

    private static readonly Guid IsolationRunId = AgentPrismId.NewId();

    [Fact]
    public async Task Written_checkpoint_is_read_back()
    {
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1"));

        var state = await Store.ReadAsync("tenant-a", "s-1", "c-1");

        state.ShouldNotBeNull();
        state.Value.GetProperty("stepNumber").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Polymorphic_payload_is_read_back_with_KEY_ORDER_PRESERVED()
    {
        // 🚨 This is the whole reason the test exists: `jsonb` reorders keys
        // first by length, then by byte value. System.Text.Json requires its
        // `$type` discriminator to be the FIRST property of the object it
        // appears in; if the order is disturbed, reading blows up with "The
        // metadata property ... is not the first property". This is why the
        // column is `json`.
        //
        // The discriminator is kept inside a nested object because that is
        // how MAF's real payload is shaped too (measured in phase 15: a
        // 7,537-byte checkpoint contains `{"$type":0,...}`).
        const string Payload = """
            {"stepNumber":0,"edges":{"writer":[{"$type":0,"hasCondition":false,"kind":0}]},"zzzz":"last"}
            """;

        using var document = JsonDocument.Parse(Payload);

        await Store.CreateAsync(Record("tenant-a", "s-poly", "c-poly") with
        {
            State = document.RootElement.Clone(),
        });

        var state = await Store.ReadAsync("tenant-a", "s-poly", "c-poly");

        state.ShouldNotBeNull();

        var edge = state.Value
            .GetProperty("edges")
            .GetProperty("writer")[0];

        // The first property must still be `$type`.
        var firstProperty = edge.EnumerateObject().First();

        firstProperty.Name.ShouldBe("$type");
        firstProperty.Value.GetInt32().ShouldBe(0);

        // The order is preserved at the root too: "stepNumber" (10
        // characters) would fall after "edges" (5 characters) and "zzzz" (4
        // characters) under `jsonb`.
        state.Value.EnumerateObject().Select(static property => property.Name)
            .ShouldBe(["stepNumber", "edges", "zzzz"]);
    }

    [Fact]
    public async Task Another_tenants_checkpoint_is_NOT_FOUND()
    {
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1"));

        // Not "unauthorized" -- "not found". A checkpoint carries the entire
        // execution state; even knowledge of its existence must not leak.
        (await Store.ReadAsync("tenant-b", "s-1", "c-1")).ShouldBeNull();
        (await Store.ListAsync("tenant-b", "s-1")).ShouldBeEmpty();
        (await Store.DeleteAsync("tenant-b", "s-1")).ShouldBe(0);

        (await Store.ReadAsync("tenant-a", "s-1", "c-1")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Listing_preserves_creation_order()
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);

        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1") with { CreatedAt = start });
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-2") with
        {
            CreatedAt = start.AddSeconds(1),
            ParentCheckpointId = "c-1",
        });
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-3") with
        {
            CreatedAt = start.AddSeconds(2),
            ParentCheckpointId = "c-2",
        });

        var list = await Store.ListAsync("tenant-a", "s-1");

        list.Select(static record => record.CheckpointId).ShouldBe(["c-1", "c-2", "c-3"]);
        list[1].ParentCheckpointId.ShouldBe("c-1");
    }

    [Fact]
    public async Task Listing_does_NOT_CARRY_the_state_payload()
    {
        // A checkpoint carries kilobytes of opaque JSON; including it in the
        // list would make the checkpoint screen in the UI unusable.
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1"));

        var list = await Store.ListAsync("tenant-a", "s-1");

        WorkflowCheckpointState.IsOmitted(list[0].State).ShouldBeTrue();
    }

    [Fact]
    public async Task Listing_by_run_filters_correctly()
    {
        var runA = AgentPrismId.NewId();
        var runB = AgentPrismId.NewId();
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);

        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1") with { RunId = runA, CreatedAt = start });
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-2") with
        {
            RunId = runB,
            CreatedAt = start.AddSeconds(1),
        });
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-3") with
        {
            RunId = runA,
            CreatedAt = start.AddSeconds(2),
        });

        var list = await Store.ListByRunAsync("tenant-a", runA);

        list.Select(static record => record.CheckpointId).ShouldBe(["c-1", "c-3"]);
    }

    [Fact]
    public async Task Delete_clears_the_entire_session()
    {
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1"));
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-2"));
        await Store.CreateAsync(Record("tenant-a", "s-2", "c-3"));

        (await Store.DeleteAsync("tenant-a", "s-1")).ShouldBe(2);
        (await Store.ListAsync("tenant-a", "s-1")).ShouldBeEmpty();
        (await Store.ListAsync("tenant-a", "s-2")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Nonexistent_checkpoint_returns_null()
        => (await Store.ReadAsync("tenant-a", "s-1", "missing")).ShouldBeNull();

    private static WorkflowCheckpointRecord Record(string tenantId, string sessionId, string checkpointId)
        => new()
        {
            Id = AgentPrismId.NewId(),
            TenantId = tenantId,
            SessionId = sessionId,
            CheckpointId = checkpointId,
            CreatedAt = DateTimeOffset.UtcNow,
            State = DefaultState,
        };

    private static JsonElement DefaultState { get; } =
        JsonDocument.Parse("""{"stepNumber":3,"executors":["writer","editor"]}""").RootElement.Clone();
}
