using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A turn that ends with the call of a client-side tool leaves a call without a result in the history. These tests run
/// the run recorder around a stand-in agent and read what it leaves with the session after the turn and what it gives
/// the next turn before its own messages.
/// </summary>
/// <remarks>
/// The recorder sits around every agent of every entry point (HTTP, AG-UI, embedded), so this is the one place the rule
/// is written; the functional tests run it through a real provider that refuses a history with an unanswered call.
/// </remarks>
public sealed class UnansweredClientCallTests
{
    private const string MarkerKey = ToolInputSessionBridge.SkippedCallsKey;

    private static readonly FunctionCallContent ClientCall = new("call-client", "open_page");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_turn_that_ends_with_a_client_call_leaves_the_call_with_the_session(bool streaming)
    {
        var rig = Rig.Create(Turn([new TextContent("Opening it."), ClientCall]));
        var session = new StubSession();

        await rig.RunAsync(streaming, [new ChatMessage(ChatRole.User, "open the page")], session);

        MarkedCalls(session).ShouldBe(["call-client"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_the_turn_answered_itself_is_not_left_behind(bool streaming)
    {
        var rig = Rig.Create(Turn(
            [new FunctionCallContent("call-server", "lookup_order")],
            [new FunctionResultContent("call-server", "order 7 shipped")],
            [new TextContent("Order 7 shipped.")]));
        var session = new StubSession();

        await rig.RunAsync(streaming, [new ChatMessage(ChatRole.User, "where is order 7")], session);

        MarkedCalls(session).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_that_waits_for_an_approval_is_not_left_behind(bool streaming)
    {
        // The approval answers it; closing it as unanswered would run it never.
        var call = new FunctionCallContent("call-approval", "cancel_order");
        var rig = Rig.Create(Turn([new ToolApprovalRequestContent("request-1", call)]));
        var session = new StubSession();

        await rig.RunAsync(streaming, [new ChatMessage(ChatRole.User, "cancel it")], session);

        MarkedCalls(session).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_user_message_closes_the_call_before_its_own_messages(bool streaming)
    {
        var rig = Rig.Create(Turn([ClientCall]), Turn([new TextContent("Fine.")]));
        var session = new StubSession();

        await rig.RunAsync(streaming, [new ChatMessage(ChatRole.User, "open the page")], session);
        await rig.RunAsync(streaming, [new ChatMessage(ChatRole.User, "never mind")], session);

        var second = rig.Inner.Received[1];
        second.Count.ShouldBe(2);
        second[0].Role.ShouldBe(ChatRole.Tool);
        var closing = second[0].Contents.OfType<FunctionResultContent>().ShouldHaveSingleItem();
        closing.CallId.ShouldBe("call-client");
        closing.Result.ShouldBe("The call was not answered.");
        second[1].Text.ShouldBe("never mind");
        MarkedCalls(session).ShouldBeEmpty("the marker is taken by the run that closes the call");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_brings_the_result_does_not_close_the_call_it_answers(bool streaming)
    {
        var rig = Rig.Create(Turn([ClientCall]), Turn([new TextContent("Opened.")]));
        var session = new StubSession();

        await rig.RunAsync(streaming, [new ChatMessage(ChatRole.User, "open the page")], session);

        var result = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-client", "Shopping cart")]);
        await rig.RunAsync(streaming, [result], session);

        rig.Inner.Received[1].ShouldHaveSingleItem().ShouldBeSameAs(result);
        MarkedCalls(session).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_answers_one_of_two_calls_closes_only_the_other(bool streaming)
    {
        var rig = Rig.Create(
            Turn([ClientCall, new FunctionCallContent("call-client-2", "pick_file")]),
            Turn([new TextContent("Done.")]));
        var session = new StubSession();

        await rig.RunAsync(streaming, [new ChatMessage(ChatRole.User, "open and pick")], session);
        MarkedCalls(session).Order(StringComparer.Ordinal).ShouldBe(["call-client", "call-client-2"]);

        var result = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-client", "Shopping cart")]);
        await rig.RunAsync(streaming, [result], session);

        var second = rig.Inner.Received[1];
        second.Count.ShouldBe(2);
        second[0].Contents.OfType<FunctionResultContent>().ShouldHaveSingleItem().CallId.ShouldBe("call-client-2");
        second[1].ShouldBeSameAs(result);
    }

    [Fact]
    public async Task A_turn_that_asks_again_after_the_answer_leaves_only_the_new_call()
    {
        var rig = Rig.Create(
            Turn([ClientCall]),
            Turn([new FunctionCallContent("call-client-2", "pick_file")]));
        var session = new StubSession();

        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "go")], session);
        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-client", "x")])], session);

        MarkedCalls(session).ShouldBe(["call-client-2"]);
    }

    [Fact]
    public async Task A_call_left_by_two_turns_in_a_row_is_closed_once()
    {
        var rig = Rig.Create(Turn([ClientCall]), Turn([new TextContent("Fine.")]), Turn([new TextContent("Still fine.")]));
        var session = new StubSession();

        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "go")], session);
        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "never mind")], session);
        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "and again")], session);

        rig.Inner.Received[1].Count.ShouldBe(2);
        rig.Inner.Received[2].ShouldHaveSingleItem().Text.ShouldBe("and again");
    }

    [Fact]
    public async Task Another_session_is_not_touched()
    {
        var rig = Rig.Create(Turn([ClientCall]), Turn([new TextContent("Fine.")]));
        var first = new StubSession();
        var other = new StubSession();

        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "go")], first);
        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "hello")], other);

        rig.Inner.Received[1].ShouldHaveSingleItem().Text.ShouldBe("hello");
        MarkedCalls(first).ShouldBe(["call-client"]);
    }

    [Fact]
    public async Task A_run_without_a_session_neither_records_nor_closes_anything()
    {
        var rig = Rig.Create(Turn([ClientCall]), Turn([new TextContent("Fine.")]));

        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "go")], session: null);
        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "again")], session: null);

        rig.Inner.Received[1].ShouldHaveSingleItem().Text.ShouldBe("again");
    }

    [Fact]
    public async Task The_marker_survives_the_session_being_written_out_and_read_back()
    {
        // A SQL session store keeps the JSON of the state bag, not the object.
        var rig = Rig.Create(Turn([ClientCall]), Turn([new TextContent("Fine.")]));
        var session = new StubSession();

        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "go")], session);

        var restored = new StubSession(AgentSessionStateBag.Deserialize(session.StateBag.Serialize()));

        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "never mind")], restored);

        rig.Inner.Received[1][0].Contents.OfType<FunctionResultContent>().ShouldHaveSingleItem().CallId.ShouldBe("call-client");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_state_bag_that_cannot_take_the_write_leaves_the_run_completed_and_the_answer_untouched(bool streaming)
    {
        // While it runs, the stand-in swaps the session's state bag for one read back from storage with the wrong shape under
        // the marker's key, so reading the marker to add to it fails after the model has answered.
        var rig = Rig.Create(Turn([new TextContent("Opening it."), ClientCall]));
        rig.Inner.DuringRun = static session => ((StubSession)session!).ReplaceStateBagWithOneThatCannotBeRead();
        var session = new StubSession();

        var texts = await rig.RunAsync(streaming, [new ChatMessage(ChatRole.User, "open the page")], session);

        var run = (await rig.Store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
        run.Error.ShouldBeNull();
        texts.ShouldContain("Opening it.");
        rig.Logger.Entries.ShouldContain(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("unanswered tool call", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lazy_sequence_of_messages_reaches_the_agent_whole_when_a_call_is_closed()
    {
        var rig = Rig.Create(Turn([ClientCall]), Turn([new TextContent("Fine.")]));
        var session = new StubSession();
        var reads = 0;

        IEnumerable<ChatMessage> Lazy()
        {
            reads++;
            yield return new ChatMessage(ChatRole.User, "first");
            yield return new ChatMessage(ChatRole.User, "second");
        }

        await rig.RunAsync(streaming: false, [new ChatMessage(ChatRole.User, "go")], session);
        await rig.Agent.RunAsync(Lazy(), session, cancellationToken: TestContext.Current.CancellationToken);

        rig.Inner.Received[1].Select(static message => message.Text).ShouldBe([string.Empty, "first", "second"]);
        reads.ShouldBeGreaterThan(0);
    }

    // --- Doubles ----------------------------------------------------------------------------------------------------

    private static List<string> MarkedCalls(AgentSession session)
        => session.StateBag.TryGetValue<List<string>>(MarkerKey, out var calls, TraconCoreJsonContext.Default.Options) && calls is not null ? calls : [];

    /// <summary>What a turn of the stand-in agent answers: one assistant or tool message per group of contents.</summary>
    private static IReadOnlyList<ChatMessage> Turn(params AIContent[][] messages)
        => [.. messages.Select(static contents => new ChatMessage(contents.OfType<FunctionResultContent>().Any() ? ChatRole.Tool : ChatRole.Assistant, [.. contents]))];

    private sealed class Rig
    {
        private Rig(StubAgent inner, InMemoryRunStore store, CapturingLogger logger)
        {
            Inner = inner;
            Store = store;
            Logger = logger;
            Agent = TestRunRecording.Agent(inner, store, new FixedTenant(), new TraconRunRecordingOptions(), logger);
        }

        public StubAgent Inner { get; }

        public InMemoryRunStore Store { get; }

        public CapturingLogger Logger { get; }

        public RunRecordingAgent Agent { get; }

        public static Rig Create(params IReadOnlyList<ChatMessage>[] turns)
        {
            var tenant = new FixedTenant();

            return new Rig(new StubAgent(turns), new InMemoryRunStore(tenantContext: tenant), new CapturingLogger());
        }

        /// <summary>Runs one turn and returns the text the caller received.</summary>
        public async Task<string> RunAsync(bool streaming, List<ChatMessage> messages, AgentSession? session)
        {
            if (!streaming)
            {
                return (await Agent.RunAsync(messages, session, cancellationToken: TestContext.Current.CancellationToken)).Text;
            }

            var text = new System.Text.StringBuilder();

            await foreach (var update in Agent.RunStreamingAsync(messages, session, cancellationToken: TestContext.Current.CancellationToken))
            {
                text.Append(update.Text);
            }

            return text.ToString();
        }
    }

    private sealed class FixedTenant : ITenantContext
    {
        public string TenantId => "test";
    }

    /// <summary>An agent that answers from a script and records what it was given.</summary>
    private sealed class StubAgent(IReadOnlyList<IReadOnlyList<ChatMessage>> turns) : AIAgent
    {
        private int _turn;

        public override string Name => "stub";

        public List<IReadOnlyList<ChatMessage>> Received { get; } = [];

        /// <summary>Runs while a turn is answered, with the turn's session.</summary>
        public Action<AgentSession?>? DuringRun { get; set; }

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResponse([.. Answer(messages, session)]));

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            foreach (var message in Answer(messages, session))
            {
                yield return new AgentResponseUpdate(message.Role, [.. message.Contents]);
            }
        }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new StubSession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new StubSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(JsonDocument.Parse("{}").RootElement.Clone());

        private IReadOnlyList<ChatMessage> Answer(IEnumerable<ChatMessage> messages, AgentSession? session)
        {
            Received.Add([.. messages]);
            DuringRun?.Invoke(session);

            return turns[Math.Min(_turn++, turns.Count - 1)];
        }
    }

    private sealed class StubSession : AgentSession
    {
        public StubSession()
        {
        }

        public StubSession(AgentSessionStateBag stateBag)
            : base(stateBag)
        {
        }

        public void ReplaceStateBagWithOneThatCannotBeRead()
        {
            var written = new AgentSessionStateBag();
            written.SetValue(MarkerKey, new Dictionary<string, int>(StringComparer.Ordinal) { ["unexpected"] = 1 }, AIJsonUtilities.DefaultOptions);

            StateBag = AgentSessionStateBag.Deserialize(written.Serialize());
        }
    }

    private sealed class CapturingLogger : ILogger<RunRecordingAgent>
    {
        private readonly Lock _gate = new();
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
