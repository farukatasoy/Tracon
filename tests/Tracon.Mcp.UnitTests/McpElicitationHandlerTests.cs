using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The decision table of <see cref="McpElicitationHandler"/>: which request is answered, which is declined and
/// why, and which one suspends the tool call. Each row of the table is a sentence the plan promised; a row that
/// quietly changed would turn a "no" into a hang or a "yes" into someone else's answer.
/// </summary>
public sealed class McpElicitationHandlerTests
{
    private static readonly ElicitRequestParams Form = new()
    {
        Message = "What is your name?",
        RequestedSchema = new ElicitRequestParams.RequestSchema
        {
            Properties = { ["name"] = new ElicitRequestParams.StringSchema { Title = "name" } },
            Required = ["name"],
        },
    };

    [Fact]
    public async Task A_request_outside_any_tool_call_is_declined()
    {
        // The older protocol delivers a request from the connection's receive loop; no call scope is open there.
        var logger = new CapturingLogger();
        var result = await Handler(logger).HandleAsync(Form, CancellationToken.None);

        result.Action.ShouldBe("decline");
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.NoRun]);
    }

    [Fact]
    public async Task A_request_reaching_a_call_that_already_ended_is_declined()
    {
        var scope = OpenScope(Run());
        scope.Close();

        var logger = new CapturingLogger();

        (await Handler(logger).HandleAsync(Form, CancellationToken.None)).Action.ShouldBe("decline");
        scope.Pending.ShouldBeNull();
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.NoRun]);
    }

    [Fact]
    public async Task A_request_to_open_an_address_is_declined()
    {
        var scope = OpenScope(Run());

        var url = new ElicitRequestParams
        {
            Mode = "url",
            Message = "Sign in",
            Url = "https://example.com/login",
            ElicitationId = "e1",
        };

        var logger = new CapturingLogger();

        (await Handler(logger).HandleAsync(url, CancellationToken.None)).Action.ShouldBe("decline");
        scope.Pending.ShouldBeNull();
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.UrlMode]);
    }

    [Fact]
    public async Task A_call_outside_a_run_is_declined()
    {
        OpenScope(run: null);

        var logger = new CapturingLogger();

        (await Handler(logger).HandleAsync(Form, CancellationToken.None)).Action.ShouldBe("decline");
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.NoRun]);
    }

    [Fact]
    public async Task An_agent_below_the_root_of_a_tree_cannot_ask_the_user()
    {
        // A sub-agent cannot wait in the middle of its parent's tree.
        var scope = OpenScope(Run(depth: 1));
        var logger = new CapturingLogger();

        (await Handler(logger).HandleAsync(Form, CancellationToken.None)).Action.ShouldBe("decline");
        scope.Pending.ShouldBeNull();
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.ChildRun]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_run_without_a_session_has_nowhere_to_take_the_answer_from(string? session)
    {
        var scope = OpenScope(Run(session: session));
        var logger = new CapturingLogger();

        (await Handler(logger).HandleAsync(Form, CancellationToken.None)).Action.ShouldBe("decline");
        scope.Pending.ShouldBeNull();
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.NoSession]);
    }

    [Fact]
    public async Task A_surface_that_cannot_carry_the_answer_declines()
    {
        // An OpenAI-compatible call, an A2A task, an MCP tool call and a queued run leave the flag off.
        var scope = OpenScope(Run(acceptsToolInput: false));
        var logger = new CapturingLogger();

        (await Handler(logger).HandleAsync(Form, CancellationToken.None)).Action.ShouldBe("decline");
        scope.Pending.ShouldBeNull();
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.NoChannel]);
    }

    [Fact]
    public async Task A_tool_the_loop_cannot_stop_is_declined_instead_of_suspended()
    {
        // A suspended call ends the run by telling the function-invocation loop to stop. A tool that was not
        // called by the loop has no loop to tell, and suspending it would strand the run.
        var scope = OpenScope(Run(), canTerminate: false);
        var logger = new CapturingLogger();

        (await Handler(logger).HandleAsync(Form, CancellationToken.None)).Action.ShouldBe("decline");
        scope.Pending.ShouldBeNull();
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.NoTermination]);
    }

    [Fact]
    public async Task A_fresh_request_suspends_the_call_and_records_what_to_run_again()
    {
        var run = Run();
        var scope = OpenScope(run, arguments: new Dictionary<string, object?>(StringComparer.Ordinal) { ["city"] = "Ankara" });

        await Should.ThrowAsync<McpInputPendingException>(
            async () => await Handler().HandleAsync(Form, CancellationToken.None));

        var pending = scope.Pending.ShouldNotBeNull();
        pending.Message.ShouldBe(Form.Message);
        pending.ServerName.ShouldBe("srv");
        pending.ToolName.ShouldBe("srv_ask");
        pending.CallId.ShouldBe("call-1");
        pending.RequestedSchema.GetProperty("properties").GetProperty("name").GetProperty("type").GetString().ShouldBe("string");
        pending.Fingerprint.ShouldBe(McpElicitationHandler.Fingerprint(Form.Message, pending.RequestedSchema));

        // The call that runs again: the same tool and arguments, under an identity of its own.
        pending.ReplayCall.Name.ShouldBe("srv_ask");
        string.Equals(pending.ReplayCall.CallId, "call-1", StringComparison.Ordinal).ShouldBeFalse("a replay never reuses the identity of the call it replays");
        pending.ReplayCall.Arguments.ShouldNotBeNull()["city"].ShouldBe("Ankara");
    }

    [Fact]
    public async Task Only_the_first_of_two_requests_in_one_call_is_kept()
    {
        var scope = OpenScope(Run());
        var handler = Handler();

        await Should.ThrowAsync<McpInputPendingException>(async () => await handler.HandleAsync(Form, CancellationToken.None));
        var first = scope.Pending.ShouldNotBeNull();

        await Should.ThrowAsync<McpInputPendingException>(async () => await handler.HandleAsync(Form, CancellationToken.None));

        scope.Pending.ShouldBeSameAs(first);
    }

    [Fact]
    public async Task The_answer_given_to_the_request_is_returned_to_the_server()
    {
        var fingerprint = FingerprintOf(Form);

        OpenScope(
            Run(),
            answers: [new ToolInputAnswer(fingerprint, ToolInputActions.Accept, new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["name"] = JsonSerializer.SerializeToElement("Ada") })]);

        var result = await Handler().HandleAsync(Form, CancellationToken.None);

        result.IsAccepted.ShouldBeTrue();
        result.Content.ShouldNotBeNull()["name"].GetString().ShouldBe("Ada");
    }

    [Theory]
    [InlineData(ToolInputActions.Decline)]
    [InlineData(ToolInputActions.Cancel)]
    public async Task A_refusal_is_returned_as_the_refusal_with_no_fields(string action)
    {
        OpenScope(Run(), answers: [new ToolInputAnswer(FingerprintOf(Form), action, Content: null)]);

        var result = await Handler().HandleAsync(Form, CancellationToken.None);

        result.Action.ShouldBe(action);
        result.Content.ShouldBeNull();
    }

    [Fact]
    public async Task An_answer_is_used_once()
    {
        var scope = OpenScope(
            Run(),
            answers: [new ToolInputAnswer(FingerprintOf(Form), ToolInputActions.Accept, new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["name"] = JsonSerializer.SerializeToElement("Ada") })]);

        var logger = new CapturingLogger();
        var handler = Handler(logger);

        (await handler.HandleAsync(Form, CancellationToken.None)).IsAccepted.ShouldBeTrue();

        // The same question again, with the answer spent: declined, never suspended, never given Ada twice.
        (await handler.HandleAsync(Form, CancellationToken.None)).Action.ShouldBe("decline");
        scope.Pending.ShouldBeNull();
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.Mismatch]);
    }

    [Fact]
    public async Task A_different_question_is_not_given_the_old_answer()
    {
        // The answer was given to "What is your name?". A "yes" to it must never reach another question.
        var scope = OpenScope(
            Run(),
            answers: [new ToolInputAnswer(FingerprintOf(Form), ToolInputActions.Accept, new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["name"] = JsonSerializer.SerializeToElement("Ada") })]);

        var other = new ElicitRequestParams
        {
            Message = "Delete everything?",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = { ["name"] = new ElicitRequestParams.StringSchema { Title = "name" } },
            },
        };

        var logger = new CapturingLogger();
        var result = await Handler(logger).HandleAsync(other, CancellationToken.None);

        result.Action.ShouldBe("decline");
        result.Content.ShouldBeNull();
        scope.Pending.ShouldBeNull("a replayed call is never suspended a second time");
        logger.Reasons.ShouldBe([McpElicitationHandler.Reasons.Mismatch]);
    }

    [Fact]
    public async Task The_same_message_with_other_fields_is_another_question()
    {
        var withNumber = new ElicitRequestParams
        {
            Message = Form.Message,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = { ["name"] = new ElicitRequestParams.NumberSchema { Title = "name" } },
            },
        };

        var schema = JsonSerializer.SerializeToElement(
            withNumber.RequestedSchema,
            ModelContextProtocol.McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ElicitRequestParams.RequestSchema)));

        string.Equals(McpElicitationHandler.Fingerprint(withNumber.Message, schema), FingerprintOf(Form), StringComparison.Ordinal).ShouldBeFalse();
    }

    // ---- Harness ----------------------------------------------------------------

    private static McpElicitationHandler Handler(CapturingLogger? logger = null) => new("srv", logger ?? new CapturingLogger());

    private static string FingerprintOf(ElicitRequestParams request)
        => McpElicitationHandler.Fingerprint(
            request.Message,
            JsonSerializer.SerializeToElement(
                request.RequestedSchema,
                ModelContextProtocol.McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ElicitRequestParams.RequestSchema))));

    private static AgentRunScope Run(int depth = 0, string? session = "session-1", bool acceptsToolInput = true)
        => new()
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            Depth = depth,
            SessionId = session,
            AcceptsToolInput = acceptsToolInput,
            ToolInputs = new ToolInputLedger(),
        };

    /// <summary>Opens a call scope in the test's own flow, the way <see cref="McpElicitingTool"/> does in its body.</summary>
    private static McpToolCallScope OpenScope(
        AgentRunScope? run,
        bool canTerminate = true,
        IReadOnlyList<ToolInputAnswer>? answers = null,
        Dictionary<string, object?>? arguments = null)
    {
        var scope = new McpToolCallScope(
            "srv",
            "srv_ask",
            run,
            new FunctionCallContent("call-1", "srv_ask", arguments),
            canTerminate,
            answers);

        McpToolCallScope.Open(scope);

        return scope;
    }

    /// <summary>Keeps the reason code of every decline the handler logged.</summary>
    private sealed class CapturingLogger : ILogger
    {
        private readonly List<string> _reasons = [];

        public IReadOnlyList<string> Reasons
        {
            get
            {
                lock (_reasons)
                {
                    return [.. _reasons];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values &&
                values.FirstOrDefault(static pair => string.Equals(pair.Key, "Reason", StringComparison.Ordinal)).Value is string reason)
            {
                lock (_reasons)
                {
                    _reasons.Add(reason);
                }
            }
        }
    }
}
