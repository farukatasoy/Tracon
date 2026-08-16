using Microsoft.Extensions.AI;

namespace AgentPrism.StoreContracts;

/// <summary>
/// Behavior tests for the <see cref="IRunInputStore"/> contract (phase 47).
/// </summary>
/// <remarks>
/// <para>
/// The in-memory store and the three SQL providers must pass the same
/// scenarios.
/// </para>
/// <para>
/// 🚨 The most valuable test is
/// <see cref="Polymorphic_content_round_trips_with_KEY_ORDER_preserved"/>:
/// if the <c>messages</c> column is written as <c>jsonb</c>, PostgreSQL
/// reorders the object keys, the <c>$type</c> discriminator stops being the
/// first property, and the read fails with a <c>JsonException</c> (K-027).
/// Neither the build nor the other tests catch this.
/// </para>
/// </remarks>
public abstract class RunInputStoreContract : IAsyncLifetime
{
    private const string Tenant = "test";

    /// <summary>The input store under test.</summary>
    protected IRunInputStore Store { get; private set; } = null!;

    /// <summary>Produces an empty input store for testing.</summary>
    /// <returns>A store ready for use.</returns>
    protected abstract ValueTask<IRunInputStore> CreateStoreAsync();

    /// <summary>
    /// Opens a run row at the given id before an input is written.
    /// </summary>
    /// <param name="runId">The run id.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// In the SQL implementations, <c>run_inputs.run_id</c> is a foreign key
    /// into the <c>runs</c> table; the in-memory implementation has no such
    /// link, and the hook does nothing.
    /// </remarks>
    protected virtual ValueTask PrepareRunAsync(Guid runId, string tenantId) => default;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Hook for the derived class to release its own resources.</summary>
    /// <returns>The completion task.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    [Fact]
    public async Task Written_input_round_trips_exactly()
    {
        var runId = await NewRunAsync();

        await Store.SaveAsync(Record(runId, [new ChatMessage(ChatRole.User, "istanbul weather")]));

        var read = await Store.GetAsync(Tenant, runId);

        read.ShouldNotBeNull();
        read!.RunId.ShouldBe(runId);
        read.TenantId.ShouldBe(Tenant);
        read.Messages.Count.ShouldBe(1);
        read.Messages[0].Role.ShouldBe(ChatRole.User);
        read.Messages[0].Text.ShouldBe("istanbul weather");
    }

    [Fact]
    public async Task Run_without_a_record_returns_null()
        => (await Store.GetAsync(Tenant, AgentPrismId.NewId())).ShouldBeNull();

    [Fact]
    public async Task Second_write_is_IGNORED()
    {
        var runId = await NewRunAsync();

        await Store.SaveAsync(Record(runId, [new ChatMessage(ChatRole.User, "first")]));
        await Store.SaveAsync(Record(runId, [new ChatMessage(ChatRole.User, "second")]));

        // 🚨 A queued run (phase 46) starts twice with the SAME id; the input
        // must not change.
        var read = await Store.GetAsync(Tenant, runId);

        read!.Messages[0].Text.ShouldBe("first");
    }

    [Fact]
    public async Task Polymorphic_content_round_trips_with_KEY_ORDER_preserved()
    {
        var runId = await NewRunAsync();

        // Text + image reference + tool result: all three are SEPARATE
        // AIContent types and cannot be read back without the `$type`
        // discriminator.
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Give a short answer."),
            new(
                ChatRole.User,
                [
                    new TextContent("explain this image"),
                    new UriContent("https://example/image.png", "image/png"),
                ]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "23 degrees")]),
        };

        await Store.SaveAsync(Record(runId, messages));

        var read = await Store.GetAsync(Tenant, runId);

        read.ShouldNotBeNull();
        read!.Messages.Count.ShouldBe(3);
        read.Messages[0].Role.ShouldBe(ChatRole.System);

        var user = read.Messages[1];

        user.Contents.OfType<TextContent>().Single().Text.ShouldBe("explain this image");
        user.Contents.OfType<UriContent>().Single().MediaType.ShouldBe("image/png");

        var toolResult = read.Messages[2].Contents.OfType<FunctionResultContent>().Single();

        toolResult.CallId.ShouldBe("call-1");
        toolResult.Result?.ToString().ShouldBe("23 degrees");
    }

    [Fact]
    public async Task Another_tenants_input_cannot_be_read()
    {
        var runId = await NewRunAsync("tenant-a");

        await Store.SaveAsync(
            Record(runId, [new ChatMessage(ChatRole.User, "secret")], tenantId: "tenant-a"));

        // "Does not exist" and "belongs to someone else" are the SAME result
        // to the caller; existence does not leak.
        (await Store.GetAsync("tenant-b", runId)).ShouldBeNull();
        (await Store.GetAsync("tenant-a", runId)).ShouldNotBeNull();
    }

    private async ValueTask<Guid> NewRunAsync(string tenantId = Tenant)
    {
        var runId = AgentPrismId.NewId();

        await PrepareRunAsync(runId, tenantId);

        return runId;
    }

    private static RunInputRecord Record(
        Guid runId,
        IReadOnlyList<ChatMessage> messages,
        string tenantId = Tenant)
        => new()
        {
            RunId = runId,
            TenantId = tenantId,
            Messages = messages,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
