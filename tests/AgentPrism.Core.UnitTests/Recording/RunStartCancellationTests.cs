using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// HATA-S4-012: verifies that a cancellation arriving while the <c>RunStarted</c>
/// event is ALREADY written to the store, but BEFORE the model call has started
/// (during the input-recording step), moves the run to <see cref="RunStatus.Canceled"/>
/// instead of leaving it stuck in <see cref="RunStatus.Running"/> forever.
/// </summary>
/// <remarks>
/// The Playground's own <c>AbortController.abort()</c> (triggered by a component
/// unmount during SPA navigation) cut the POST request that starts the run in
/// exactly this narrow window: the old single-block form of
/// <c>RunRecordingAgent.BeginRunAsync</c> left an <see cref="OperationCanceledException"/>
/// thrown during input recording (<see cref="IRunInputStore.SaveAsync"/>) OUTSIDE
/// the caller's try/finally safety net (HATA-S1-015).
/// </remarks>
public sealed class RunStartCancellationTests
{
    [Fact]
    public async Task Cancellation_during_input_recording_writes_Canceled_for_non_streaming_run_and_does_not_leave_it_stuck_Running()
    {
        using var cts = new CancellationTokenSource();
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var inputStore = new CancelingRunInputStore(cts);
        var agent = CreateAgent(store, new FakeChatClient(), inputStore);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await agent.RunAsync("hello", cancellationToken: cts.Token));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Canceled);
        run.CompletedAt.ShouldNotBeNull();

        var types = await ReadEventTypesAsync(store, run.Id);
        types[0].ShouldBe(RunEventType.RunStarted);

        // BlockingChatClient was never called: cancellation happened before the model was ever reached.
        inputStore.SaveAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_during_input_recording_writes_Canceled_for_streaming_run_and_does_not_leave_it_stuck_Running()
    {
        using var cts = new CancellationTokenSource();
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var inputStore = new CancelingRunInputStore(cts);
        var agent = CreateAgent(store, new FakeChatClient(), inputStore);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("hello", cancellationToken: cts.Token))
            {
                // Cancellation is expected before BlockingChatClient/FakeChatClient
                // can produce any frame; the loop must never execute.
            }
        });

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Canceled);
        run.CompletedAt.ShouldNotBeNull();
        run.IsStreaming.ShouldBeTrue();

        var types = await ReadEventTypesAsync(store, run.Id);
        types[0].ShouldBe(RunEventType.RunStarted);
    }

    private static async Task<List<RunEventType>> ReadEventTypesAsync(InMemoryRunStore store, Guid runId)
    {
        var types = new List<RunEventType>();
        await foreach (var runEvent in store.ReadEventsAsync(runId))
        {
            types.Add(runEvent.Type);
        }

        return types;
    }

    private static RunRecordingAgent CreateAgent(IRunStore store, FakeChatClient client, IRunInputStore inputStore)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            runInputStore: inputStore);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }

    /// <summary>
    /// Mimics a real client abort: input recording cancels the token called at
    /// EXACTLY this instant and throws its own exception from that (now canceled)
    /// token — the exact scenario that <c>SaveInputAsync</c>'s
    /// <c>catch (Exception ex) when (ex is not OperationCanceledException)</c>
    /// filter DELIBERATELY does not catch.
    /// </summary>
    private sealed class CancelingRunInputStore(CancellationTokenSource cts) : IRunInputStore
    {
        public int SaveAttempts { get; private set; }

        public ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask<RunInputRecord?> GetAsync(
            string tenantId,
            Guid runId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
