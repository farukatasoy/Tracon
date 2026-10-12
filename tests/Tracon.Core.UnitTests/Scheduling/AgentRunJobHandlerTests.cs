using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>Tests for <see cref="AgentRunJobHandler"/> (Phase 46).</summary>
public sealed class AgentRunJobHandlerTests
{
    [Fact]
    public async Task A_successful_run_uses_the_pre_assigned_id()
    {
        var runs = new InMemoryRunStore();
        var agent = new ScriptedAgent();
        var handler = NewHandler(new SingleAgentCatalog(agent), runs);
        var runId = TraconId.NewId();

        await handler.ExecuteAsync(BuildContext(runId, "hello"));

        agent.Inputs.ShouldBe(["hello"]);
        agent.LastRunId.ShouldBe(runId);
    }

    [Fact]
    public async Task When_the_agent_is_not_found_the_queued_row_is_closed_as_Failed()
    {
        var runs = new InMemoryRunStore();
        var handler = NewHandler(new SingleAgentCatalog(null), runs);
        var runId = TraconId.NewId();

        // The placeholder row that promises the 202: the HTTP layer writes such a
        // row AT enqueue time (Phase 46, AgentEndpoints.RunQueuedAsync).
        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "missing-agent",
            Status = RunStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await Should.ThrowAsync<TraconException>(
            () => handler.ExecuteAsync(BuildContext(runId, "hello", targetName: "missing-agent")).AsTask());

        var record = await runs.GetRunAsync(runId);
        record.ShouldNotBeNull();
        record!.Status.ShouldBe(RunStatus.Failed);
        record.Error.ShouldNotBeNull();
    }

    /// <summary>
    /// Phase 142: a registered <see cref="IToolApprovalPresenter"/> reaches the
    /// PERSISTED <see cref="PendingApproval"/> row, not just the closing run event.
    /// This is the fast, isolated proof the plan's own test matrix names
    /// (<c>QueuedApprovalTests</c>) — the DI + queue + store boundary the
    /// end-to-end screenshot test exercises too, but without a browser or an
    /// HTTP round trip.
    /// </summary>
    [Fact]
    public async Task A_registered_presenter_reaches_the_persisted_PendingApproval_row()
    {
        var runs = new InMemoryRunStore();
        var approvals = new InMemoryPendingApprovalStore();
        var sessions = new AgentSessionManager(new InMemorySessionStore(), FixedTenantContext.Default);

        var presentation = new ToolApprovalPresentation
        {
            EntityType = "order",
            EntityId = "ORD-1",
            EntityName = "Order ORD-1",
        };

        var presenterRunner = new ToolApprovalPresenterRunner(
            new StubToolApprovalPresenter(presentation),
            TestData.DefaultOptionsMonitor(),
            NullLogger<ToolApprovalPresenterRunner>.Instance);

        var recordingAgent = new RunRecordingAgent(
            new ApprovalRequestingAgent(),
            runs,
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            approvalPresenterRunner: presenterRunner);

        var handler = new AgentRunJobHandler(
            new SingleAgentCatalog(recordingAgent),
            sessions,
            runs,
            approvals,
            new InMemoryExperimentStore(),
            Options.Create(new TraconOptions()),
            timeProvider: null,
            logger: NullLogger<AgentRunJobHandler>.Instance);

        var runId = TraconId.NewId();

        await handler.ExecuteAsync(BuildContext(runId, "cancel it", sessionId: "session-1"));

        var pending = (await approvals.ListPendingAsync()).ShouldHaveSingleItem();

        pending.RunId.ShouldBe(runId);
        pending.Arguments.ShouldNotBeNull();
        pending.Presentation.ShouldNotBeNull();
        pending.Presentation.ShouldBe(presentation);
    }

    [Fact]
    public async Task Invalid_payload_throws()
    {
        var handler = NewHandler(new SingleAgentCatalog(new ScriptedAgent()));

        var context = new JobContext
        {
            Job = new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                HandlerKey = JobHandlerKeys.AgentRun,
                TargetName = "fake-agent",
                Status = JobStatus.Running,
                Payload = JsonDocument.Parse("""{"runId":"invalid"}""").RootElement,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = [],
            ReportItemAsync = (_, _) => default,
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };

        await Should.ThrowAsync<TraconException>(() => handler.ExecuteAsync(context).AsTask());
    }

    /// <summary>
    /// F-351: the fields the HTTP layer writes at enqueue time reach the
    /// catalog (version, culture) and the run (version, arm).
    /// </summary>
    [Fact]
    public async Task The_payloads_culture_version_and_arm_reach_the_catalog_and_the_run()
    {
        var experiments = new InMemoryExperimentStore();
        var experiment = await StartExperimentAsync(experiments, agentName: "fake-agent");
        var agent = new ScriptedAgent();
        var catalog = new SingleAgentCatalog(agent);
        var handler = NewHandler(catalog, experiments: experiments);
        var runId = TraconId.NewId();

        await handler.ExecuteAsync(BuildContext(runId, "hello", payload: new
        {
            runId = runId.ToString(),
            message = "hello",
            culture = "tr",
            agentVersion = 1,
            experimentId = experiment.Id.ToString(),
            variant = "control",
        }));

        catalog.Resolutions.ShouldHaveSingleItem().ShouldBe(((int?)1, (string?)"tr"));
        agent.LastOptions.ShouldNotBeNull();
        agent.LastOptions.AgentVersion.ShouldBe(1);
        agent.LastOptions.ExperimentId.ShouldBe(experiment.Id);
        agent.LastOptions.Variant.ShouldBe("control");
    }

    /// <summary>A job enqueued before the fields existed keeps its earlier behavior.</summary>
    [Fact]
    public async Task A_payload_without_the_new_fields_resolves_the_current_version_with_the_default_culture()
    {
        var agent = new ScriptedAgent();
        var catalog = new SingleAgentCatalog(agent);
        var handler = NewHandler(catalog);
        var runId = TraconId.NewId();

        await handler.ExecuteAsync(BuildContext(runId, "hello"));

        catalog.Resolutions.ShouldHaveSingleItem().ShouldBe(((int?)null, (string?)null));
        agent.LastOptions.ShouldNotBeNull();
        agent.LastOptions.AgentVersion.ShouldBeNull();
        agent.LastOptions.ExperimentId.ShouldBeNull();
        agent.LastOptions.Variant.ShouldBeNull();
    }

    /// <summary>A version without an arm is a pinned run, not an experiment run.</summary>
    [Fact]
    public async Task A_version_without_an_arm_is_a_pinned_run()
    {
        var agent = new ScriptedAgent();
        var catalog = new SingleAgentCatalog(agent);
        var handler = NewHandler(catalog);
        var runId = TraconId.NewId();

        await handler.ExecuteAsync(BuildContext(runId, "hello", payload: new
        {
            runId = runId.ToString(),
            message = "hello",
            agentVersion = 3,
        }));

        catalog.Resolutions.ShouldHaveSingleItem().ShouldBe(((int?)3, (string?)null));
        agent.LastOptions!.AgentVersion.ShouldBe(3);
        agent.LastOptions.ExperimentId.ShouldBeNull();
    }

    /// <summary>
    /// F-351: an administrator can schedule an AgentRun job with a hand-written
    /// payload; an arm that does not match a started experiment of this tenant
    /// for this agent, with this exact version, refuses the run.
    /// </summary>
    [Theory]
    [InlineData("version-of-another-variant")]
    [InlineData("unknown-variant")]
    [InlineData("another-agents-experiment")]
    [InlineData("another-tenants-experiment")]
    [InlineData("unknown-experiment")]
    [InlineData("draft-experiment")]
    [InlineData("arm-without-version")]
    [InlineData("variant-without-experiment")]
    [InlineData("malformed-version")]
    public async Task A_forged_arm_fails_closed_and_never_runs_the_agent(string forgery)
    {
        var experiments = new InMemoryExperimentStore();
        var own = await StartExperimentAsync(experiments, agentName: "fake-agent");
        var otherAgent = await StartExperimentAsync(experiments, agentName: "other-agent", name: "other-agent-experiment");
        var otherTenant = await StartExperimentAsync(experiments, agentName: "fake-agent", tenantId: "tenant-b");
        var draft = await experiments.SaveAsync(NewExperiment("fake-agent", "draft-experiment", "default"));

        // The honest arm is (1, own experiment, "control"); each case breaks one part.
        var version = forgery switch
        {
            "version-of-another-variant" => 2,
            "arm-without-version" => null,
            "malformed-version" => "1",
            _ => (object?)1,
        };

        var experimentId = forgery switch
        {
            "another-agents-experiment" => otherAgent.Id.ToString(),
            "another-tenants-experiment" => otherTenant.Id.ToString(),
            "unknown-experiment" => Guid.NewGuid().ToString(),
            "draft-experiment" => draft.Id.ToString(),
            "variant-without-experiment" => null,
            _ => own.Id.ToString(),
        };

        var variant = string.Equals(forgery, "unknown-variant", StringComparison.Ordinal) ? "missing" : "control";

        var runs = new InMemoryRunStore();
        var agent = new ScriptedAgent();
        var handler = NewHandler(new SingleAgentCatalog(agent), runs, experiments);
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "fake-agent",
            Status = RunStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await Should.ThrowAsync<TraconException>(() => handler.ExecuteAsync(BuildContext(runId, "hello", payload: new
        {
            runId = runId.ToString(),
            message = "hello",
            agentVersion = version,
            experimentId,
            variant,
        })).AsTask());

        agent.Inputs.ShouldBeEmpty();

        var record = (await runs.GetRunAsync(runId)).ShouldNotBeNull();
        record.Status.ShouldBe(RunStatus.Failed);
        record.ExperimentId.ShouldBeNull();
    }

    private static async Task<Experiment> StartExperimentAsync(
        InMemoryExperimentStore store,
        string agentName,
        string name = "experiment",
        string tenantId = "default")
    {
        await store.SaveAsync(NewExperiment(agentName, name, tenantId));

        return await store.StartAsync(tenantId, name);
    }

    private static Experiment NewExperiment(string agentName, string name, string tenantId)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            AgentName = agentName,
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 },
            ],
        };

    private static AgentRunJobHandler NewHandler(
        IAgentCatalog catalog,
        IRunStore? runs = null,
        IExperimentStore? experiments = null)
        => new(
            catalog,
            new AgentSessionManager(new InMemorySessionStore(), FixedTenantContext.Default),
            runs ?? new InMemoryRunStore(),
            new InMemoryPendingApprovalStore(),
            experiments ?? new InMemoryExperimentStore(),
            Options.Create(new TraconOptions()),
            timeProvider: null,
            logger: NullLogger<AgentRunJobHandler>.Instance);

    private static JobContext BuildContext(
        Guid runId,
        string message,
        string targetName = "fake-agent",
        string? sessionId = null,
        object? payload = null)
        => new()
        {
            Job = new JobRecord
            {
                Id = runId,
                TenantId = "default",
                HandlerKey = JobHandlerKeys.AgentRun,
                TargetName = targetName,
                Status = JobStatus.Running,
                Payload = JsonSerializer.SerializeToElement(payload ?? new { runId = runId.ToString(), message, sessionId }),
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = [],
            ReportItemAsync = (_, _) => default,
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };

    private sealed class SingleAgentCatalog(AIAgent? agent) : IAgentCatalog
    {
        /// <summary>The version and culture of every resolution, in order.</summary>
        public List<(int? Version, string? Culture)> Resolutions { get; } = [];

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
        {
            Resolutions.Add((null, culture));

            return new(agent);
        }

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
        {
            Resolutions.Add((version, culture));

            return new(agent);
        }
    }

    private sealed class StubToolApprovalPresenter(ToolApprovalPresentation presentation) : IToolApprovalPresenter
    {
        public ValueTask<ToolApprovalPresentation?> PresentAsync(
            ToolApprovalContext context, CancellationToken cancellationToken = default)
            => new(presentation);
    }

    /// <summary>An agent whose only response is a tool call pending approval — never text.</summary>
    private sealed class ApprovalRequestingAgent : AIAgent
    {
        public override string Name => "fake-agent";

        public override string Description => "for tests";

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var request = new ToolApprovalRequestContent(
                "req-1",
                new FunctionCallContent(
                    "call-1",
                    "cancel_order",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["orderId"] = "ORD-1" }));

            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, [request])));
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
            => new(JsonDocument.Parse("{}").RootElement.Clone());

        private sealed class ScriptedSession : AgentSession;
    }

    /// <summary>The smallest possible fake agent, which records the id it was called with and its input.</summary>
    private sealed class ScriptedAgent : AIAgent
    {
        public List<string> Inputs { get; } = [];

        public Guid? LastRunId { get; private set; }

        public TraconRunOptions? LastOptions { get; private set; }

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
            LastRunId = (options as TraconRunOptions)?.RunId;
            LastOptions = options as TraconRunOptions;

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
