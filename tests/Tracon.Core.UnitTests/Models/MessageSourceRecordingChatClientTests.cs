using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// F-773 (phase 267): when the message source ring writes the run's context event and when it leaves a call
/// alone. The position of the ring in the real pipeline is measured by <c>ContextSourceRecordingTests</c>
/// (functional); here are the guards that decide whose call it is.
/// </summary>
public sealed class MessageSourceRecordingChatClientTests
{
    private static readonly ChatMessage[] Request = [new(ChatRole.User, "hello")];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_first_own_call_writes_the_event_and_the_later_calls_of_the_run_do_not(bool streaming)
    {
        var (store, runId, scope) = await OpenRunAsync();
        TraconRunContext.SetCurrent(scope);
        var ring = Ring(new FakeChatClient());

        await CallAsync(ring, streaming);
        await CallAsync(ring, streaming);
        await CallAsync(ring, streaming);

        (await ContextEventsAsync(store, runId)).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_side_channel_call_before_the_agents_first_call_does_not_take_the_claim(bool streaming)
    {
        // The compaction summarizer runs BEFORE the agent's first model call. If the ring claimed on its call,
        // the run would record the summarizer's request and never its own.
        var (store, runId, scope) = await OpenRunAsync();
        var ring = Ring(new FakeChatClient());

        TraconRunContext.SetCurrent(scope with { SideChannelFallback = new FallbackModelAttribution() });
        await CallAsync(ring, streaming);

        (await ContextEventsAsync(store, runId)).ShouldBeEmpty();

        TraconRunContext.SetCurrent(scope);
        await CallAsync(ring, streaming);

        (await ContextEventsAsync(store, runId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_call_outside_a_run_passes_through()
    {
        TraconRunContext.SetCurrent(null);
        var inner = new FakeChatClient();

        await CallAsync(Ring(inner), streaming: false);

        inner.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_scope_that_records_no_context_writes_nothing()
    {
        // A workflow opens a scope for itself; it holds no claim.
        var (store, runId, scope) = await OpenRunAsync();
        TraconRunContext.SetCurrent(scope with { ContextRecord = null });

        await CallAsync(Ring(new FakeChatClient()), streaming: false);

        (await ContextEventsAsync(store, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Calls_that_start_together_write_one_event()
    {
        var (store, runId, scope) = await OpenRunAsync();
        TraconRunContext.SetCurrent(scope);
        var ring = Ring(new FakeChatClient());

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => CallAsync(ring, streaming: false))));

        (await ContextEventsAsync(store, runId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_call_below_gets_the_messages_it_would_have_got_and_a_lazy_request_is_read_once()
    {
        var (_, _, scope) = await OpenRunAsync();
        TraconRunContext.SetCurrent(scope);
        var inner = new FakeChatClient();
        var reads = 0;

        IEnumerable<ChatMessage> Lazy()
        {
            reads++;
            yield return new ChatMessage(ChatRole.User, "first");
            yield return new ChatMessage(ChatRole.User, "second");
        }

        await Ring(inner).GetResponseAsync(Lazy(), cancellationToken: TestContext.Current.CancellationToken);

        reads.ShouldBe(1);
        inner.LastRequest.Select(static m => m.Text).ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task The_event_counts_what_the_call_below_is_sent()
    {
        var (store, runId, scope) = await OpenRunAsync();
        TraconRunContext.SetCurrent(scope);

        await Ring(new FakeChatClient()).GetResponseAsync(
            [new ChatMessage(ChatRole.User, "a"), new ChatMessage(ChatRole.User, "b")],
            cancellationToken: TestContext.Current.CancellationToken);

        (await ContextEventsAsync(store, runId)).ShouldHaveSingleItem().Text.ShouldBe("context: 2 messages");
    }

    private static MessageSourceRecordingChatClient Ring(IChatClient inner)
        => new(inner, NullLogger.Instance);

    private static async Task CallAsync(MessageSourceRecordingChatClient ring, bool streaming)
    {
        if (streaming)
        {
            await foreach (var _ in ring.GetStreamingResponseAsync(Request, cancellationToken: TestContext.Current.CancellationToken))
            {
                // drain the stream
            }
        }
        else
        {
            await ring.GetResponseAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Opens a started run with an event writer. The caller sets the scope in its own body: an ambient
    /// write made inside this async helper would not reach the caller.
    /// </summary>
    private static async Task<(InMemoryRunStore Store, Guid RunId, AgentRunScope Scope)> OpenRunAsync()
    {
        var store = new InMemoryRunStore();
        var runId = TraconId.NewId();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, runId, metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = runId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        return (store, runId, new AgentRunScope { RunId = runId, RootRunId = runId, Writer = writer, ContextRecord = new ContextRecordClaim() });
    }

    private static async Task<List<RunEvent>> ContextEventsAsync(InMemoryRunStore store, Guid runId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(runId, 0, TestContext.Current.CancellationToken))
        {
            if (string.Equals(runEvent.CustomType, RunEventCustomTypes.Context, StringComparison.Ordinal))
            {
                events.Add(runEvent);
            }
        }

        return events;
    }
}
