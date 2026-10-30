using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// <see cref="IRunEventSink"/>: fan-out from <see cref="RunEventWriter"/>
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
    public async Task A_consumer_written_Custom_event_reaches_every_sink_like_a_built_in_one()
    {
        // The sink contract is "every event", and Custom is not an
        // exception carved out of that promise.
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var sink = new SpyRunEventSink();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics: null, [sink]);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.preview-ready" });

        sink.Events.ShouldContain(runEvent =>
            runEvent.Type == RunEventType.Custom && runEvent.CustomType == "contoso.preview-ready");
    }

    [Fact]
    public async Task A_second_writer_waits_until_the_first_event_has_reached_the_store_and_every_sink()
    {
        // A run writes from more than one place at once (concurrent tool
        // calls, a child run writing into its parent's writer, a workflow's
        // executors next to its event pump). Numbering, the store write and the
        // sink dispatch used to be three separate steps, so a second writer took
        // sequence 1, wrote and dispatched it while the first writer was still
        // inside the store with sequence 0 - and the sink saw 1 before 0,
        // against IRunEventSink's documented "in sequence order".
        var store = new FirstAppendHoldingRunStore();
        var sink = new SpyRunEventSink();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics: null, [sink]);

        var first = writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.first" }).AsTask();
        await store.FirstAppendEntered;

        var second = writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.second" }).AsTask();

        // Deterministic, no timing: the second write's store call and sink call
        // complete synchronously, so if nothing holds it back it is already done.
        second.IsCompleted.ShouldBeFalse("the second event overtook the first one");
        sink.Events.ShouldBeEmpty();

        store.ReleaseFirst();
        await Task.WhenAll(first, second);

        sink.Events.Select(static e => e.Sequence).ShouldBe([0L, 1L]);
        sink.Events.Select(static e => e.CustomType).ShouldBe(["contoso.first", "contoso.second"]);
    }

    [Fact]
    public async Task A_sink_that_appends_to_the_run_it_handles_is_disabled_instead_of_hanging_the_run()
    {
        // Events are written one at a time, so an append from inside a sink would
        // wait for the event the sink is still handling - for ever, since nothing
        // cancels it. The append throws instead; the sink is disabled like any
        // failing sink and the run goes on.
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var sink = new ReentrantRunEventSink();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics: null, [sink]);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        // Armed after the run opened: from here on the sink appends to the run.
        sink.Writer = writer;

        await writer
            .AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.first" })
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.second" });

        sink.Failure.ShouldBeOfType<InvalidOperationException>();
        sink.Calls.ShouldBe(1, "a failing sink is disabled for the rest of the run");
        writer.IsDisabled.ShouldBeFalse();
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
        // The consumer abandons `await foreach` after the first
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
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            sinks: sinks);
    }

    private sealed class FixedTenantContext(string tenantId = "test") : ITenantContext
    {
        public string TenantId => tenantId;
    }

    /// <summary>Records the exact events it receives, in the order it received them.</summary>
    /// <remarks>
    /// Thread-safe: a regression of the writer's ordering must fail on the ORDER,
    /// not as a corrupted list.
    /// </remarks>
    private sealed class SpyRunEventSink : IRunEventSink
    {
        private readonly List<RunEvent> _events = [];

        public IReadOnlyList<RunEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            lock (_events)
            {
                _events.Add(runEvent);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Appends to the run whose event it is handling, then reports what happened.</summary>
    private sealed class ReentrantRunEventSink : IRunEventSink
    {
        public RunEventWriter? Writer { get; set; }

        public Exception? Failure { get; private set; }

        public int Calls { get; private set; }

        public async ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            if (Writer is null)
            {
                return;
            }

            Calls++;

            try
            {
                await Writer.AppendAsync(new RunEventDraft(RunEventType.Custom) { CustomType = "contoso.echo" }, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                Failure = exception;
                throw;
            }
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

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<long?>(null);

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

        public ValueTask<bool> CompleteLateToolInvocationAsync(
            LateToolCompletion completion,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask RecordNonTokenUsageAsync(NonTokenUsageRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<NonTokenUsageRecord>> ListNonTokenUsageAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
            string? tenantId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunCancellationRequest>> TouchHeartbeatAsync(
            IReadOnlyCollection<Guid> runIds,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> RequestCancellationAsync(
            Guid runId,
            string tenantId,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
            DateTimeOffset staleBefore,
            int max,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Holds the FIRST event write open until <see cref="ReleaseFirst"/> is called;
    /// every later write completes at once. That is the moment a second writer
    /// used to overtake the first.
    /// </summary>
    private sealed class FirstAppendHoldingRunStore : IRunStore
    {
        private readonly TaskCompletionSource _firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _appends;

        public Task FirstAppendEntered => _firstEntered.Task;

        public void ReleaseFirst() => _releaseFirst.TrySetResult();

        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RunRecord
            {
                Id = info.RunId,
                AgentName = info.AgentName,
                Status = info.Status,
                StartedAt = info.StartedAt,
            });

        public async ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _appends) == 1)
            {
                _firstEntered.TrySetResult();
                await _releaseFirst.Task.ConfigureAwait(false);
            }
        }

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<long?>(null);

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

        public ValueTask<bool> CompleteLateToolInvocationAsync(
            LateToolCompletion completion,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask RecordNonTokenUsageAsync(NonTokenUsageRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<NonTokenUsageRecord>> ListNonTokenUsageAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
            string? tenantId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunCancellationRequest>> TouchHeartbeatAsync(
            IReadOnlyCollection<Guid> runIds,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> RequestCancellationAsync(
            Guid runId,
            string tenantId,
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
