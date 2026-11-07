using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A streamed reply goes to its caller chunk by chunk without waiting for the store, and the text it carried is in the
/// store when the run is over - whichever way the run ends, and whether or not the caller read the stream to its end.
/// </summary>
/// <remarks>
/// The reply is recorded in these chunks and nowhere else (the run row carries no output), so the end of the run is
/// where a queued chunk would be lost. Each scenario holds the store busy with a group before it starts.
/// </remarks>
public sealed class StreamDoesNotWaitForStoreTests
{
    private const int Chunks = 40;

    private static IReadOnlyList<ChatResponseUpdate> Updates()
        => [.. Enumerable.Range(0, Chunks).Select(static i => new ChatResponseUpdate(ChatRole.Assistant, $"c{i} "))];

    private static string Reply() => string.Concat(Enumerable.Range(0, Chunks).Select(static i => $"c{i} "));

    [Fact]
    public async Task Every_chunk_reaches_the_caller_while_the_store_is_stuck_on_a_group()
    {
        var store = new GroupSpyRunStore(new InMemoryRunStore(tenantContext: new FixedTenantContext()));
        store.Hold();

        var agent = CreateAgent(store, new FakeChatClient(streamingUpdates: Updates()));
        var received = new List<string>();
        var consuming = Task.Run(async () =>
        {
            await foreach (var update in agent.RunStreamingAsync("hi"))
            {
                lock (received)
                {
                    received.Add(update.Text);
                }
            }
        });

        // Before: each chunk waited for its own write, so a store that does not answer held the stream at the first one.
        await WaitUntil.TrueAsync(() => { lock (received) { return received.Count == Chunks; } }, "every chunk to reach the caller");

        store.GroupsEntered.ShouldBeGreaterThan(0);
        consuming.IsCompleted.ShouldBeFalse("the end of the run still waits for the text of the reply");

        store.Release();
        await consuming.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var events = await ReadEventsAsync(store);
        string.Concat(events.Where(static e => e.Type == RunEventType.MessageDelta).Select(static e => e.Text)).ShouldBe(Reply());
        events[^1].Type.ShouldBe(RunEventType.RunCompleted);
        events.Select(static e => e.Sequence).ShouldBe(Enumerable.Range(0, events.Count).Select(static n => (long)n));
    }

    [Fact]
    public async Task A_caller_that_stops_reading_leaves_the_text_it_was_given_in_the_store()
    {
        var store = new GroupSpyRunStore(new InMemoryRunStore(tenantContext: new FixedTenantContext()));
        store.Hold();

        var agent = CreateAgent(store, new FakeChatClient(streamingUpdates: Updates()));

        await using (var enumerator = agent.RunStreamingAsync("hi").GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            for (var i = 0; i < 3; i++)
            {
                (await enumerator.MoveNextAsync()).ShouldBeTrue();
            }

            // Walking away is DisposeAsync: it runs the closing steps, and they have to wait for the store even though
            // nobody is reading any more.
            var disposing = enumerator.DisposeAsync().AsTask();

            await WaitUntil.TrueAsync(() => store.GroupsEntered > 0, "the writer to hand a group to the store");
            store.Release();
            await disposing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Canceled);

        var events = await ReadEventsAsync(store);
        string.Concat(events.Where(static e => e.Type == RunEventType.MessageDelta).Select(static e => e.Text)).ShouldBe("c0 c1 c2 ");
        events.Select(static e => e.Sequence).ShouldBe(Enumerable.Range(0, events.Count).Select(static n => (long)n));
    }

    [Fact]
    public async Task A_stream_that_is_cancelled_while_it_runs_keeps_the_text_it_had_produced()
    {
        var store = new GroupSpyRunStore(new InMemoryRunStore(tenantContext: new FixedTenantContext()));
        var agent = CreateAgent(store, new FakeChatClient(streamingUpdates: Updates()));

        using var cancel = new CancellationTokenSource();
        var seen = 0;

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in agent.RunStreamingAsync("hi", cancellationToken: cancel.Token))
            {
                _ = update;

                if (++seen == 5)
                {
                    await cancel.CancelAsync();
                }
            }
        });

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Canceled);

        // However many chunks the model produced before the stop was seen, every one that reached the caller is recorded.
        var recorded = (await ReadEventsAsync(store)).Count(static e => e.Type == RunEventType.MessageDelta);
        recorded.ShouldBeGreaterThanOrEqualTo(5);
        recorded.ShouldBeLessThan(Chunks);
    }

    [Fact]
    public async Task A_stream_that_fails_keeps_the_text_it_had_produced()
    {
        var store = new GroupSpyRunStore(new InMemoryRunStore(tenantContext: new FixedTenantContext()));
        store.Hold();

        var agent = CreateAgent(store, new FailingAfterChunksChatClient(chunks: 12));
        var consuming = Task.Run(async () =>
        {
            await foreach (var update in agent.RunStreamingAsync("hi"))
            {
                _ = update;
            }
        });

        await WaitUntil.TrueAsync(() => store.GroupsEntered > 0, "the writer to hand a group to the store");
        store.Release();

        await Should.ThrowAsync<ProviderInvocationException>(() => consuming.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);

        var events = await ReadEventsAsync(store);
        events.Count(static e => e.Type == RunEventType.MessageDelta).ShouldBe(12);
        events[^1].Type.ShouldBe(RunEventType.RunFailed);
        events.Select(static e => e.Sequence).ShouldBe(Enumerable.Range(0, events.Count).Select(static n => (long)n));
    }

    [Fact]
    public async Task With_the_option_off_the_stream_waits_for_each_chunk_as_before()
    {
        var store = new GroupSpyRunStore(new InMemoryRunStore(tenantContext: new FixedTenantContext()));
        var agent = CreateAgent(store, new FakeChatClient(streamingUpdates: Updates()), new TraconRunRecordingOptions { WriteDeltasBehind = false });

        await foreach (var update in agent.RunStreamingAsync("hi"))
        {
            _ = update;
        }

        store.GroupsEntered.ShouldBe(0, "no group is ever formed");
        (await ReadEventsAsync(store)).Count(static e => e.Type == RunEventType.MessageDelta).ShouldBe(Chunks);
    }

    private static async Task<List<RunEvent>> ReadEventsAsync(GroupSpyRunStore store)
    {
        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        return await store.ReadAsync(run.Id);
    }

    private static RunRecordingAgent CreateAgent(IRunStore store, IChatClient client, TraconRunRecordingOptions? options = null)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            options ?? new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }

    /// <summary>A model that streams some chunks and then fails, as a provider connection that drops does.</summary>
    private sealed class FailingAfterChunksChatClient(int chunks) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < chunks; i++)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, $"c{i} ");
                await Task.Yield();
            }

            throw new InvalidOperationException("the provider connection dropped");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
