using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Guards;

/// <summary>
/// Verifies that output inspection buffers a streaming response.
/// </summary>
/// <remarks>
/// Measures the natural limit of streaming: once a frame is sent to the caller
/// it cannot be taken back. That is why the stream is buffered while output
/// inspection is on; a pattern does not match against a partial frame.
/// </remarks>
public sealed class ContentGuardStreamingTests
{
    private static readonly IReadOnlyList<ChatResponseUpdate> SplitCardNumber =
    [
        new ChatResponseUpdate(ChatRole.Assistant, "card number 4539"),
        new ChatResponseUpdate(ChatRole.Assistant, "5787"),
        new ChatResponseUpdate(ChatRole.Assistant, "6362"),
        new ChatResponseUpdate(ChatRole.Assistant, "1486 was"),
    ];

    [Fact]
    public async Task A_pattern_split_across_frames_does_not_slip_through_a_buffered_stream()
    {
        // 🚨 The rationale for this phase's streaming decision: no single frame
        // by itself contains "4539578763621486". Without buffering, the pattern
        // WOULD have slipped through.
        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates: SplitCardNumber),
            new AgentPrismContentGuardOptions(),
            StubContentGuard.Blocking("4539578763621486"));

        var seen = 0;

        await Should.ThrowAsync<AgentPrismContentBlockedException>(async () =>
        {
            await foreach (var _ in chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "what was the card number")],
                cancellationToken: TestContext.Current.CancellationToken))
            {
                seen++;
            }
        });

        // 🚨 No frame reached the caller: the buffer does not drain before a decision is made.
        seen.ShouldBe(0);
    }

    [Fact]
    public async Task With_buffering_disabled_a_split_pattern_slips_through()
    {
        // Measures what the setting buys. This behavior is not a defect, it is
        // an explicit trade-off: silently doing half an inspection is worse
        // than doing none, so the choice stands as a visible setting, not a
        // hidden one.
        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates: SplitCardNumber),
            new AgentPrismContentGuardOptions { BufferStreamingOutput = false },
            StubContentGuard.Blocking("4539578763621486"));

        var seen = 0;

        await foreach (var _ in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "what was the card number")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            seen++;
        }

        seen.ShouldBe(SplitCardNumber.Count);
    }

    [Fact]
    public async Task Masking_preserves_the_total_text_of_a_buffered_stream()
    {
        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates: SplitCardNumber),
            new AgentPrismContentGuardOptions(),
            StubContentGuard.Masking("4539578763621486", "[redacted]"));

        var text = string.Empty;

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "what was the card number")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            text += update.Text;
        }

        text.ShouldBe("card number [redacted] was");
    }

    [Fact]
    public async Task Non_text_content_is_preserved_in_a_buffered_stream()
    {
        // Usage counters and tool calls must stay intact: recording and cost
        // accounting depend on them.
        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates:
            [
                new ChatResponseUpdate(ChatRole.Assistant, "forbidden text"),
                new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 7 })]),
            ]),
            new AgentPrismContentGuardOptions(),
            StubContentGuard.Masking("forbidden", "***"));

        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.SelectMany(static update => update.Contents)
            .OfType<UsageContent>()
            .ShouldHaveSingleItem()
            .Details.InputTokenCount.ShouldBe(7);

        string.Concat(updates.Select(static update => update.Text)).ShouldBe("*** text");
    }

    [Fact]
    public async Task When_output_inspection_is_disabled_the_stream_is_not_buffered()
    {
        // Liveness is preserved: frames flow through as they arrive.
        var guard = StubContentGuard.Blocking("never-matches");

        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates: SplitCardNumber),
            new AgentPrismContentGuardOptions { InspectOutput = false },
            guard);

        var seen = 0;

        await foreach (var _ in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            seen++;
        }

        seen.ShouldBe(SplitCardNumber.Count);
        guard.SeenDirections.ShouldNotContain(ContentGuardDirection.Output);
    }

    [Fact]
    public async Task Input_is_also_inspected_on_the_streaming_path()
    {
        var inner = new FakeChatClient(streamingUpdates: SplitCardNumber);

        using var chatClient = Guarded(
            inner,
            new AgentPrismContentGuardOptions(),
            StubContentGuard.Blocking("secret-project"));

        await Should.ThrowAsync<AgentPrismContentBlockedException>(async () =>
        {
            await foreach (var _ in chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "what is secret-project")],
                cancellationToken: TestContext.Current.CancellationToken))
            {
                // No frame is expected.
            }
        });

        inner.CallCount.ShouldBe(0);
    }

    private static IChatClient Guarded(
        FakeChatClient inner,
        AgentPrismContentGuardOptions options,
        params IContentGuard[] guards)
        => TestData
            .Providers(TestData.ContentGuards(options: options, guards: guards), new FakeModelProvider(inner))
            .CreateChatClient(TestData.Binding());
}
