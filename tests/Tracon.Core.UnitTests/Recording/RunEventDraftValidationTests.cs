using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Recording;

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
    public async Task A_Custom_event_under_the_reserved_tracon_prefix_is_rejected()
    {
        var writer = await CreateStartedWriterAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "tracon.internal" }));
    }

    [Fact]
    public async Task The_quota_threshold_reserved_type_is_rejected_by_the_public_append_path()
    {
        // A regression guard for the concrete value a consumer could plausibly
        // try to imitate, not just the reserved prefix in the abstract.
        var writer = await CreateStartedWriterAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendAsync(
                new RunEventDraft(RunEventType.Custom) { CustomType = RunEventCustomTypes.QuotaThreshold }));
    }

    [Fact]
    public async Task A_reserved_CustomType_is_accepted_through_AppendReservedAsync()
    {
        // AppendReservedAsync is internal -- only Tracon's own code (the
        // quota threshold notice) can reach it; this proves the bypass exists
        // and is scoped to exactly the reserved-prefix check, nothing looser.
        var writer = await CreateStartedWriterAsync();

        var runEvent = await writer.AppendReservedAsync(
            new RunEventDraft(RunEventType.Custom) { CustomType = RunEventCustomTypes.QuotaThreshold, Payload = "{}" });

        runEvent.CustomType.ShouldBe(RunEventCustomTypes.QuotaThreshold);
    }

    [Fact]
    public async Task AppendReservedAsync_still_rejects_a_malformed_type()
    {
        // The bypass is narrow: it lifts ONLY the reserved-prefix check, not
        // the shape check every Custom event must still pass.
        var writer = await CreateStartedWriterAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendReservedAsync(
                new RunEventDraft(RunEventType.Custom) { CustomType = "Tracon.Bad-Case" }));
    }

    [Fact]
    public async Task A_reserved_Custom_events_payload_survives_even_when_RecordToolPayloads_is_off()
    {
        // The quota threshold notice's NoticeId is the function itself, not an
        // observability detail (the same exception WorkflowRequest already
        // gets) -- suppressing it would leave the client an unlabeled frame.
        var store = new InMemoryRunStore();
        var options = new TraconRunRecordingOptions { RecordToolPayloads = false };
        var writer = new RunEventWriter(store, options, NullLogger.Instance, TraconId.NewId(), metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        var runEvent = await writer.AppendReservedAsync(
            new RunEventDraft(RunEventType.Custom) { CustomType = RunEventCustomTypes.QuotaThreshold, Payload = "{\"noticeId\":\"abc\"}" });

        runEvent.Payload.ShouldBe("{\"noticeId\":\"abc\"}");

        // An ordinary Custom event (through the public path) still loses its
        // payload with the same setting off -- the exception is narrow.
        var ordinary = await writer.AppendAsync(
            new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.preview-ready", Payload = "{\"x\":1}" });

        ordinary.Payload.ShouldBeNull();
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
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        return writer;
    }
}
