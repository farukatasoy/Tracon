using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// An agent node that pins a version runs that version, and only that step (phase 200, failure
/// modes 14 and 15).
/// </summary>
public sealed class WorkflowNodeAgentVersionTests
{
    [Fact]
    public async Task A_pinned_step_asks_the_catalog_for_its_version_and_the_others_for_the_current_one()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(Chain(Pinned("one", 3), Pinned("two", null)));
        await Run(host.CreateRunner());

        host.ResolveCalls.ToArray().ShouldContain(("one", (int?)3));
        host.ResolveCalls.ToArray().ShouldContain(("two", (int?)null));
        host.ResolveCalls.ToArray().ShouldNotContain(("one", (int?)null), "a pinned step never resolves the current version");
    }

    [Fact]
    public async Task The_pinned_version_answers_and_its_child_row_records_that_version()
    {
        var host = new WorkflowTestHost("one", "two");

        host.AddAgentVersion("one", 1, new TaggingAgent("one", "v1"));
        host.AddAgentVersion("one", 2, new TaggingAgent("one", "v2"));

        await host.SaveAsync(Chain(Pinned("one", 1), Pinned("two", null)));

        var events = await Run(host.CreateRunner());

        events.Single(static runEvent => runEvent.Type == RunEventType.WorkflowOutput).Text!
            .ShouldContain("[one:v1]", Case.Sensitive);

        var rows = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

        rows.Single(static run => run.Kind == RunKind.Agent && string.Equals(run.AgentName, "one", StringComparison.Ordinal)).AgentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_step_with_no_pin_never_asks_for_a_version()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(Chain(Pinned("one", null), Pinned("two", null)));
        await Run(host.CreateRunner());

        host.ResolveCalls.ToArray().ShouldAllBe(static call => call.Version == null);
    }

    [Fact]
    public async Task The_same_agent_pinned_to_two_versions_is_two_wrappers_with_one_executor_id()
    {
        // The cache key carries the pin because the wrapper takes it in its constructor; the
        // identity never does, or pinning would strand every waiting run.
        var host = new WorkflowTestHost("one");

        var current = host.AgentCache.Get("wf", "one", null);
        var first = host.AgentCache.Get("wf", "one", null, timeout: null, agentVersion: 1);
        var second = host.AgentCache.Get("wf", "one", null, timeout: null, agentVersion: 2);

        first.ShouldNotBeSameAs(second);
        first.ShouldNotBeSameAs(current);
        host.AgentCache.Get("wf", "one", null, timeout: null, agentVersion: 1).ShouldBeSameAs(first);

        first.Id.ShouldBe(second.Id);
        first.Id.ShouldBe(current.Id);
    }

    private static WorkflowDefinition Chain(params WorkflowNodeReference[] nodes)
        => new() { Name = "chain", Kind = WorkflowKind.Sequential, Nodes = nodes };

    private static WorkflowNodeReference Pinned(string name, int? version)
        => new() { Name = name, Kind = WorkflowNodeKind.Agent, AgentVersion = version };

    private static async Task<List<RunEvent>> Run(WorkflowRunner runner)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "chain", Message = "go" }))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>An agent that tags the text it receives with the version it answers as.</summary>
    private sealed class TaggingAgent(string name, string tag) : Microsoft.Agents.AI.AIAgent
    {
        public override string Name => name;

        protected override Task<Microsoft.Agents.AI.AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            Microsoft.Agents.AI.AgentSession? session = null,
            Microsoft.Agents.AI.AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new Microsoft.Agents.AI.AgentResponse(
                new ChatMessage(ChatRole.Assistant, $"[{name}:{tag}]{messages.LastOrDefault()?.Text}")));

        protected override async IAsyncEnumerable<Microsoft.Agents.AI.AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            Microsoft.Agents.AI.AgentSession? session = null,
            Microsoft.Agents.AI.AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                yield return new Microsoft.Agents.AI.AgentResponseUpdate(message.Role, message.Contents);
            }
        }

        protected override ValueTask<Microsoft.Agents.AI.AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new TaggingSession());

        protected override ValueTask<Microsoft.Agents.AI.AgentSession> DeserializeSessionCoreAsync(
            System.Text.Json.JsonElement serializedState,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new TaggingSession());

        protected override ValueTask<System.Text.Json.JsonElement> SerializeSessionCoreAsync(
            Microsoft.Agents.AI.AgentSession session,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone());

        private sealed class TaggingSession : Microsoft.Agents.AI.AgentSession;
    }
}
