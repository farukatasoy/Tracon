using System.Runtime.CompilerServices;
using System.Text.Json;
using Tracon.Workflows.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Cancellation of a workflow run (phase 32, defect F-107).
/// </summary>
/// <remarks>
/// 🚨 Measured on 2026-08-18: the graph built by MAF's
/// <c>AgentWorkflowBuilder</c> does NOT honor an external cancellation token in
/// the middle of a running step. <c>WatchStreamAsync</c> finishes normally, so
/// before the fix a canceled run was recorded as <see cref="RunStatus.Completed"/>
/// with <c>error: null</c> — the caller was told the opposite of what happened
/// while still paying the full cost. The runner therefore enforces cancellation
/// itself at every super-step boundary.
/// </remarks>
public sealed class WorkflowCancellationTests
{
    [Fact]
    public async Task Cancellation_during_a_step_records_Canceled_not_Completed()
    {
        using var trigger = new CancellationTokenSource();

        var host = new WorkflowTestHost("second");
        host.AddAgent("first", new CancelingAgent("first", trigger));

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["first", "second"],
        });

        var runner = host.CreateRunner();
        var events = new List<RunEvent>();

        try
        {
            await foreach (var runEvent in runner.RunStreamingAsync(
                new WorkflowRunRequest { WorkflowName = "chain", Message = "hello" },
                trigger.Token))
            {
                events.Add(runEvent);
            }
        }
        catch (OperationCanceledException)
        {
            // The enumerator may also surface the cancellation; either way the
            // recorded status is what the caller reads back over HTTP.
        }

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task Run_that_is_not_canceled_still_completes()
    {
        // The guard must not fire on its own: the same shape without a
        // cancellation request keeps the previous behavior exactly.
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["one", "two"],
        });

        var runner = host.CreateRunner();

        await foreach (var _ in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = "chain", Message = "hello" }))
        {
        }

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Completed);
    }

    /// <summary>
    /// Agent that requests cancellation while it runs and then returns
    /// normally, exactly like a step that ignores the token.
    /// </summary>
    private sealed class CancelingAgent(string name, CancellationTokenSource trigger) : AIAgent
    {
        public override string Name => name;

        public override string Description => $"Cancels the run, then answers anyway.";

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // The caller pressed cancel while this step was executing.
            trigger.Cancel();

            // The step ignores the token and answers anyway.
            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, $"[{name}]")));
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
            => new(new EmptySession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new EmptySession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(JsonDocument.Parse("{}").RootElement.Clone());

        private sealed class EmptySession : AgentSession;
    }
}
