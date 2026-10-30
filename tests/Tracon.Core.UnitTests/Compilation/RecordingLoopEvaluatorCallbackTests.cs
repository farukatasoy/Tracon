using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// What <see cref="RecordingLoopEvaluator"/> hands the callback of the run's scope
/// (<see cref="TraconRunOptions.AfterLoopIterationRecorded"/>, the source of the native stream's
/// <c>loop.iteration-completed</c> frame): the event the run recorded, never one it could not.
/// </summary>
/// <remarks>
/// The evaluator is driven directly, with a <see cref="LoopContext"/> built here; the order of the frame among
/// the stream's updates is a property of the real host and is pinned by <c>LoopIterationFrameTests</c>.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only"
#pragma warning disable MAAI001
public sealed class RecordingLoopEvaluatorCallbackTests
{
    [Fact]
    public async Task The_callback_gets_the_event_the_store_serves()
    {
        await using var rig = await Rig.StartAsync();

        await rig.EvaluateAsync();

        var handed = rig.Handed.ShouldHaveSingleItem();
        var stored = await rig.StoredLoopEventsAsync();

        handed.Type.ShouldBe(RunEventType.LoopIterationCompleted);
        stored.ShouldHaveSingleItem().ShouldBe(handed);
    }

    [Fact]
    public async Task A_recorded_timestamp_has_whole_microseconds()
    {
        await using var rig = await Rig.StartAsync();

        for (var iteration = 0; iteration < 20; iteration++)
        {
            await rig.EvaluateAsync();
        }

        rig.Handed.Count.ShouldBe(20);
        rig.Handed.ShouldAllBe(static handed => handed.Timestamp.UtcTicks % 10 == 0);
    }

    [Fact]
    public async Task A_store_that_refuses_the_event_hands_nothing_over_and_the_loop_goes_on()
    {
        await using var rig = await Rig.StartAsync(failEventAppends: true);

        var evaluation = await rig.EvaluateAsync();

        // The run does not stop for a store that is down, and the stream gets no frame
        // /events could never serve.
        evaluation.ShouldReinvoke.ShouldBeTrue();
        rig.Handed.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_without_an_event_writer_hands_nothing_over()
    {
        await using var rig = await Rig.StartAsync(withWriter: false);

        var evaluation = await rig.EvaluateAsync();

        evaluation.ShouldReinvoke.ShouldBeTrue();
        rig.Handed.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_payloads_off_the_event_and_so_the_callbacks_copy_carry_no_payload()
    {
        await using var rig = await Rig.StartAsync(recordToolPayloads: false);

        await rig.EvaluateAsync();

        var handed = rig.Handed.ShouldHaveSingleItem();

        handed.Payload.ShouldBeNull();
        handed.Text.ShouldNotBeNull().ShouldEndWith("continued by 'always'");
    }

    [Fact]
    public async Task A_failing_callback_is_not_a_criterion_failure_and_the_event_stays_clean()
    {
        // ReturnLastResponse swallows a criterion that throws. The callback is not a criterion:
        // its failure must reach the caller, and the recorded event must not name a failed criterion.
        await using var rig = await Rig.StartAsync(callbackFailure: new InvalidOperationException("the stream is gone"));

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () => await rig.EvaluateAsync());

        failure.Message.ShouldBe("the stream is gone");

        var stored = (await rig.StoredLoopEventsAsync()).ShouldHaveSingleItem();

        System.Text.Json.JsonDocument.Parse(stored.Payload!).RootElement.GetProperty("failedCriterion").ValueKind
            .ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly InMemoryRunStore _store;
        private readonly RecordingLoopEvaluator _evaluator = new(
            "looper",
            [new LoopCriterionBinding("always", new DelegateLoopEvaluator((_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("more"))))],
            maxIterations: 50,
            LoopOutcome.ReturnLastResponse,
            LoopOutcome.ReturnLastResponse,
            NullLogger.Instance);
        private readonly RunEventWriter _writer;
        private AgentRunScope? _scope;
        private readonly LoopContext _context = new(
            new NoopAgent(),
            new NoopSession(),
            [new ChatMessage(ChatRole.User, "go")],
            new AgentResponse(new ChatMessage(ChatRole.Assistant, "a draft")),
            new AgentRunOptions());

        private Rig(InMemoryRunStore store, RunEventWriter writer)
        {
            _store = store;
            _writer = writer;
        }

        public List<RunEvent> Handed { get; } = [];

        public static async Task<Rig> StartAsync(
            bool failEventAppends = false,
            bool withWriter = true,
            bool recordToolPayloads = true,
            Exception? callbackFailure = null)
        {
            var inner = new InMemoryRunStore();
            var faulty = new FaultyRunStore(inner);
            var writer = new RunEventWriter(
                faulty,
                new TraconRunRecordingOptions { RecordToolPayloads = recordToolPayloads },
                NullLogger.Instance,
                TraconId.NewId(),
                metrics: null);

            await writer.StartAsync(
                new RunStartInfo { RunId = writer.RunId, AgentName = "looper", StartedAt = DateTimeOffset.UtcNow },
                query: "go");

            faulty.FailEventAppends = failEventAppends;

            var rig = new Rig(inner, writer);

            rig._scope = new AgentRunScope
            {
                RunId = writer.RunId,
                RootRunId = writer.RunId,
                Writer = withWriter ? writer : null,
                ExtraUsage = new SideChannelUsageAccumulator(),
                AfterLoopIterationRecorded = (runEvent, _) =>
                {
                    rig.Handed.Add(runEvent);

                    return callbackFailure is null ? ValueTask.CompletedTask : throw callbackFailure;
                },
            };

            return rig;
        }

        public async ValueTask<LoopEvaluation> EvaluateAsync()
        {
            // 🚨 Set here, in the body that awaits the evaluator: a scope set inside StartAsync would not
            // flow back to this caller (an AsyncLocal assignment never does), and the callback would silently
            // never run - which the two "hands nothing over" cases would read as success.
            TraconRunContext.SetCurrent(_scope);

            return await _evaluator.EvaluateAsync(_context);
        }

        public async Task<List<RunEvent>> StoredLoopEventsAsync()
        {
            var events = new List<RunEvent>();

            await foreach (var runEvent in _store.ReadEventsAsync(_writer.RunId))
            {
                if (runEvent.Type == RunEventType.LoopIterationCompleted)
                {
                    events.Add(runEvent);
                }
            }

            return events;
        }

        public ValueTask DisposeAsync()
        {
            TraconRunContext.SetCurrent(null);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopSession : AgentSession;

    private sealed class NoopAgent : AIAgent
    {
        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            System.Text.Json.JsonElement serializedState,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<System.Text.Json.JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
#pragma warning restore MAAI001
