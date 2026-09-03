using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Scheduling;

public sealed class AgentBatchJobHandlerTests
{
    [Fact]
    public async Task Throws_when_the_agent_is_not_found()
    {
        var handler = new AgentBatchJobHandler(new SingleAgentCatalog(null), NullLogger<AgentBatchJobHandler>.Instance);

        await Should.ThrowAsync<AgentPrismException>(
            () => handler.ExecuteAsync(BuildContext([Item(0, "input")])).AsTask());
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

    private static JobItemRecord Item(int seq, string input, JobItemStatus status = JobItemStatus.Pending)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = seq, Input = input, Status = status };

    private static JobContext BuildContext(
        IReadOnlyList<JobItemRecord> items,
        List<JobItemResult>? reported = null,
        int? cancelAfter = null)
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
            ReportItemAsync = (result, _) =>
            {
                reported.Add(result);
                return default;
            },
            IsCancelledAsync = _ =>
            {
                calls++;
                return new ValueTask<bool>(cancelAfter is { } limit && calls > limit);
            },
        };
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
