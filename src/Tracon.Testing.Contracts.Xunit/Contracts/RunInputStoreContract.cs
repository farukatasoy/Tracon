using Microsoft.Extensions.AI;

namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IRunInputStore"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// Every implementation must pass the same scenarios.
/// </para>
/// <para>
/// The most valuable test is
/// <see cref="Polymorphic_content_round_trips_with_KEY_ORDER_preserved"/>:
/// a store that reorders object keys on write (for example, one backed by a
/// JSON-typed column) can silently move the <c>$type</c> discriminator away
/// from the first property, and the read then fails with a
/// <c>JsonException</c>. Neither the build nor the other tests catch this.
/// </para>
/// </remarks>
public abstract class RunInputStoreContract : StoreCancellationContract<IRunInputStore>
{
    private const string Tenant = "test";

    /// <summary>The run the cancellation contract writes an input for.</summary>
    private readonly Guid _cancellationRunId = TraconId.NewId();

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
    protected override async ValueTask CancellableReadAsync(CancellationToken cancellationToken)
        => await Store.GetAsync(Tenant, _cancellationRunId, cancellationToken);

    /// <inheritdoc />
    protected override async ValueTask CancellableWriteAsync(CancellationToken cancellationToken)
    {
        await PrepareRunAsync(_cancellationRunId, Tenant);

        await Store.SaveAsync(
            Record(_cancellationRunId, [new ChatMessage(ChatRole.User, "cancelled")]),
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> WroteAnythingAsync()
        => await Store.GetAsync(Tenant, _cancellationRunId, CancellationToken.None) is not null;

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
        => (await Store.GetAsync(Tenant, TraconId.NewId())).ShouldBeNull();

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
    public async Task Parameter_values_round_trip_exactly()
    {
        var runId = await NewRunAsync();

        // Values are user content: quotes, a newline, a non-ASCII character and
        // text that looks like a placeholder must all come back byte for byte.
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tone"] = "formal",
            ["note"] = "line1\nline2 \"quoted\" {{tone}} \u00e7\u011f\u0131",
        };

        await Store.SaveAsync(Record(runId, [new ChatMessage(ChatRole.User, "hi")], parameters: values));

        var read = await Store.GetAsync(Tenant, runId);

        read.ShouldNotBeNull();
        read!.Parameters.ShouldNotBeNull();
        read.Parameters!.Count.ShouldBe(2);
        read.Parameters["tone"].ShouldBe("formal");
        read.Parameters["note"].ShouldBe(values["note"]);
    }

    [Fact]
    public async Task Missing_parameters_and_empty_parameters_stay_distinct()
    {
        var withoutRecord = await NewRunAsync();
        var withEmpty = await NewRunAsync();

        await Store.SaveAsync(Record(withoutRecord, [new ChatMessage(ChatRole.User, "a")]));
        await Store.SaveAsync(
            Record(withEmpty, [new ChatMessage(ChatRole.User, "b")], parameters: new Dictionary<string, string>(StringComparer.Ordinal)));

        // 🚨 NULL means "nothing was recorded", {} means "recorded, no values".
        // A resume that reads both as "no values" runs a parameterized
        // definition on its defaults and changes what the model is told.
        (await Store.GetAsync(Tenant, withoutRecord))!.Parameters.ShouldBeNull();

        var empty = (await Store.GetAsync(Tenant, withEmpty))!.Parameters;

        empty.ShouldNotBeNull();
        empty!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_run_with_values_and_no_messages_still_records_the_values()
    {
        var runId = await NewRunAsync();

        // A workflow resume starts from an empty message list.
        await Store.SaveAsync(
            Record(runId, [], parameters: new Dictionary<string, string>(StringComparer.Ordinal) { ["voice"] = "alloy" }));

        var read = await Store.GetAsync(Tenant, runId);

        read.ShouldNotBeNull();
        read!.Messages.Count.ShouldBe(0);
        read.Parameters!["voice"].ShouldBe("alloy");
    }

    [Fact]
    public async Task Second_write_never_changes_the_recorded_parameters()
    {
        var runId = await NewRunAsync();

        await Store.SaveAsync(
            Record(
                runId,
                [new ChatMessage(ChatRole.User, "first")],
                parameters: new Dictionary<string, string>(StringComparer.Ordinal) { ["tone"] = "formal" }));

        // A queued run that loses its lease starts again with the SAME id; the
        // second start must not rewrite what the first one ran with.
        await Store.SaveAsync(
            Record(
                runId,
                [new ChatMessage(ChatRole.User, "second")],
                parameters: new Dictionary<string, string>(StringComparer.Ordinal) { ["tone"] = "casual" }));

        (await Store.GetAsync(Tenant, runId))!.Parameters!["tone"].ShouldBe("formal");
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
        var runId = TraconId.NewId();

        await PrepareRunAsync(runId, tenantId);

        return runId;
    }

    private static RunInputRecord Record(
        Guid runId,
        IReadOnlyList<ChatMessage> messages,
        string tenantId = Tenant,
        IReadOnlyDictionary<string, string>? parameters = null)
        => new()
        {
            RunId = runId,
            TenantId = tenantId,
            Messages = messages,
            Parameters = parameters,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
