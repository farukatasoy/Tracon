using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Voice;

/// <summary>
/// The <c>end_conversation</c> tool inside the REAL function-invocation loop, streamed and not: when the tool's call ends the
/// loop and when the model is asked once more.
/// </summary>
/// <remarks>
/// <para>
/// The stop signal and the message that carried the call both belong to the loop: the tool reads them from
/// <see cref="FunctionInvokingChatClient.CurrentContext"/>, which only the loop sets. A stand-in would prove nothing about
/// what the loop shows a tool, least of all in the streamed path, where the loop rebuilds the model's message from the
/// updates it collected.
/// </para>
/// <para>
/// The first group measures what the loop shows a tool; the rule of the tool stands on those facts, and a MEAI upgrade that
/// changes one of them fails here first.
/// </para>
/// </remarks>
public sealed class EndConversationToolTests
{
    private const string Lookup = "lookup";

    // ---- What the loop shows the tool --------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_tool_sees_the_message_that_called_it_with_the_words_that_came_before_the_call(bool streaming)
    {
        var model = new ScriptedModel(Turn(Said("Goodbye, take care."), Call("end-1", Probe.Name)));
        var seen = new Probe();

        await RunAsync(model, streaming, seen.Tool);

        var history = seen.History.ShouldNotBeNull();
        var tail = history[^1];

        tail.Role.ShouldBe(ChatRole.Assistant);
        tail.Text.ShouldBe("Goodbye, take care.");
        tail.Contents.OfType<FunctionCallContent>().ShouldHaveSingleItem().CallId.ShouldBe("end-1");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_text_that_arrives_in_pieces_reaches_the_tool_as_one_message_with_the_whole_text(bool streaming)
    {
        var model = new ScriptedModel(Turn(Said("Good", messageId: "m1"), Said("bye, ", messageId: "m1"), Said("friend.", messageId: "m1"), Call("end-1", Probe.Name, messageId: "m1")));
        var seen = new Probe();

        await RunAsync(model, streaming, seen.Tool);

        var history = seen.History.ShouldNotBeNull();
        var tail = history[^1];

        tail.Text.ShouldBe("Goodbye, friend.");
        tail.Contents.OfType<FunctionCallContent>().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_message_without_an_identity_is_still_one_message_with_its_text_and_its_call(bool streaming)
    {
        // A provider that names no message at all: the loop must not split the text from the call.
        var model = new ScriptedModel(Turn(Said("Goodbye, ", messageId: null), Said("friend.", messageId: null), Call("end-1", Probe.Name, messageId: null)));
        var seen = new Probe();

        await RunAsync(model, streaming, seen.Tool);

        var history = seen.History.ShouldNotBeNull();
        var tail = history[^1];

        tail.Text.ShouldBe("Goodbye, friend.");
        tail.Contents.OfType<FunctionCallContent>().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_text_message_and_a_call_message_of_one_response_reach_the_tool_as_two_assistant_messages(bool streaming)
    {
        // The shape of a provider that returns the words and the call as separate items of one response.
        var model = new ScriptedModel(Turn(Said("Goodbye, friend.", messageId: "msg-1"), Call("end-1", Probe.Name, messageId: "call-1")));
        var seen = new Probe();

        await RunAsync(model, streaming, seen.Tool);

        var history = seen.History.ShouldNotBeNull();

        history[^1].Role.ShouldBe(ChatRole.Assistant);
        history[^1].Contents.OfType<FunctionCallContent>().ShouldHaveSingleItem();
        history[^2].Role.ShouldBe(ChatRole.Assistant);
        history[^2].Text.ShouldBe("Goodbye, friend.");
        history[^3].Role.ShouldBe(ChatRole.User);
    }

    // ---- The rule ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Words_before_the_call_in_the_same_message_end_the_loop_and_the_model_is_not_asked_again(bool streaming)
    {
        var model = new ScriptedModel(Turn(Said("Goodbye, take care."), Call("end-1")));

        var outcome = await RunAsync(model, streaming);

        outcome.EndRequested.ShouldBeTrue();
        model.Requests.ShouldBe(1, "the words were said before the call, so nothing is left to ask the model for");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Words_streamed_in_pieces_before_the_call_end_the_loop(bool streaming)
    {
        var model = new ScriptedModel(Turn(Said("Good", messageId: "m1"), Said("bye.", messageId: "m1"), Call("end-1", messageId: "m1")));

        var outcome = await RunAsync(model, streaming);

        outcome.EndRequested.ShouldBeTrue();
        model.Requests.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Words_in_a_message_of_their_own_before_the_call_end_the_loop(bool streaming)
    {
        var model = new ScriptedModel(Turn(Said("Goodbye, friend.", messageId: "msg-1"), Call("end-1", messageId: "call-1")));

        var outcome = await RunAsync(model, streaming);

        outcome.EndRequested.ShouldBeTrue();
        model.Requests.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, " \n\t ")]
    [InlineData(true, "")]
    [InlineData(true, " \n\t ")]
    public async Task A_call_with_no_words_before_it_lets_the_model_say_goodbye_next(bool streaming, string blank)
    {
        var model = new ScriptedModel(
            Turn(Said(blank), Call("end-1")),
            Turn(Said("Goodbye, then.")));

        var outcome = await RunAsync(model, streaming);

        outcome.EndRequested.ShouldBeTrue("the conversation still ends after the answer the model gives next");
        model.Requests.ShouldBe(2, "a goodbye that was not said is still to come");
        outcome.Text.ShouldContain("Goodbye, then.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_that_is_the_whole_message_lets_the_model_say_goodbye_next(bool streaming)
    {
        var model = new ScriptedModel(
            Turn(Call("end-1")),
            Turn(Said("Goodbye, then.")));

        var outcome = await RunAsync(model, streaming);

        outcome.EndRequested.ShouldBeTrue();
        model.Requests.ShouldBe(2);
        outcome.Text.ShouldContain("Goodbye, then.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_is_not_a_conversation_is_not_cut_short_whatever_the_model_said(bool streaming)
    {
        var model = new ScriptedModel(
            Turn(Said("Goodbye, take care."), Call("end-1")),
            Turn(Said("Still here.")));

        var outcome = await RunAsync(model, streaming, conversation: false);

        outcome.EndRequested.ShouldBeFalse();
        model.Requests.ShouldBe(2, "nothing ended, so the model gets its next turn like after any tool");
        outcome.Text.ShouldContain("Still here.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Words_said_before_an_earlier_tool_are_not_a_goodbye(bool streaming)
    {
        var lookups = 0;
        var lookup = AIFunctionFactory.Create(() => ++lookups, Lookup);

        var model = new ScriptedModel(
            Turn(Said("One moment, I will check."), Call("lookup-1", Lookup)),
            Turn(Call("end-1")),
            Turn(Said("Goodbye, then.")));

        var outcome = await RunAsync(model, streaming, lookup);

        lookups.ShouldBe(1);
        outcome.EndRequested.ShouldBeTrue();
        model.Requests.ShouldBe(3, "the words of the first turn announced a lookup; the goodbye was still to be said");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_that_ends_its_turn_after_other_calls_ends_the_loop_when_the_goodbye_was_said(bool streaming)
    {
        var lookups = 0;
        var lookup = AIFunctionFactory.Create(() => ++lookups, Lookup);

        var model = new ScriptedModel(Turn(Said("Goodbye, friend."), Call("lookup-1", Lookup), Call("end-1")));

        var outcome = await RunAsync(model, streaming, lookup);

        lookups.ShouldBe(1);
        outcome.EndRequested.ShouldBeTrue();
        model.Requests.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_with_other_calls_behind_it_does_not_cut_them_off(bool streaming)
    {
        // The loop runs the calls of a turn one after the other and drops the ones behind a call that stops it: they would
        // never run and the history would hold calls with no result. The loop goes on instead.
        var lookups = 0;
        var lookup = AIFunctionFactory.Create(() => ++lookups, Lookup);

        var model = new ScriptedModel(
            Turn(Said("Goodbye, friend."), Call("end-1"), Call("lookup-1", Lookup)),
            Turn(Said("Done.")));

        var outcome = await RunAsync(model, streaming, lookup);

        lookups.ShouldBe(1, "the call behind the end call ran");
        outcome.EndRequested.ShouldBeTrue();
        model.Requests.ShouldBe(2);
    }

    [Fact]
    public async Task A_call_made_outside_the_loop_asks_for_the_end_and_changes_nothing_else()
    {
        var registry = new VoiceConversationEndRegistry();
        var runId = Guid.NewGuid();
        var requested = false;
        using var lease = registry.Register(runId, () => requested = true);

        TraconRunContext.SetCurrent(Scope(runId));

        try
        {
            var result = await new EndConversationTool(registry).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), TestContext.Current.CancellationToken);

            result.ShouldNotBeNull().ToString().ShouldBe(EndConversationTool.EndingText);
            requested.ShouldBeTrue();
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public void The_description_tells_the_model_to_say_goodbye_before_it_calls_the_tool()
    {
        var description = new EndConversationTool(new VoiceConversationEndRegistry()).Description;

        description.ShouldContain("goodbye first", Case.Insensitive);
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------

    private static async Task<Outcome> RunAsync(
        ScriptedModel model,
        bool streaming,
        AIFunction? extra = null,
        bool conversation = true)
    {
        var registry = new VoiceConversationEndRegistry();
        var runId = Guid.NewGuid();
        var requested = false;

        // The registration and the scope are made in this method's own body: the loop that runs the tool below is in the
        // same flow, so the tool finds both.
        using var lease = conversation ? registry.Register(runId, () => requested = true) : null;

        TraconRunContext.SetCurrent(Scope(runId));

        try
        {
            using var loop = new FunctionInvokingChatClient(model);

            var tools = new List<AITool> { new EndConversationTool(registry) };

            if (extra is not null)
            {
                tools.Add(extra);
            }

            var options = new ChatOptions { Tools = tools };

            string text;

            if (streaming)
            {
                var builder = new System.Text.StringBuilder();

                await foreach (var update in loop.GetStreamingResponseAsync("go", options, TestContext.Current.CancellationToken))
                {
                    builder.Append(update.Text);
                }

                text = builder.ToString();
            }
            else
            {
                text = (await loop.GetResponseAsync("go", options, TestContext.Current.CancellationToken)).Text;
            }

            return new Outcome(requested, text);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private static AgentRunScope Scope(Guid runId) => new() { RunId = runId, RootRunId = runId };

    private static ChatResponseUpdate Said(string text, string? messageId = "m1")
        => new(ChatRole.Assistant, text) { MessageId = messageId };

    private static ChatResponseUpdate Call(string callId, string tool = EndConversationTool.ToolName, string? messageId = "m1")
        => new(ChatRole.Assistant, [new FunctionCallContent(callId, tool, new Dictionary<string, object?>(StringComparer.Ordinal))]) { MessageId = messageId };

    private static ChatResponseUpdate[] Turn(params ChatResponseUpdate[] updates) => updates;

    private sealed record Outcome(bool EndRequested, string Text);

    /// <summary>A tool that keeps the history the loop shows it.</summary>
    private sealed class Probe
    {
        public const string Name = "probe";

        public Probe() => Tool = AIFunctionFactory.Create(
            () =>
            {
                History = [.. FunctionInvokingChatClient.CurrentContext!.Messages];

                return "ok";
            },
            Name);

        public AIFunction Tool { get; }

        public ChatMessage[]? History { get; private set; }
    }

    /// <summary>
    /// A model that answers each request with the next scripted turn, as the updates a provider would stream; a request
    /// past the script gets a plain answer.
    /// </summary>
    private sealed class ScriptedModel(params ChatResponseUpdate[][] turns) : IChatClient
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(NextTurn().ToChatResponse());

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in NextTurn())
            {
                await Task.Yield();

                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private ChatResponseUpdate[] NextTurn()
        {
            var index = Interlocked.Increment(ref _requests) - 1;

            return index < turns.Length ? turns[index] : [new ChatResponseUpdate(ChatRole.Assistant, "Fine.")];
        }
    }
}
