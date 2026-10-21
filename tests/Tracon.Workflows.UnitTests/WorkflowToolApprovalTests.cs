using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A tool call that waits for approval inside a workflow becomes a request of the <c>ToolApproval</c>
/// form, and the answer is built from the request the checkpoint holds (phase 230, K-1051).
/// </summary>
/// <remarks>
/// <para>
/// The engine is measured here without Tracon's own agent wrapper: a stand-in agent returns the approval request
/// the way Microsoft.Extensions.AI's function-invoking client does, and a ready-made pattern hosts it. What is
/// pinned is the part that is Tracon's: the form, the facts the request records for the audit row and the rule,
/// the refusals of the early check, and that an answer carries a decision and nothing else.
/// </para>
/// </remarks>
public sealed class WorkflowToolApprovalTests
{
    private const string Tool = "cancel_order";

    [Fact]
    public async Task An_approval_request_of_a_hosted_agent_is_published_as_a_tool_approval_request()
    {
        var (_, runner, run) = await StartAndWaitAsync(new AskingAgent("asker"));

        var request = (await runner.ListPendingRequestsAsync(run.Id)).ShouldHaveSingleItem();

        request.Form.ShouldBe(WorkflowRequestForm.ToolApproval);

        // The prompt names the tool, never the arguments.
        request.Prompt.ShouldBe(Tool);
        request.RequestType.ShouldBe("Microsoft.Extensions.AI.ToolApprovalRequestContent");
        run.Status.ShouldBe(RunStatus.AwaitingInput);
    }

    [Fact]
    public async Task The_request_records_the_tool_the_agent_and_a_hash_of_the_arguments_but_not_the_arguments()
    {
        var (host, _, run) = await StartAndWaitAsync(new AskingAgent("asker"));

        var recorded = await RequestEventAsync(host, run.Id);
        var facts = WorkflowToolApprovalFacts.TryRead(recorded.Payload).ShouldNotBeNull();

        facts.ToolName.ShouldBe(Tool);

        // The stand-in is not one of the runner's agent wrappers, so nothing can name it by its real name. The id
        // carries the name only as the framework rewrote it, and a request that cannot say which agent asked
        // says nothing rather than a name that would never match (the endpoint then refuses "remember").
        facts.AgentName.ShouldBeNull();
        facts.ArgumentsHash.ShouldBe(
            ToolApprovalRuleEvaluator.ComputeArgumentsHash(new Dictionary<string, object?>(StringComparer.Ordinal) { ["orderId"] = "ORD-7" }));
        recorded.Payload.ShouldNotBeNull().ShouldNotContain("ORD-7", Case.Sensitive);
    }

    [Fact]
    public async Task An_approved_answer_reaches_the_agent_with_the_reason_and_the_call_the_request_held()
    {
        var agent = new AskingAgent("asker");
        var (_, runner, run) = await StartAndWaitAsync(agent);
        var request = (await runner.ListPendingRequestsAsync(run.Id)).Single();

        var resumed = await RespondAsync(runner, run.Id, request.RequestId, approved: true, text: "checked with the customer");

        resumed[^1].Type.ShouldBe(RunEventType.RunCompleted);
        agent.Answer.ShouldNotBeNull().Approved.ShouldBeTrue();
        agent.Answer.Reason.ShouldBe("checked with the customer");

        // The call comes from the request the workflow recorded: the answer carried none.
        agent.Answer.ToolCall.ShouldBeOfType<FunctionCallContent>().Name.ShouldBe(Tool);
        agent.ToolRuns.ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_answer_reaches_the_agent_as_a_refusal()
    {
        var agent = new AskingAgent("asker");
        var (_, runner, run) = await StartAndWaitAsync(agent);
        var request = (await runner.ListPendingRequestsAsync(run.Id)).Single();

        var resumed = await RespondAsync(runner, run.Id, request.RequestId, approved: false, text: "not this order");

        resumed[^1].Type.ShouldBe(RunEventType.RunCompleted);
        agent.Answer.ShouldNotBeNull().Approved.ShouldBeFalse();
        agent.ToolRuns.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, null, null, null, "'approved'")]
    [InlineData(true, null, "{}", null, "'data'")]
    [InlineData(false, null, null, true, "'remember'")]
    public async Task The_early_check_refuses_an_answer_that_does_not_fit_a_tool_approval(
        bool? approved,
        string? text,
        string? json,
        bool? remember,
        string expectedPart)
    {
        var (_, runner, run) = await StartAndWaitAsync(new AskingAgent("asker"));
        var request = (await runner.ListPendingRequestsAsync(run.Id)).Single();

        var problem = await runner.ValidateResponseAsync(new WorkflowRespondRequest
        {
            RunId = run.Id,
            RequestId = request.RequestId,
            Approved = approved,
            Text = text,
            Json = json,
            Remember = remember,
        });

        problem.ShouldNotBeNull().ShouldContain(expectedPart, Case.Sensitive);
    }

    [Fact]
    public async Task The_early_check_takes_an_approval_that_asks_to_be_remembered()
    {
        var (_, runner, run) = await StartAndWaitAsync(new AskingAgent("asker"));
        var request = (await runner.ListPendingRequestsAsync(run.Id)).Single();

        (await runner.ValidateResponseAsync(new WorkflowRespondRequest
        {
            RunId = run.Id,
            RequestId = request.RequestId,
            Approved = true,
            Remember = true,
        })).ShouldBeNull();
    }

    [Fact]
    public async Task A_conversion_refuses_a_data_answer_the_early_check_did_not_see()
    {
        // A caller of the runner that skips the HTTP layer reaches Create directly; it refuses the same answer.
        var agent = new AskingAgent("asker");
        var (_, runner, run) = await StartAndWaitAsync(agent);
        var request = (await runner.ListPendingRequestsAsync(run.Id)).Single();

        var resumed = await RespondAsync(runner, run.Id, request.RequestId, approved: true, text: null, json: "{\"name\":\"other_tool\"}");

        resumed[^1].Type.ShouldBe(RunEventType.RunFailed);
        agent.ToolRuns.ShouldBe(0);
    }

    [Fact]
    public void A_remember_answer_to_a_request_that_is_not_a_tool_approval_is_refused()
    {
        var recorded = new RecordedRequest("request-1", "approval-port", WorkflowRequestForm.Boolean, ResponseType: null);

        WorkflowResponseFactory.Check(recorded, new WorkflowAnswer("request-1", Approved: true, Text: null, Json: null, Remember: true))
            .ShouldNotBeNull().ShouldContain("'remember'", Case.Sensitive);

        // A request written before the form existed cannot be judged here; the republished request decides.
        WorkflowResponseFactory.Check(
                new RecordedRequest("request-1", "approval-port", Form: null, ResponseType: null),
                new WorkflowAnswer("request-1", Approved: true, Text: null, Json: null, Remember: true))
            .ShouldBeNull();
    }

    [Fact]
    public void The_agents_real_name_is_found_by_its_identity_and_not_by_the_name_the_framework_gave_its_executor()
    {
        // The framework rewrites a character it dislikes: "approval-agent" is "approval_agent" in the executor id.
        var host = new WorkflowTestHost("approval-agent");
        var wrapper = host.AgentCache.Get("approval-flow", "approval-agent", description: null);
        var portId = $"approval_agent_{wrapper.Id}_UserInput";

        var identity = WorkflowRequestDescriptor.ExecutorIdentityOf(portId).ShouldNotBeNull();

        identity.ShouldBe(wrapper.Id);
        host.AgentCache.NameOf(identity).ShouldBe("approval-agent");
        host.AgentCache.NameOf(new string('0', 32)).ShouldBeNull();
    }

    [Theory]
    [InlineData("asker_0123456789abcdef0123456789abcdef_UserInput", "0123456789abcdef0123456789abcdef")]
    [InlineData("a_b_0123456789ABCDEF0123456789abcdef_UserInput", "0123456789ABCDEF0123456789abcdef")]
    [InlineData("asker_0123456789abcdef0123456789abcdef_FunctionCall", null)]
    [InlineData("asker_0123456789abcdef0123456789abcdez_UserInput", null)]
    [InlineData("0123456789abcdef0123456789abcdef_UserInput", null)]
    [InlineData("_UserInput", null)]
    [InlineData("approval-port", null)]
    public void The_identity_is_read_from_the_port_of_an_agent_host_and_from_nothing_else(string portId, string? expected)
        => WorkflowRequestDescriptor.ExecutorIdentityOf(portId).ShouldBe(expected);

    [Theory]
    [InlineData("""{"toolName":"cancel_order","agentName":"writer","toolArgumentsHash":"AB"}""", "cancel_order", "writer", "AB")]
    [InlineData("""{"toolName":"cancel_order"}""", "cancel_order", null, null)]
    public void The_facts_are_read_back_from_a_payload(string payload, string tool, string? agent, string? hash)
    {
        var facts = WorkflowToolApprovalFacts.TryRead(payload).ShouldNotBeNull();

        facts.ToolName.ShouldBe(tool);
        facts.AgentName.ShouldBe(agent);
        facts.ArgumentsHash.ShouldBe(hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"toolName\":\"cancel_o")]
    [InlineData("""{"toolName":""}""")]
    [InlineData("""{"prompt":"an approval for nothing"}""")]
    [InlineData("[1]")]
    public void A_payload_that_names_no_tool_gives_no_facts(string? payload)
        => WorkflowToolApprovalFacts.TryRead(payload).ShouldBeNull();

    private static async Task<(WorkflowTestHost Host, WorkflowRunner Runner, RunRecord Run)> StartAndWaitAsync(AIAgent agent)
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(
            configure: null,
            services: null,
            new CodeWorkflowRegistration("approval-agent-flow", "A hosted agent asks for approval.", _ => AgentWorkflowBuilder.BuildSequential("approval-agent-flow", [agent])));

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "approval-agent-flow", Message = "go" }))
        {
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        return (host, runner, run);
    }

    private static async Task<RunEvent> RequestEventAsync(WorkflowTestHost host, Guid runId)
    {
        await foreach (var runEvent in host.RunStore.ReadEventsAsync(runId, 0))
        {
            if (runEvent.Type == RunEventType.WorkflowRequest)
            {
                return runEvent;
            }
        }

        throw new InvalidOperationException("The run published no request.");
    }

    private static async Task<List<RunEvent>> RespondAsync(
        WorkflowRunner runner,
        Guid runId,
        string requestId,
        bool approved,
        string? text,
        string? json = null)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = runId,
            RequestId = requestId,
            Approved = approved,
            Text = text,
            Json = json,
        }))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>
    /// An agent that asks for approval of a tool call, and on the answer returns the completed call the way
    /// Microsoft.Extensions.AI's function-invoking client does.
    /// </summary>
    private sealed class AskingAgent(string name) : AIAgent
    {
        private int _toolRuns;

        public override string Name => name;

        public ToolApprovalResponseContent? Answer { get; private set; }

        public int ToolRuns => Volatile.Read(ref _toolRuns);

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The pattern's host streams.");

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            var call = new FunctionCallContent(
                "call-1",
                Tool,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["orderId"] = "ORD-7" });

            if (messages.SelectMany(static message => message.Contents).OfType<ToolApprovalResponseContent>().FirstOrDefault() is { } answer)
            {
                Answer = answer;

                if (answer.Approved)
                {
                    Interlocked.Increment(ref _toolRuns);
                }

                yield return new AgentResponseUpdate(ChatRole.Assistant, [call]);
                yield return new AgentResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", answer.Approved ? "ORD-7 canceled." : "Refused.")]);
                yield return new AgentResponseUpdate(ChatRole.Assistant, [new TextContent("done")]);

                yield break;
            }

            yield return new AgentResponseUpdate(ChatRole.Assistant, [new ToolApprovalRequestContent("request-1", call)]);
        }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new AskingSession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new AskingSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(JsonDocument.Parse("{}").RootElement.Clone());

        private sealed class AskingSession : AgentSession;
    }
}
