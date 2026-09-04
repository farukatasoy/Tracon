using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// <see cref="RunEventType.Custom"/> (phase 141): the two-way rule between
/// <see cref="RunEventDraft.Type"/> and <see cref="RunEventDraft.CustomType"/>,
/// and the <see cref="RunEventCustomTypes"/> shape it is validated against.
/// </summary>
public sealed class RunEventDraftValidationTests
{
    [Fact]
    public async Task A_Custom_event_with_a_valid_type_is_accepted()
    {
        var writer = await CreateStartedWriterAsync();

        var runEvent = await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.preview-ready" });

        runEvent.Type.ShouldBe(RunEventType.Custom);
        runEvent.CustomType.ShouldBe("contoso.preview-ready");
    }

    [Fact]
    public async Task A_Custom_event_without_a_type_is_rejected()
    {
        var writer = await CreateStartedWriterAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendAsync(new RunEventDraft(RunEventType.Custom)));
    }

    [Fact]
    public async Task A_rejected_call_does_not_burn_a_sequence_number()
    {
        // ValidateCustomType runs BEFORE Interlocked.Increment in AppendAsync --
        // a rejected call must leave the stream gapless, not skip a number.
        var writer = await CreateStartedWriterAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendAsync(new RunEventDraft(RunEventType.Custom)));

        var runEvent = await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.preview-ready" });

        // Sequence 0 was RunStarted (StartAsync); the rejected call must not
        // have consumed 1, so this successful append is still 1, not 2.
        runEvent.Sequence.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Contoso.PreviewReady")]
    [InlineData("has spaces")]
    [InlineData("has/slash")]
    public async Task A_Custom_event_with_a_malformed_type_is_rejected(string customType)
    {
        var writer = await CreateStartedWriterAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = customType }));
    }

    [Fact]
    public async Task A_Custom_event_with_a_129_character_type_is_rejected()
    {
        var writer = await CreateStartedWriterAsync();
        var tooLong = new string('a', 129);

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = tooLong }));
    }

    [Fact]
    public async Task A_Custom_event_with_a_128_character_type_is_accepted()
    {
        var writer = await CreateStartedWriterAsync();
        var maxLength = new string('a', 128);

        var runEvent = await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = maxLength });

        runEvent.CustomType.ShouldBe(maxLength);
    }

    [Fact]
    public async Task A_Custom_event_under_the_reserved_agentprism_prefix_is_rejected()
    {
        var writer = await CreateStartedWriterAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "agentprism.internal" }));
    }

    [Fact]
    public async Task A_non_Custom_event_carrying_a_type_is_rejected()
    {
        // 🚨 The other half of the two-way rule (141.1): a caller who sets
        // CustomType on a built-in event type must be told, not silently
        // dropped -- every store discards it, since only Custom gives it
        // any meaning.
        var writer = await CreateStartedWriterAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendAsync(
                new RunEventDraft(RunEventType.ToolInvoking) { ToolName = "get_order_status", CustomType = "contoso.oops" }));
    }

    [Fact]
    public async Task A_non_Custom_event_without_a_type_is_unaffected()
    {
        var writer = await CreateStartedWriterAsync();

        var runEvent = await writer.AppendAsync(new RunEventDraft(RunEventType.ToolInvoking) { ToolName = "get_order_status" });

        runEvent.CustomType.ShouldBeNull();
    }

    [Fact]
    public async Task Concurrent_Custom_writes_get_distinct_gapless_sequence_numbers()
    {
        // The sequence number comes from Interlocked.Increment regardless of
        // event type -- this is the direct evidence that a Custom write does
        // not carve out an exception from that guarantee.
        var writer = await CreateStartedWriterAsync();

        var appends = Enumerable.Range(0, 20)
            .Select(i => writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = $"contoso.event-{i}" }).AsTask());

        var events = await Task.WhenAll(appends);

        events.Select(static e => e.Sequence).Order().ShouldBe(Enumerable.Range(1, 20).Select(static i => (long)i));
    }

    private static async Task<RunEventWriter> CreateStartedWriterAsync()
    {
        var store = new InMemoryRunStore();
        var writer = new RunEventWriter(store, new AgentPrismRunRecordingOptions(), NullLogger.Instance, AgentPrismId.NewId());

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        return writer;
    }
}
