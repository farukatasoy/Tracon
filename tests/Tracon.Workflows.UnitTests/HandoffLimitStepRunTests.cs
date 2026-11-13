using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The workflow step that asks for the handoff past the run's limit: the same on both ways the framework can call a
/// step, one answer and a stream of updates.
/// </summary>
/// <remarks>
/// <para>
/// A stored Handoff workflow always streams its steps; the one-answer path is reached by code that calls a step wrapper
/// itself, so these cases call the wrapper directly under a recorded parent run whose scope carries a counter. The parent
/// run, the step wrapper and the run rows are the real ones; only the step's agent is scripted, to ask for a handoff.
/// </para>
/// <para>
/// What the cases pin: the call that goes past the limit never reaches the caller (the framework acts on a handoff
/// call when it reads it), the step's own run has made its whole answer and closes <see cref="RunStatus.Completed"/>
/// whichever way it was read, and the run that called the step fails with the limit.
/// </para>
/// </remarks>
public sealed class HandoffLimitStepRunTests
{
    private const string Step = "specialist";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_step_past_the_limit_closes_its_own_run_completed_and_only_the_run_above_it_fails(bool streaming)
    {
        var host = new WorkflowTestHost();
        host.AddAgent(Step, new HandoffAskingAgent(Step));

        var caller = new StepCaller(host.AgentCache.Get("triage", Step, description: null), limit: 1, calls: 2, streaming);
        var parent = Parent(host, caller);

        var failure = await Should.ThrowAsync<TraconHandoffLimitExceededException>(
            async () => await parent.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("handoff number 2", Case.Sensitive);
        failure.Message.ShouldContain("allows 1", Case.Sensitive);

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var steps = runs.Where(static run => run.Depth == 1).OrderBy(static run => run.StartedAt).ToList();

        runs.Single(static run => run.Depth == 0).Status.ShouldBe(RunStatus.Failed);
        steps.Count.ShouldBe(2);
        steps.ShouldAllBe(
            static step => step.Status == RunStatus.Completed,
            string.Join(", ", steps.Select(static step => step.Status)));

        // The call within the limit went up; the one past it did not.
        caller.HandoffCallsDelivered.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_tool_approval_a_step_cannot_host_fails_the_step_and_its_run_closes_the_same_on_both_paths(bool streaming)
    {
        // The same early exit as the handoff past the limit: the step fails on what the child's answer holds, and a
        // child whose stream was left unfinished closed Canceled for a call nobody cancelled.
        var host = new WorkflowTestHost();
        host.AddAgent(Step, new HandoffAskingAgent(Step) { AsksForApproval = true });

        var caller = new StepCaller(host.AgentCache.Get("triage", Step, description: null), limit: 5, calls: 1, streaming);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await Parent(host, caller).RunAsync("go", cancellationToken: TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("requires user approval", Case.Sensitive);

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

        // A sub-run that ends by asking for approval is Failed: an agent tree cannot wait in its middle.
        runs.Single(static run => run.Depth == 1).Status.ShouldBe(RunStatus.Failed);
        runs.Single(static run => run.Depth == 0).Status.ShouldBe(RunStatus.Failed);
        caller.HandoffCallsDelivered.ShouldBe(0);
    }

    [Fact]
    public async Task A_step_within_the_limit_is_unchanged_on_both_paths()
    {
        var host = new WorkflowTestHost();
        host.AddAgent(Step, new HandoffAskingAgent(Step));

        foreach (var streaming in new[] { false, true })
        {
            var caller = new StepCaller(host.AgentCache.Get("triage", Step, description: null), limit: 2, calls: 2, streaming);

            _ = await Parent(host, caller).RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);

            caller.HandoffCallsDelivered.ShouldBe(2);
        }

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

        runs.ShouldAllBe(static run => run.Status == RunStatus.Completed);
    }

    [Fact]
    public async Task A_stream_that_does_not_end_after_the_call_past_the_limit_still_fails_the_run_with_the_limit()
    {
        // Reading the stream to its end is bounded by the step's wait limits. When the child never ends, the limit is
        // still the failure: a wait limit met on the way is not the reason the run stopped.
        var host = new WorkflowTestHost();
        host.AddAgent(Step, new HandoffAskingAgent(Step) { EndsAfterTheCall = false, CallsPerAnswer = 2 });

        var cache = new WorkflowAgentCache(
            host.Resolver,
            host.TenantContext,
            NullLoggerFactory.Instance,
            Options.Create(new TraconOptions
            {
                AgentGraph = { ChildDeadline = TimeSpan.FromMilliseconds(200), WaitTimeout = TimeSpan.FromSeconds(5) },
            }));

        // Two handoff calls in one answer count as two: the first and only answer crosses the limit of 1.
        var caller = new StepCaller(cache.Get("triage", Step, description: null), limit: 1, calls: 1, streaming: true);

        await Should.ThrowAsync<TraconHandoffLimitExceededException>(
            async () => await Parent(host, caller).RunAsync("go", cancellationToken: TestContext.Current.CancellationToken));

        caller.HandoffCallsDelivered.ShouldBe(0);
    }

    private static RunRecordingAgent Parent(WorkflowTestHost host, StepCaller caller)
        => TestRunRecording.Agent(
            caller,
            host.RunStore,
            host.TenantContext,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);

    /// <summary>
    /// The run above the steps: puts a handoff counter on its own scope, as the workflow runner does, and calls the
    /// step wrapper the number of times asked for, one answer or one stream each.
    /// </summary>
    private sealed class StepCaller(
        ChildAgentInvoker step,
        int limit,
        int calls,
        bool streaming) : AIAgent
    {
        public override string Name => "triage";

        /// <summary>Gets how many handoff calls reached this caller, which stands for the framework that acts on them.</summary>
        public int HandoffCallsDelivered { get; private set; }

        protected override async Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // Set in this method's own body, before the first await: the step reads the scope from the ambient value.
            var counter = new HandoffLimit(limit);
            TraconRunContext.SetCurrent(TraconRunContext.Current! with { Handoffs = counter });

            for (var index = 0; index < calls; index++)
            {
                if (streaming)
                {
                    await foreach (var update in step.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
                    {
                        HandoffCallsDelivered += update.Contents.OfType<FunctionCallContent>().Count();
                    }
                }
                else
                {
                    var response = await step.RunAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

                    HandoffCallsDelivered += response.Messages.SelectMany(static message => message.Contents).OfType<FunctionCallContent>().Count();
                }
            }

            return new AgentResponse(new ChatMessage(ChatRole.Assistant, "done"));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                yield return new AgentResponseUpdate(message.Role, message.Contents);
            }
        }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new CallerSession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new CallerSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(JsonDocument.Parse("{}").RootElement.Clone());

        private sealed class CallerSession : AgentSession;
    }

    /// <summary>A step's agent that answers with a handoff call.</summary>
    private sealed class HandoffAskingAgent(string name) : AIAgent
    {
        public override string Name => name;

        /// <summary>Gets or sets whether the stream ends after the handoff call. Otherwise it waits for cancellation.</summary>
        public bool EndsAfterTheCall { get; init; } = true;

        /// <summary>Gets or sets how many handoff calls one answer holds.</summary>
        public int CallsPerAnswer { get; init; } = 1;

        /// <summary>Gets or sets whether the answer is a request for a tool approval instead of a handoff call.</summary>
        public bool AsksForApproval { get; init; }

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, Calls())));

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, Calls());

            // What a provider sends after the call: the end of the turn, with the usage.
            yield return new AgentResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2 })]);

            if (!EndsAfterTheCall)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }
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

        private List<AIContent> Calls()
            => AsksForApproval
                ? [new ToolApprovalRequestContent("request-1", new FunctionCallContent("call-1", "cancel_order"))]
                : [.. Enumerable.Range(0, CallsPerAnswer).Select(static _ => (AIContent)new FunctionCallContent(Guid.NewGuid().ToString("N"), HandoffLimit.ToolPrefix + "1"))];

        private sealed class AskingSession : AgentSession;
    }
}
