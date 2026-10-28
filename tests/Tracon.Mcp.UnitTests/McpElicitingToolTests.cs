using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// <see cref="McpElicitingTool"/> inside the REAL function-invocation loop and Tracon's REAL wrapper chain: the
/// stop signal, the call identity and the run scope all travel through <c>AsyncLocal</c> state that only the loop
/// sets, so a stand-in for the loop would prove nothing about them.
/// </summary>
public sealed class McpElicitingToolTests
{
    private static readonly ElicitRequestParams Form = new()
    {
        Message = "What is your name?",
        RequestedSchema = new ElicitRequestParams.RequestSchema
        {
            Properties = { ["name"] = new ElicitRequestParams.StringSchema { Title = "name" } },
        },
    };

    [Fact]
    public async Task A_suspended_call_stops_the_loop_and_returns_the_request_as_its_result()
    {
        var run = Run();
        TraconRunContext.SetCurrent(run);

        var model = new ScriptedModel("srv_ask", callId: "call-1");
        var response = await Loop(model).GetResponseAsync("greet me", Options(ComposeChain(HandlerBackedTool())));

        model.Calls.ShouldBe(1, "the loop stopped: the model never saw the request");

        var result = response.Messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().ShouldHaveSingleItem();
        result.CallId.ShouldBe("call-1");

        using var payload = JsonDocument.Parse((string)result.Result!);
        payload.RootElement.GetProperty("status").GetString().ShouldBe("input_required");
        payload.RootElement.GetProperty("message").GetString().ShouldBe(Form.Message);
        payload.RootElement.GetProperty("server").GetString().ShouldBe("srv");
        payload.RootElement.GetProperty("tool").GetString().ShouldBe("srv_ask");
        payload.RootElement.GetProperty("replayCallId").GetString().ShouldNotBeNullOrEmpty();
        payload.RootElement.GetProperty("fingerprint").GetString().ShouldNotBeNullOrEmpty();

        var pending = run.ToolInputs!.Snapshot().ShouldHaveSingleItem();
        pending.RequestId.ShouldBe(payload.RootElement.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task The_replayed_call_reads_its_answer_and_the_loop_goes_on()
    {
        var run = Run() with
        {
            ToolInputAnswers = new Dictionary<string, IReadOnlyList<ToolInputAnswer>>(StringComparer.Ordinal)
            {
                ["call-replay"] = [new ToolInputAnswer(Fingerprint(), ToolInputActions.Accept, new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["name"] = JsonSerializer.SerializeToElement("Ada") })],
            },
        };

        TraconRunContext.SetCurrent(run);

        var model = new ScriptedModel("srv_ask", callId: "call-replay");
        var response = await Loop(model).GetResponseAsync("greet me", Options(ComposeChain(HandlerBackedTool())));

        model.Calls.ShouldBe(2, "the call finished, so the model answered from its result");
        run.ToolInputs!.HasPending.ShouldBeFalse();
        response.Messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().ShouldHaveSingleItem()
            .Result!.ToString()!.ShouldContain("accept:Ada");
    }

    [Fact]
    public async Task A_request_outside_a_run_is_declined_and_the_loop_goes_on()
    {
        TraconRunContext.SetCurrent(null);

        var model = new ScriptedModel("srv_ask", callId: "call-1");
        var response = await Loop(model).GetResponseAsync("greet me", Options(ComposeChain(HandlerBackedTool())));

        model.Calls.ShouldBe(2);
        response.Messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().ShouldHaveSingleItem()
            .Result!.ToString()!.ShouldContain("decline");
    }

    [Fact]
    public async Task The_call_scope_is_closed_when_the_call_ends()
    {
        TraconRunContext.SetCurrent(Run());

        McpToolCallScope? seen = null;

        var inner = AIFunctionFactory.Create(
            () =>
            {
                seen = McpToolCallScope.Current;

                return "done";
            },
            "srv_ask");

        var model = new ScriptedModel("srv_ask", callId: "call-1");
        await Loop(model).GetResponseAsync("greet me", Options(ComposeChain(new McpElicitingTool(inner, "srv"))));

        var scope = seen.ShouldNotBeNull("the scope is open while the tool runs");
        scope.IsActive.ShouldBeFalse("a handler reached later, from a task the SDK left running, must not answer for a call that is over");
        scope.Call!.CallId.ShouldBe("call-1");
    }

    [Fact]
    public async Task A_caller_that_cancels_is_not_turned_into_a_suspension()
    {
        var run = Run();
        TraconRunContext.SetCurrent(run);

        using var cancellation = new CancellationTokenSource();

        var inner = AIFunctionFactory.Create(
            async (CancellationToken token) =>
            {
                // The server asked, and the caller went away before the answer: not a suspended call.
                await Should.ThrowAsync<McpInputPendingException>(
                    async () => await new McpElicitationHandler("srv", NullLogger.Instance).HandleAsync(Form, CancellationToken.None));
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();

                return "unreachable";
            },
            "srv_ask");

        var model = new ScriptedModel("srv_ask", callId: "call-1");

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Loop(model).GetResponseAsync("greet me", Options(ComposeChain(new McpElicitingTool(inner, "srv"))), cancellation.Token));

        run.ToolInputs!.HasPending.ShouldBeFalse();
    }

    // ---- Harness ----------------------------------------------------------------

    private static string Fingerprint()
        => McpElicitationHandler.Fingerprint(
            Form.Message,
            JsonSerializer.SerializeToElement(
                Form.RequestedSchema,
                ModelContextProtocol.McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ElicitRequestParams.RequestSchema))));

    private static AgentRunScope Run()
        => new()
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            Depth = 0,
            SessionId = "session-1",
            AcceptsToolInput = true,
            ToolInputs = new ToolInputLedger(),
        };

    /// <summary>A tool whose body asks the way the SDK's retry loop does: through the connection's handler.</summary>
    private static McpElicitingTool HandlerBackedTool()
    {
        var handler = new McpElicitationHandler("srv", NullLogger.Instance);

        var inner = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                var answer = await handler.HandleAsync(Form, cancellationToken);

                return answer.IsAccepted ? $"accept:{answer.Content?["name"].GetString()}" : answer.Action;
            },
            "srv_ask");

        return new McpElicitingTool(inner, "srv");
    }

    /// <summary>The tool wrapped the way <c>McpTenantTools</c> wraps every discovered tool.</summary>
    private static AIFunctionDeclaration ComposeChain(AIFunction tool)
    {
        var tools = McpTenantTools.Create(
            [new TraconToolRegistration(tool) { Source = "srv" }],
            NullLogger.Instance,
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            TimeSpan.FromSeconds(30),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        tools.TryGet(tool.Name, out var wrapped).ShouldBeTrue();

        return wrapped!;
    }

    private static FunctionInvokingChatClient Loop(ScriptedModel model) => new(model);

    private static ChatOptions Options(AIFunctionDeclaration tool) => new() { Tools = [tool] };

    /// <summary>A model that calls one tool, then answers once it has seen the result.</summary>
    private sealed class ScriptedModel(string tool, string callId) : IChatClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);

            return Task.FromResult(call == 1
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, tool, new Dictionary<string, object?>(StringComparer.Ordinal))]))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);

            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
