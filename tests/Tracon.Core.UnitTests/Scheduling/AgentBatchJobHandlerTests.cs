using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Scheduling;

public sealed class AgentBatchJobHandlerTests
{
    [Fact]
    public async Task Throws_when_the_agent_is_not_found()
    {
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(null), NullLogger<AgentBatchJobHandler>.Instance);

        await Should.ThrowAsync<TraconException>(
            () => handler.ExecuteAsync(BuildContext([Item(0, "input")])).AsTask());
    }

    [Fact]
    public async Task An_agent_whose_required_parameter_has_no_value_fails_the_job_before_any_item()
    {
        // F-330: a batch carries no parameter values. The job fails once, up
        // front, instead of once per item with the same error.
        var agent = new ScriptedAgent();
        var guarded = new UnboundParameterGuard(
            agent,
            "billing-agent",
            AgentParameterValidator.ValidateValues(
                [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }],
                values: null));
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(guarded), NullLogger<AgentBatchJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        var exception = await Should.ThrowAsync<TraconException>(
            () => handler.ExecuteAsync(BuildContext([Item(0, "first"), Item(1, "second")], reported)).AsTask());

        exception.Message.ShouldContain("'customer'", Case.Sensitive);
        agent.Inputs.ShouldBeEmpty();
        reported.ShouldBeEmpty();
    }

    [Fact]
    public async Task Items_are_processed_in_order_and_results_are_reported()
    {
        var agent = new ScriptedAgent(fail: "boom");
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(agent), NullLogger<AgentBatchJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        var context = BuildContext(
            [Item(0, "first"), Item(1, "boom"), Item(2, "third")],
            reported);

        await handler.ExecuteAsync(context);

        agent.Inputs.ShouldBe(["first", "boom", "third"]);
        reported.Count.ShouldBe(3);
        reported[0].Status.ShouldBe(JobItemStatus.Completed);
        reported[0].RunId.ShouldNotBeNull();
        reported[1].Status.ShouldBe(JobItemStatus.Failed);
        reported[1].Error.ShouldNotBeNullOrWhiteSpace();
        reported[2].Status.ShouldBe(JobItemStatus.Completed);
    }

    [Fact]
    public async Task Previously_processed_items_are_not_rerun()
    {
        // When the lease expires and the job is reclaimed, only Pending items are processed.
        var agent = new ScriptedAgent();
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(agent), NullLogger<AgentBatchJobHandler>.Instance);

        var context = BuildContext(
        [
            Item(0, "old", JobItemStatus.Completed),
            Item(1, "new"),
        ]);

        await handler.ExecuteAsync(context);

        agent.Inputs.ShouldBe(["new"]);
    }

    [Fact]
    public async Task A_canceled_job_stops_between_items()
    {
        var agent = new ScriptedAgent();
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(agent), NullLogger<AgentBatchJobHandler>.Instance);

        var context = BuildContext(
            [Item(0, "first"), Item(1, "second")],
            cancelAfter: 1);

        await handler.ExecuteAsync(context);

        agent.Inputs.ShouldBe(["first"]);
    }

    /// <summary>
    /// F-310: a batch can run for hours. The handler resolved its agent once, and an
    /// agent's MCP tools stay bound to the connection they were discovered on: after a
    /// server edit or delete, every later item called a closed connection. Each item
    /// now runs the agent the catalog resolves at that moment (a cache hit while
    /// nothing changed).
    /// </summary>
    [Fact]
    public async Task Each_item_runs_the_agent_the_catalog_resolves_when_the_item_starts()
    {
        var before = new ScriptedAgent();
        var after = new ScriptedAgent();

        // The first resolve checks that the job's agent exists at all.
        var catalog = new SequenceCatalog(before, before, after);
        var handler = new AgentBatchJobHandler(catalog, NullLogger<AgentBatchJobHandler>.Instance);

        await handler.ExecuteAsync(BuildContext([Item(0, "first"), Item(1, "second")]));

        before.Inputs.ShouldBe(["first"]);
        after.Inputs.ShouldBe(["second"]);
    }

    [Fact]
    public async Task An_item_whose_agent_no_longer_resolves_fails_on_its_own()
    {
        var agent = new ScriptedAgent();
        var catalog = new SequenceCatalog(agent, null, agent);
        var handler = new AgentBatchJobHandler(catalog, NullLogger<AgentBatchJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext([Item(0, "first"), Item(1, "second")], reported));

        reported.Select(static result => result.Status).ShouldBe([JobItemStatus.Failed, JobItemStatus.Completed]);
        agent.Inputs.ShouldBe(["second"]);
    }

    /// <summary>
    /// F-348: the report of a completed item sat in the item's own <c>try</c>.
    /// A store error while reporting it was taken for a run failure, and an
    /// item whose run had completed was recorded as Failed.
    /// </summary>
    [Fact]
    public async Task A_store_error_while_reporting_a_completed_item_does_not_record_it_as_failed()
    {
        var agent = new ScriptedAgent();
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(agent), NullLogger<AgentBatchJobHandler>.Instance);
        var reported = new List<JobItemResult>();
        var reports = 0;

        var context = BuildContext(
            [Item(0, "first")],
            reportItem: (result, _) =>
            {
                if (++reports == 1)
                {
                    throw new TimeoutException("The store did not answer in time.");
                }

                reported.Add(result);
                return default;
            });

        // The report error ends the attempt; the worker decides whether to retry it.
        await Should.ThrowAsync<TimeoutException>(() => handler.ExecuteAsync(context).AsTask());

        agent.Inputs.ShouldBe(["first"]);
        reported.ShouldNotContain(
            result => result.Status == JobItemStatus.Failed,
            "an item whose run completed was recorded as failed");
    }

    private static JobItemRecord Item(int seq, string input, JobItemStatus status = JobItemStatus.Pending)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = seq, Input = input, Status = status };

    private static JobContext BuildContext(
        IReadOnlyList<JobItemRecord> items,
        List<JobItemResult>? reported = null,
        int? cancelAfter = null,
        Func<JobItemResult, CancellationToken, ValueTask>? reportItem = null)
    {
        reported ??= [];
        var calls = 0;

        return new JobContext
        {
            Job = new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant",
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "summarizer",
                Status = JobStatus.Running,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = items,
            ReportItemAsync = reportItem ?? ((result, _) =>
            {
                reported.Add(result);
                return default;
            }),
            IsCancelledAsync = _ =>
            {
                calls++;
                return new ValueTask<bool>(cancelAfter is { } limit && calls > limit);
            },
        };
    }

    /// <summary>Answers each resolve with the next agent of the sequence; the last one repeats.</summary>
    private sealed class SequenceCatalog(params AIAgent?[] agents) : IAgentCatalog
    {
        private int _next;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => new(agents[Math.Min(_next++, agents.Length - 1)]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => ResolveAsync(agentName, culture, cancellationToken);
    }

    private sealed class SingleAgentCatalog(AIAgent? agent) : IAgentCatalog
    {
        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => new(agent);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => new(agent);
    }

    /// <summary>The smallest possible fake agent, which throws when its input matches <paramref name="fail"/>.</summary>
    private sealed class ScriptedAgent(string? fail = null) : AIAgent
    {
        public List<string> Inputs { get; } = [];

        public override string Name => "fake-agent";

        public override string Description => "for tests";

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var last = messages.LastOrDefault()?.Text ?? string.Empty;
            Inputs.Add(last);

            if (fail is not null && string.Equals(last, fail, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"'{last}' input deliberately failed.");
            }

            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, $"ok:{last}")));
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
            => new(new ScriptedSession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new ScriptedSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(EmptyState);

        private static JsonElement EmptyState { get; } = JsonDocument.Parse("{}").RootElement.Clone();

        private sealed class ScriptedSession : AgentSession;
    }
}
