using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// What the ring under the tool-call loop does with one model response: it takes the client calls out of a response
/// that also asks for a server tool, and leaves every other response alone.
/// </summary>
/// <remarks>
/// The ring is the only place that sees the whole answer before the loop does. The functional tests run it through the
/// real pipeline against a strict provider; here are the rules that decide which calls go and which objects stay.
/// </remarks>
public sealed class MixedCallWithholdingChatClientTests
{
    private const string Server = "lookup_order";
    private const string SecondServer = "lookup_customer";
    private const string Client = "open_page";
    private const string SecondClient = "pick_file";

    private static readonly JsonElement EmptySchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

    private static AIFunction ServerTool(string name = Server) => AIFunctionFactory.Create(() => "ok", name);

    private static AIFunctionDeclaration ClientTool(string name = Client) => AIFunctionFactory.CreateDeclaration(name, "Runs on the client.", EmptySchema);

    private static ChatOptions Both() => new() { Tools = [ServerTool(), ServerTool(SecondServer), ClientTool(), ClientTool(SecondClient)] };

    private static FunctionCallContent Call(string id, string name) => new(id, name);

    private static ChatResponse Answer(params AIContent[] contents)
        => new(new ChatMessage(ChatRole.Assistant, [.. contents])) { ResponseId = "resp-1", ModelId = "model-1", FinishReason = ChatFinishReason.ToolCalls };

    private static IReadOnlyList<string> CallIds(ChatResponse response)
        => [.. response.Messages.SelectMany(static message => message.Contents).OfType<FunctionCallContent>().Select(static call => call.CallId)];

    private static IReadOnlyList<string> CallIds(IEnumerable<ChatResponseUpdate> updates)
        => [.. updates.SelectMany(static update => update.Contents).OfType<FunctionCallContent>().Select(static call => call.CallId)];

    private static async Task<List<ChatResponseUpdate>> DrainAsync(MixedCallWithholdingChatClient ring, ChatOptions? options)
    {
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in ring.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "go")], options, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        return updates;
    }

    private static Task<ChatResponse> AskAsync(MixedCallWithholdingChatClient ring, ChatOptions? options)
        => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], options, TestContext.Current.CancellationToken);

    // --- Buffered ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_response_that_asks_for_a_server_tool_and_a_client_tool_loses_the_client_call()
    {
        var ring = new MixedCallWithholdingChatClient(new ScriptedClient(Answer(Call("s1", Server), Call("c1", Client))));

        var response = await AskAsync(ring, Both());

        CallIds(response).ShouldBe(["s1"]);
    }

    [Fact]
    public async Task Every_client_call_of_a_mixed_response_goes_and_every_server_call_stays_in_order()
    {
        var ring = new MixedCallWithholdingChatClient(new ScriptedClient(
            Answer(Call("c1", Client), Call("s1", Server), Call("c2", SecondClient), Call("s2", SecondServer))));

        var response = await AskAsync(ring, Both());

        CallIds(response).ShouldBe(["s1", "s2"]);
    }

    [Fact]
    public async Task What_else_the_mixed_response_holds_stays_and_what_it_reports_is_kept()
    {
        var usage = new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3 };
        var inner = Answer(new TextContent("Looking that up."), Call("s1", Server), Call("c1", Client));
        inner.Usage = usage;
        inner.CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        inner.ConversationId = "conversation-1";
        inner.AdditionalProperties = new AdditionalPropertiesDictionary { ["k"] = "v" };
        inner.Messages[0].MessageId = "message-1";
        inner.Messages[0].AuthorName = "assistant-name";

        var response = await AskAsync(new MixedCallWithholdingChatClient(new ScriptedClient(inner)), Both());

        response.Messages.ShouldHaveSingleItem().Contents.Select(static content => content.GetType()).ShouldBe([typeof(TextContent), typeof(FunctionCallContent)]);
        response.Messages[0].MessageId.ShouldBe("message-1");
        response.Messages[0].AuthorName.ShouldBe("assistant-name");
        response.ResponseId.ShouldBe("resp-1");
        response.ModelId.ShouldBe("model-1");
        response.FinishReason.ShouldBe(ChatFinishReason.ToolCalls);
        response.Usage.ShouldBeSameAs(usage);
        response.CreatedAt.ShouldBe(inner.CreatedAt);
        response.ConversationId.ShouldBe("conversation-1");
        response.AdditionalProperties.ShouldBeSameAs(inner.AdditionalProperties);
    }

    [Fact]
    public void Every_settable_property_of_a_response_is_one_the_ring_copies()
    {
        // The ring builds a new response when it removes a call. A property the framework adds later would be dropped
        // from it without a sound; this list is what the copy was written against.
        var settable = typeof(ChatResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.SetMethod?.IsPublic == true)
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal);

        settable.ShouldBe(
        [
            nameof(ChatResponse.AdditionalProperties),
            nameof(ChatResponse.ContinuationToken),
            nameof(ChatResponse.ConversationId),
            nameof(ChatResponse.CreatedAt),
            nameof(ChatResponse.FinishReason),
            nameof(ChatResponse.Messages),
            nameof(ChatResponse.ModelId),
            nameof(ChatResponse.RawRepresentation),
            nameof(ChatResponse.ResponseId),
            nameof(ChatResponse.Usage),
        ]);
    }

    [Fact]
    public async Task The_response_the_inner_client_returned_is_not_changed()
    {
        // A client that caches its answers hands the same object out again.
        var inner = Answer(Call("s1", Server), Call("c1", Client));
        var message = inner.Messages[0];
        var contents = message.Contents;

        var response = await AskAsync(new MixedCallWithholdingChatClient(new ScriptedClient(inner)), Both());

        response.ShouldNotBeSameAs(inner);
        CallIds(inner).ShouldBe(["s1", "c1"]);
        inner.Messages[0].ShouldBeSameAs(message);
        message.Contents.ShouldBeSameAs(contents);
        contents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_message_that_held_only_a_client_call_is_dropped_rather_than_left_empty()
    {
        var inner = new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, [Call("s1", Server)]),
            new ChatMessage(ChatRole.Assistant, [Call("c1", Client)]),
        ]);

        var response = await AskAsync(new MixedCallWithholdingChatClient(new ScriptedClient(inner)), Both());

        response.Messages.ShouldHaveSingleItem();
        CallIds(response).ShouldBe(["s1"]);
    }

    [Fact]
    public async Task A_call_to_a_tool_the_request_does_not_declare_stays_in_a_mixed_response()
    {
        // The loop answers an unknown call with an error result; whether it does is not for this ring to decide.
        var response = await AskAsync(
            new MixedCallWithholdingChatClient(new ScriptedClient(Answer(Call("s1", Server), Call("c1", Client), Call("x1", "not_declared")))),
            Both());

        CallIds(response).ShouldBe(["s1", "x1"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task A_response_that_is_not_mixed_comes_back_as_the_same_object(int shape)
    {
        var inner = ResponseThatIsNotMixed(shape);

        var response = await AskAsync(new MixedCallWithholdingChatClient(new ScriptedClient(inner)), Both());

        response.ShouldBeSameAs(inner);
    }

    // Only server calls · several server calls · one client call · several client calls · no call · a client call after text · a client call and a call to an undeclared tool.
    private static ChatResponse ResponseThatIsNotMixed(int shape)
        => shape switch
        {
            0 => Answer(Call("s1", Server)),
            1 => Answer(Call("s1", Server), Call("s2", SecondServer)),
            2 => Answer(Call("c1", Client)),
            3 => Answer(Call("c1", Client), Call("c2", SecondClient)),
            4 => Answer(new TextContent("Nothing to call.")),
            5 => Answer(new TextContent("A client call and no server call."), Call("c1", Client)),
            _ => Answer(Call("x1", "not_declared"), Call("c1", Client)),
        };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task A_request_that_lacks_one_of_the_two_kinds_of_tool_is_not_looked_at(int shape)
    {
        var inner = Answer(Call("s1", Server), Call("c1", Client));

        var response = await AskAsync(new MixedCallWithholdingChatClient(new ScriptedClient(inner)), RequestThatCannotBeMixed(shape));

        response.ShouldBeSameAs(inner);
    }

    // No options · no tools set · an empty tool list · server tools only · client tools only · a hosted tool only.
    private static ChatOptions? RequestThatCannotBeMixed(int shape)
        => shape switch
        {
            0 => null,
            1 => new ChatOptions(),
            2 => new ChatOptions { Tools = [] },
            3 => new ChatOptions { Tools = [ServerTool(), ServerTool(SecondServer)] },
            4 => new ChatOptions { Tools = [ClientTool(), ClientTool(SecondClient)] },
            _ => new ChatOptions { Tools = [new HostedWebSearchTool()] },
        };

    [Fact]
    public async Task The_first_declaration_of_a_name_decides_which_kind_it_is()
    {
        // The same name declared twice: the loop takes the first, so the ring does too.
        var options = new ChatOptions { Tools = [ServerTool(Client), ClientTool(), ServerTool(SecondServer)] };

        var inner = Answer(Call("s1", SecondServer), Call("c1", Client));

        (await AskAsync(new MixedCallWithholdingChatClient(new ScriptedClient(inner)), options)).ShouldBeSameAs(inner);
    }

    // --- Streaming --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_stream_that_asks_for_a_server_tool_and_a_client_tool_loses_the_client_call()
    {
        var ring = new MixedCallWithholdingChatClient(new ScriptedClient(
            updates:
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Looking")]) { MessageId = "m1" },
                new ChatResponseUpdate(ChatRole.Assistant, [Call("s1", Server), Call("c1", Client)]) { MessageId = "m1", FinishReason = ChatFinishReason.ToolCalls },
            ]));

        var updates = await DrainAsync(ring, Both());

        CallIds(updates).ShouldBe(["s1"]);
        updates.Count.ShouldBe(2);
        updates[0].Contents.OfType<TextContent>().ShouldHaveSingleItem().Text.ShouldBe("Looking");
        updates[1].FinishReason.ShouldBe(ChatFinishReason.ToolCalls);
        updates[1].MessageId.ShouldBe("m1");
    }

    [Fact]
    public async Task A_client_call_that_arrives_in_an_update_of_its_own_leaves_that_update_empty_and_in_place()
    {
        // The update may be the one that reports the finish reason: it stays, with nothing in it.
        var ring = new MixedCallWithholdingChatClient(new ScriptedClient(
            updates:
            [
                new ChatResponseUpdate(ChatRole.Assistant, [Call("s1", Server)]),
                new ChatResponseUpdate(ChatRole.Assistant, [Call("c1", Client)]) { FinishReason = ChatFinishReason.ToolCalls },
                new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 4 })]),
            ]));

        var updates = await DrainAsync(ring, Both());

        updates.Count.ShouldBe(3);
        CallIds(updates).ShouldBe(["s1"]);
        updates[1].Contents.ShouldBeEmpty();
        updates[1].FinishReason.ShouldBe(ChatFinishReason.ToolCalls);
        updates[2].Contents.OfType<UsageContent>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_updates_a_stream_carries_are_not_changed()
    {
        var mixed = new ChatResponseUpdate(ChatRole.Assistant, [Call("s1", Server), Call("c1", Client)]);
        var contents = mixed.Contents;

        var updates = await DrainAsync(new MixedCallWithholdingChatClient(new ScriptedClient(updates: [mixed])), Both());

        updates.ShouldHaveSingleItem().ShouldNotBeSameAs(mixed);
        mixed.Contents.ShouldBeSameAs(contents);
        contents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_stream_that_is_not_mixed_comes_back_update_for_update_as_the_same_objects()
    {
        ChatResponseUpdate[] inner =
        [
            new(ChatRole.Assistant, [new TextContent("Looking")]),
            new(ChatRole.Assistant, [Call("s1", Server)]),
            new(ChatRole.Assistant, [Call("s2", SecondServer)]) { FinishReason = ChatFinishReason.ToolCalls },
        ];

        var updates = await DrainAsync(new MixedCallWithholdingChatClient(new ScriptedClient(updates: inner)), Both());

        updates.Count.ShouldBe(3);
        updates.Zip(inner).ShouldAllBe(static pair => ReferenceEquals(pair.First, pair.Second));
    }

    [Fact]
    public async Task A_stream_whose_calls_are_all_client_calls_comes_back_as_the_same_objects()
    {
        ChatResponseUpdate[] inner =
        [
            new(ChatRole.Assistant, [Call("c1", Client)]),
            new(ChatRole.Assistant, [Call("c2", SecondClient)]),
        ];

        var updates = await DrainAsync(new MixedCallWithholdingChatClient(new ScriptedClient(updates: inner)), Both());

        updates.Zip(inner).ShouldAllBe(static pair => ReferenceEquals(pair.First, pair.Second));
        updates.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_request_that_lacks_one_of_the_two_kinds_of_tool_streams_straight_through()
    {
        ChatResponseUpdate[] inner = [new(ChatRole.Assistant, [Call("s1", Server), Call("c1", Client)])];
        var options = new ChatOptions { Tools = [ServerTool()] };

        var updates = await DrainAsync(new MixedCallWithholdingChatClient(new ScriptedClient(updates: inner)), options);

        updates.ShouldHaveSingleItem().ShouldBeSameAs(inner[0]);
    }

    [Fact]
    public async Task Updates_before_the_first_call_leave_at_once_and_updates_from_the_first_call_wait_for_the_end_of_the_response()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reachedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var ring = new MixedCallWithholdingChatClient(new ScriptedClient(
            updates:
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("one")]),
                new ChatResponseUpdate(ChatRole.Assistant, [Call("s1", Server)]),
                new ChatResponseUpdate(ChatRole.Assistant, [Call("c1", Client)]),
            ],
            beforeLast: async () =>
            {
                reachedGate.TrySetResult();
                await gate.Task;
            }));

        await using var enumerator = ring
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "go")], Both(), TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        try
        {
            // The text update is not held: it arrives while the inner stream is still open. A ring that held it would leave
            // the inner stream parked at its gate before anything came out, which this reads as the failure it is.
            var firstMove = enumerator.MoveNextAsync().AsTask();

            (await Task.WhenAny(firstMove, reachedGate.Task)).ShouldBeSameAs(firstMove, "the text update was held until the response ended");
            (await firstMove).ShouldBeTrue();
            enumerator.Current.Text.ShouldBe("one");

            var next = enumerator.MoveNextAsync().AsTask();
            await reachedGate.Task;

            // The call update is held: the inner stream is parked before its last update and nothing has come out.
            next.IsCompleted.ShouldBeFalse();

            gate.SetResult();

            (await next).ShouldBeTrue();
            CallIds([enumerator.Current]).ShouldBe(["s1"]);
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            enumerator.Current.Contents.ShouldBeEmpty();
            (await enumerator.MoveNextAsync()).ShouldBeFalse();
        }
        finally
        {
            // A failed assertion must not leave the inner stream parked: disposing the enumerator would wait for it.
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task A_stream_that_fails_after_a_call_passes_the_failure_on_and_hands_on_none_of_the_held_updates()
    {
        var ring = new MixedCallWithholdingChatClient(new ScriptedClient(
            updates: [new ChatResponseUpdate(ChatRole.Assistant, [Call("s1", Server), Call("c1", Client)])],
            failAfterUpdates: new InvalidOperationException("provider failed")));

        var seen = new List<ChatResponseUpdate>();

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var update in ring.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "go")], Both(), TestContext.Current.CancellationToken))
            {
                seen.Add(update);
            }
        });

        failure.Message.ShouldBe("provider failed");
        seen.ShouldBeEmpty("a response that did not end is not handed on: the loop would run a call of an answer the provider never finished");
    }

    [Fact]
    public async Task A_caller_that_stops_reading_a_held_stream_gets_nothing_more_and_the_inner_stream_is_released()
    {
        var inner = new ScriptedClient(updates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("one")]),
            new ChatResponseUpdate(ChatRole.Assistant, [Call("s1", Server), Call("c1", Client)]),
        ]);

        var ring = new MixedCallWithholdingChatClient(inner);

        await using (var enumerator = ring
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "go")], Both(), TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
        }

        inner.StreamsDisposed.ShouldBe(1);
    }

    [Fact]
    public async Task A_canceled_request_stops_the_ring_and_no_held_update_leaks()
    {
        using var canceled = new CancellationTokenSource();

        var ring = new MixedCallWithholdingChatClient(new ScriptedClient(updates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("one")]),
            new ChatResponseUpdate(ChatRole.Assistant, [Call("s1", Server), Call("c1", Client)]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("two")]),
        ]));

        await using var enumerator = ring
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "go")], Both(), canceled.Token)
            .GetAsyncEnumerator(canceled.Token);

        (await enumerator.MoveNextAsync()).ShouldBeTrue();

        await canceled.CancelAsync();

        var thrown = await Record.ExceptionAsync(async () => await enumerator.MoveNextAsync());

        thrown.ShouldBeAssignableTo<OperationCanceledException>();
    }

    // --- Doubles ----------------------------------------------------------------------------------------------------

    private sealed class ScriptedClient : IChatClient
    {
        private readonly ChatResponse? _response;
        private readonly IReadOnlyList<ChatResponseUpdate> _updates;
        private readonly Func<Task>? _beforeLast;
        private readonly Exception? _failAfterUpdates;
        private int _streamsDisposed;

        public ScriptedClient(ChatResponse response)
        {
            _response = response;
            _updates = [];
        }

        public ScriptedClient(IReadOnlyList<ChatResponseUpdate> updates, Func<Task>? beforeLast = null, Exception? failAfterUpdates = null)
        {
            _updates = updates;
            _beforeLast = beforeLast;
            _failAfterUpdates = failAfterUpdates;
        }

        public int StreamsDisposed => Volatile.Read(ref _streamsDisposed);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_response ?? throw new InvalidOperationException("This client answers a stream only."));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                for (var index = 0; index < _updates.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (index == _updates.Count - 1 && _beforeLast is not null)
                    {
                        await _beforeLast().ConfigureAwait(false);
                    }

                    yield return _updates[index];
                }

                if (_failAfterUpdates is not null)
                {
                    await Task.Yield();

                    throw _failAfterUpdates;
                }
            }
            finally
            {
                Interlocked.Increment(ref _streamsDisposed);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
