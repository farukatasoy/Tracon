using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// <see cref="IRunEventSink"/> (phase 70): fan-out from <see cref="RunEventWriter"/>
/// is independent of the store — a sink failure never disables the store, a store
/// failure never skips the sinks, and a bad sink never silences a healthy one.
/// </summary>
public sealed class RunEventSinkTests
{
    [Fact]
    public async Task No_sink_registered_leaves_the_run_unaffected()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var agent = CreateAgent(store, new FakeChatClient(), sinks: null);

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_sink_sees_the_exact_same_sequence_numbers_as_the_store()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var sink = new SpyRunEventSink();
        var agent = CreateAgent(store, new FakeChatClient(), [sink]);

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var storeSequences = (await ReadEventsAsync(store, run.Id)).Select(static e => e.Sequence).ToList();
        var sinkSequences = sink.Events.Select(static e => e.Sequence).ToList();

        sinkSequences.ShouldBe(storeSequences);
    }

    [Fact]
    public async Task A_sink_that_always_throws_is_disabled_after_its_first_failure_and_the_run_still_completes()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var sink = new ThrowingRunEventSink();
        var agent = CreateAgent(store, new FakeChatClient(), [sink]);

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);

        // The store write is COMPLETE: the sink's failure did not touch it.
        var storeEvents = await ReadEventsAsync(store, run.Id);
        storeEvents.Count.ShouldBeGreaterThan(1);
        storeEvents[^1].Type.ShouldBe(RunEventType.RunCompleted);

        // The sink was tried exactly once, then disabled for the rest of THIS run.
        sink.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task One_sink_failing_does_not_silence_a_healthy_sink_registered_alongside_it()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var badSink = new ThrowingRunEventSink();
        var goodSink = new SpyRunEventSink();
        var agent = CreateAgent(store, new FakeChatClient(), [badSink, goodSink]);

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var storeEvents = await ReadEventsAsync(store, run.Id);

        badSink.CallCount.ShouldBe(1);
        goodSink.Events.Count.ShouldBe(storeEvents.Count);
        goodSink.Events[^1].Type.ShouldBe(RunEventType.RunCompleted);
    }

    [Fact]
    public async Task A_sink_still_receives_every_event_when_the_store_itself_is_disabled()
    {
        // StartRunAsync succeeds (the run genuinely opens); every write AFTER
        // that fails — the store disables itself on the first AppendEventAsync.
        // The sink must keep receiving events regardless.
        var store = new AppendThrowingRunStore();
        var sink = new SpyRunEventSink();
        var agent = CreateAgent(store, new FakeChatClient(), [sink]);

        await agent.RunAsync("hello");

        sink.Events.ShouldNotBeEmpty();
        sink.Events[0].Type.ShouldBe(RunEventType.RunStarted);
        sink.Events[^1].Type.ShouldBe(RunEventType.RunCompleted);
    }

    [Fact]
    public async Task A_shared_sink_stamps_each_event_with_its_own_run_s_tenant()
    {
        var sink = new SpyRunEventSink();
        var storeA = new InMemoryRunStore(tenantContext: new FixedTenantContext("tenant-a"));
        var storeB = new InMemoryRunStore(tenantContext: new FixedTenantContext("tenant-b"));

        var agentA = CreateAgent(storeA, new FakeChatClient(), [sink], "tenant-a");
        var agentB = CreateAgent(storeB, new FakeChatClient(), [sink], "tenant-b");

        await agentA.RunAsync("hello");
        await agentB.RunAsync("hello");

        var runA = (await storeA.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var runB = (await storeB.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        sink.Events.Where(e => e.RunId == runA.Id).ShouldAllBe(e => e.TenantId == "tenant-a");
        sink.Events.Where(e => e.RunId == runB.Id).ShouldAllBe(e => e.TenantId == "tenant-b");
    }

    [Fact]
    public async Task A_sink_receives_the_terminal_event_when_the_consumer_disposes_the_stream_early()
    {
        // HATA-S1-015/K-398: the consumer abandons `await foreach` after the first
        // frame. RunRecordingAgent's finally block still writes a Canceled closing
        // event (RunFailed, "The run was canceled.") — the sink must see it too.
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var sink = new SpyRunEventSink();
        var client = new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "He"),
            new ChatResponseUpdate(ChatRole.Assistant, "llo"),
        ]);
        var agent = CreateAgent(store, client, [sink]);

        await foreach (var _ in agent.RunStreamingAsync("hi"))
        {
            break;
        }

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Canceled);

        var terminal = sink.Events.Single(e => e.RunId == run.Id && e.Type == RunEventType.RunFailed);
        terminal.Text.ShouldBe("The run was canceled.");
    }

    private static async Task<List<RunEvent>> ReadEventsAsync(InMemoryRunStore store, Guid runId)
    {
        var events = new List<RunEvent>();
        await foreach (var runEvent in store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        return events;
    }

    private static RunRecordingAgent CreateAgent(
        IRunStore store,
        FakeChatClient client,
        IReadOnlyList<IRunEventSink>? sinks,
        string tenantId = "test")
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(tenantId),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            sinks: sinks);
    }

    private sealed class FixedTenantContext(string tenantId = "test") : ITenantContext
    {
        public string TenantId => tenantId;
    }

    /// <summary>Records the exact events it receives, in the order it received them.</summary>
    private sealed class SpyRunEventSink : IRunEventSink
    {
        public List<RunEvent> Events { get; } = [];

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(runEvent);

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Throws on every call — used to prove sink failure isolation.</summary>
    private sealed class ThrowingRunEventSink : IRunEventSink
    {
        public int CallCount { get; private set; }

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            CallCount++;

            throw new InvalidOperationException("sink unavailable");
        }
    }

    /// <summary>
    /// <see cref="StartRunAsync"/> succeeds — the run genuinely opens — but every
    /// write after that fails, exactly like a store that goes down mid-run.
    /// </summary>
    private sealed class AppendThrowingRunStore : IRunStore
    {
        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RunRecord
            {
                Id = info.RunId,
                AgentName = info.AgentName,
                Status = info.Status,
                StartedAt = info.StartedAt,
            });

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(
            RunQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunStatistics> GetStatisticsAsync(
            RunStatisticsQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(
            Guid runId,
            long fromSequence = 0,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask RecordToolInvocationAsync(
            ToolInvocationRecord invocation,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(
            ToolUsageQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(
            ExperimentResultsQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
            RunTimeSeriesQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask UpdateRunCostAsync(
            Guid runId,
            RunCost? cost,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask TouchHeartbeatAsync(
            IReadOnlyCollection<Guid> runIds,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
            DateTimeOffset staleBefore,
            int max,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
