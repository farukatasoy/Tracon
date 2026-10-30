using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The run recorder's close step for the tool calls the tool chain recorded, measured through the real
/// pipeline the compiler assembles: a run that does not stream and fails after a tool turn keeps its calls, in the
/// order they ran, whatever the clock and the store do while it closes.
/// </summary>
public sealed class RunRecordingAgentLedgerTests
{
    private const string ToolName = "lookup";
    private const string Counter = TraconDiagnostics.RunRecordingFailureCounterName;

    [Fact]
    public async Task A_run_that_fails_after_a_tool_turn_keeps_the_row_and_the_events_of_the_call_it_made()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store, AsksForTheToolThenFails(), AIFunctionFactory.Create(() => "sunny", ToolName));

        var failure = await Should.ThrowAsync<TraconException>(async () => await agent.RunAsync("go"));

        failure.ErrorType.ShouldBe("upstream_error");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var row = (await store.ListToolInvocationsAsync(run.Id)).ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Failed);
        row.ToolName.ShouldBe(ToolName);
        row.ToolCallId.ShouldBe("call-1");
        row.Succeeded.ShouldBeTrue();
        row.Result.ShouldNotBeNull().ShouldContain("sunny", Case.Sensitive);
        row.Duration.ShouldNotBeNull();

        var events = (await ReadEventsAsync(store, run.Id))
            .Where(static e => e.Type is RunEventType.ToolInvoking or RunEventType.ToolInvoked)
            .ToList();

        events.Select(static e => e.Type).ShouldBe([RunEventType.ToolInvoking, RunEventType.ToolInvoked]);
        events.ShouldAllBe(static e => string.Equals(e.ToolCallId, "call-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_wall_clock_corrected_backwards_while_the_tool_runs_still_writes_the_start_before_the_end()
    {
        // The end of a call must not read earlier than its start: the close step orders the two events by time, and
        // an end written first would find no call to close (a row named "unknown", and the call closed a second time
        // as unfinished).
        var clock = new ManualTimeProvider();
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);

        var tool = AIFunctionFactory.Create(
            () =>
            {
                clock.Advance(TimeSpan.FromSeconds(-30));

                return "sunny";
            },
            ToolName);

        var agent = CreateAgent(store, AsksForTheToolThenFails(beforeFailing: () => clock.Advance(TimeSpan.FromSeconds(30))), tool, clock);

        await Should.ThrowAsync<TraconException>(async () => await agent.RunAsync("go"));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var row = (await store.ListToolInvocationsAsync(run.Id)).ShouldHaveSingleItem();

        row.ToolName.ShouldBe(ToolName);
        row.Succeeded.ShouldBeTrue();

        (await ReadEventsAsync(store, run.Id))
            .Where(static e => e.Type is RunEventType.ToolInvoking or RunEventType.ToolInvoked)
            .Select(static e => e.Type)
            .ShouldBe([RunEventType.ToolInvoking, RunEventType.ToolInvoked]);
    }

    [Fact]
    public async Task A_store_that_refuses_the_events_while_the_calls_are_written_does_not_change_the_failure_the_caller_gets()
    {
        // Observability must not break functionality: the model failed, so the caller gets the model's failure, and
        // the lost record is counted once at its own stage.
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var store = new FaultyRunStore(new InMemoryRunStore(tenantContext: FixedTenantContext.Default));
        var ran = 0;

        var agent = CreateAgent(
            store,
            AsksForTheToolThenFails(beforeFailing: () => store.FailEventAppends = true),
            AIFunctionFactory.Create(() => { ran++; return "sunny"; }, ToolName),
            metrics: metrics);

        var failure = await Should.ThrowAsync<TraconException>(async () => await agent.RunAsync("go"));

        failure.ErrorType.ShouldBe("upstream_error");
        ran.ShouldBe(1);

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.Event);
    }

    [Fact]
    public async Task A_store_that_refuses_the_tool_row_is_counted_once_and_the_run_still_closes_with_the_models_failure()
    {
        // The row of a call is a store write of its own, apart from the events. The store refuses it while the close
        // step writes the calls the chain ran. The writer counts the loss once, at the stage of the row, and stops
        // writing to the store, which is its rule after a failure (so the run row stays open, as for any store
        // failure). The caller still gets the model's failure, and the closing event still reaches a sink.
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var store = new FaultyRunStore(new InMemoryRunStore(tenantContext: FixedTenantContext.Default));
        var sink = new CollectingSink();

        var agent = CreateAgent(
            store,
            AsksForTheToolThenFails(beforeFailing: () => store.FailToolInvocations = true),
            AIFunctionFactory.Create(() => "sunny", ToolName),
            metrics: metrics,
            sinks: [sink]);

        var failure = await Should.ThrowAsync<TraconException>(async () => await agent.RunAsync("go"));

        failure.ErrorType.ShouldBe("upstream_error");

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.ToolInvocation);

        sink.Events[^1].Type.ShouldBe(RunEventType.RunFailed);
    }

    [Fact]
    public async Task A_tool_argument_that_cannot_be_printed_does_not_escape_the_close_step_and_the_run_still_closes()
    {
        // 🚨 The close step formats the call's arguments to write them, and a value's ToString is code the model's
        // answer and a tool's caller control. Whatever it throws must stay in the close step: counted and logged, never
        // thrown out of CompleteAsync. Escaping would leave the run open in the store and hand the caller the
        // formatter's exception in place of the model's failure.
        using var factory = new TestMeterFactory();
        using var collector = new MetricCollector(factory.Meter);
        using var metrics = new TraconMetrics(factory, TenantTaggedMetricsOptions.Create());

        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var ran = 0;

        var agent = CreateAgent(
            store,
            AsksForTheToolThenFails(arguments: new Dictionary<string, object?>(StringComparer.Ordinal) { ["order"] = new Unprintable() }),
            AIFunctionFactory.Create(() => { ran++; return "sunny"; }, ToolName),
            metrics: metrics);

        var failure = await Should.ThrowAsync<TraconException>(async () => await agent.RunAsync("go"));

        failure.ErrorType.ShouldBe("upstream_error");
        ran.ShouldBe(1);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Failed, "the run was left open by the close step");

        collector.LongTags(Counter)
            .ShouldHaveSingleItem()[TraconDiagnostics.Tags.RecordingStage]
            .ShouldBe(RunRecordingStages.ToolInvocation);
    }

    /// <summary>
    /// A model that asks for the tool on its first turn and fails on its second, the way a provider outage does after
    /// a tool turn. <paramref name="beforeFailing"/> runs just before the second turn fails.
    /// </summary>
    private static FakeChatClient AsksForTheToolThenFails(Action? beforeFailing = null, IDictionary<string, object?>? arguments = null)
    {
        var turn = 0;

        return new FakeChatClient(_ =>
        {
            if (++turn == 1)
            {
                return new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call-1", ToolName, arguments ?? new Dictionary<string, object?>(StringComparer.Ordinal))]));
            }

            beforeFailing?.Invoke();

            throw new InvalidOperationException("model crashed");
        });
    }

    private static RunRecordingAgent CreateAgent(
        IRunStore store,
        FakeChatClient client,
        AIFunction tool,
        TimeProvider? clock = null,
        TraconMetrics? metrics = null,
        IReadOnlyList<IRunEventSink>? sinks = null)
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider(client)), TestData.Registry(tool));

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition(toolNames: [ToolName])),
            store,
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            timeProvider: clock,
            sinks: sinks);
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

    /// <summary>A value that cannot be turned into text, the way a foreign object's formatter may fail.</summary>
    private sealed class Unprintable
    {
        public override string ToString() => throw new InvalidOperationException("this value cannot be printed");
    }

    /// <summary>Keeps every event it receives, in order.</summary>
    private sealed class CollectingSink : IRunEventSink
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
}
