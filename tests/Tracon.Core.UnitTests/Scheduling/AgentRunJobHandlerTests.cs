using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>Tests for <see cref="AgentRunJobHandler"/>.</summary>
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
        // row AT enqueue time (AgentEndpoints.RunQueuedAsync).
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
    /// A registered <see cref="IToolApprovalPresenter"/> reaches the
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
    /// The fields the HTTP layer writes at enqueue time reach the
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

    /// <summary>
    /// The labels the HTTP layer resolved travel in the payload with the
    /// user, and the run (with every sub-agent and tool under it) reads both
    /// from the scope the worker opens.
    /// </summary>
    [Fact]
    public async Task The_payloads_user_and_labels_open_the_scope_the_run_reads()
    {
        var agent = new ScriptedAgent();
        var handler = NewHandler(new SingleAgentCatalog(agent));
        var runId = TraconId.NewId();

        await handler.ExecuteAsync(BuildContext(runId, "hello", payload: new
        {
            runId = runId.ToString(),
            message = "hello",
            userId = "ada",
            labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" },
        }));

        agent.SeenUserId.ShouldBe("ada");
        agent.SeenLabels.ShouldNotBeNull()["team"].ShouldBe("payments");
    }

    /// <summary>
    /// A payload is durable data a schedule can write by hand. Labels that are
    /// not an object of strings, or that break a limit, are dropped rather
    /// than thrown on: throwing would fail the job on every attempt, and labels
    /// only describe the run.
    /// </summary>
    [Theory]
    [InlineData("not-an-object")]
    [InlineData("a-value-that-is-not-a-string")]
    [InlineData("more-labels-than-allowed")]
    public async Task Malformed_payload_labels_are_dropped_and_the_run_still_runs(string shape)
    {
        var agent = new ScriptedAgent();
        var handler = NewHandler(new SingleAgentCatalog(agent));
        var runId = TraconId.NewId();

        object labels = shape switch
        {
            "not-an-object" => "team=payments",
            "a-value-that-is-not-a-string" => new Dictionary<string, object>(StringComparer.Ordinal) { ["team"] = 7 },
            _ => Enumerable.Range(0, RunLabels.MaxCount + 1)
                .ToDictionary(static index => $"key-{index}", static _ => "value", StringComparer.Ordinal),
        };

        await handler.ExecuteAsync(BuildContext(runId, "hello", payload: new
        {
            runId = runId.ToString(),
            message = "hello",
            userId = "ada",
            labels,
        }));

        agent.Inputs.ShouldBe(["hello"]);
        agent.SeenUserId.ShouldBe("ada");
        agent.SeenLabels.ShouldBeNull();
    }

    /// <summary>
    /// A store error on the session save after the model answered is
    /// tried again while the worker can prove its lease; the turn is not.
    /// </summary>
    [Fact]
    public async Task A_session_save_that_fails_once_is_tried_again_and_the_turn_is_not()
    {
        var store = new FlakySessionStore(failures: 1, () => new TimeoutException("The store did not answer in time."));
        var agent = new ScriptedAgent();
        var handler = NewHandler(new SingleAgentCatalog(agent), sessionStore: store);
        var runId = TraconId.NewId();

        await handler.ExecuteAsync(BuildContext(runId, "hello", sessionId: "s-1", provableLease: TimeSpan.FromMinutes(1)));

        agent.Inputs.ShouldBe(["hello"]);
        store.CreateCalls.ShouldBe(2);
        (await store.GetAsync("s-1")).ShouldNotBeNull();
    }

    /// <summary>The window is the lease the worker can prove, fixed at the first error.</summary>
    [Fact]
    public async Task A_session_store_that_keeps_failing_is_given_up_on_when_the_lease_window_closes()
    {
        var store = new FlakySessionStore(failures: int.MaxValue, () => new TimeoutException("The store did not answer in time."));
        var agent = new ScriptedAgent();
        var handler = NewHandler(new SingleAgentCatalog(agent), sessionStore: store);
        var runId = TraconId.NewId();

        var failure = await Should.ThrowAsync<NonRetryableJobException>(() => handler.ExecuteAsync(
            BuildContext(runId, "hello", sessionId: "s-1", provableLease: TimeSpan.FromMilliseconds(500))).AsTask());

        failure.Code.ShouldBe(QueuedTurn.SessionSaveFailedCode);
        failure.Message.ShouldStartWith(QueuedTurn.SessionSaveFailedCode);
        failure.InnerException.ShouldBeOfType<TimeoutException>();
        store.CreateCalls.ShouldBeGreaterThan(1);
        agent.Inputs.ShouldBe(["hello"]);
    }

    /// <summary>
    /// Tracon's own refusal (a turn that conflicts with another, an owner that
    /// cannot be resolved) gets the same answer on every try.
    /// </summary>
    [Fact]
    public async Task A_session_save_Tracon_refuses_is_not_tried_again()
    {
        var store = new FlakySessionStore(
            failures: int.MaxValue,
            () => new TraconSessionConflictException("Another request saved this session first."));
        var handler = NewHandler(new SingleAgentCatalog(new ScriptedAgent()), sessionStore: store);
        var runId = TraconId.NewId();

        var failure = await Should.ThrowAsync<NonRetryableJobException>(() => handler.ExecuteAsync(
            BuildContext(runId, "hello", sessionId: "s-1", provableLease: TimeSpan.FromMinutes(1))).AsTask());

        failure.InnerException.ShouldBeOfType<TraconSessionConflictException>();
        store.CreateCalls.ShouldBe(1);
    }

    /// <summary>
    /// A turn that asks for approval saves its session before the approval is
    /// listed. When that save is given up on, nothing is listed, and the run
    /// closes Failed with the job's code: an approval without its session
    /// cannot be answered.
    /// </summary>
    [Fact]
    public async Task An_approval_whose_session_cannot_be_saved_is_not_listed_and_its_run_fails_with_the_code()
    {
        var runs = new InMemoryRunStore();
        var approvals = new InMemoryPendingApprovalStore();
        var store = new FlakySessionStore(failures: int.MaxValue, () => new TimeoutException("The store did not answer in time."));

        var recordingAgent = new RunRecordingAgent(
            new ApprovalRequestingAgent(),
            runs,
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);

        var handler = new AgentRunJobHandler(
            new SingleAgentCatalog(recordingAgent),
            new AgentSessionManager(store, FixedTenantContext.Default),
            runs,
            approvals,
            new InMemoryExperimentStore(),
            Options.Create(new TraconOptions()),
            timeProvider: null,
            logger: NullLogger<AgentRunJobHandler>.Instance);

        var runId = TraconId.NewId();

        await Should.ThrowAsync<NonRetryableJobException>(
            () => handler.ExecuteAsync(BuildContext(runId, "cancel it", sessionId: "session-1")).AsTask());

        (await approvals.ListPendingAsync()).ShouldBeEmpty();

        var run = (await runs.GetRunAsync(runId)).ShouldNotBeNull();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(QueuedTurn.SessionSaveFailedCode);
    }

    /// <summary>Without a worker there is no lease to prove, so the save is tried once.</summary>
    [Fact]
    public async Task Without_a_lease_a_failed_session_save_is_tried_once()
    {
        var store = new FlakySessionStore(failures: 1, () => new TimeoutException("The store did not answer in time."));
        var handler = NewHandler(new SingleAgentCatalog(new ScriptedAgent()), sessionStore: store);
        var runId = TraconId.NewId();

        await Should.ThrowAsync<NonRetryableJobException>(
            () => handler.ExecuteAsync(BuildContext(runId, "hello", sessionId: "s-1")).AsTask());

        store.CreateCalls.ShouldBe(1);
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
    /// An administrator can schedule an AgentRun job with a hand-written
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
        IExperimentStore? experiments = null,
        ISessionStore? sessionStore = null)
        => new(
            catalog,
            new AgentSessionManager(sessionStore ?? new InMemorySessionStore(), FixedTenantContext.Default),
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
        object? payload = null,
        TimeSpan? provableLease = null)
        => new()
        {
            // What the worker reports: the lease it can prove, less its margin.
            ProvableLeaseRemaining = provableLease is { } lease ? () => lease : null,
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

    /// <summary>
    /// Fails the first <paramref name="failures"/> session creations, then
    /// writes through to an in-memory store.
    /// </summary>
    private sealed class FlakySessionStore(int failures, Func<Exception> fault) : ISessionStore
    {
        private readonly InMemorySessionStore _inner = new();
        private int _createCalls;

        public int CreateCalls => Volatile.Read(ref _createCalls);

        public ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _createCalls) <= failures
                ? throw fault()
                : ((ISessionStore)_inner).TryCreateAsync(record, cancellationToken);

        public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => _inner.SaveAsync(record, cancellationToken);

        public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.GetAsync(sessionId, cancellationToken);

        public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(sessionId, cancellationToken);

        public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
            => _inner.QueryAsync(query, cancellationToken);
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

        /// <summary>The ambient user the run saw: what a sub-agent or a tool under it reads.</summary>
        public string? SeenUserId { get; private set; }

        /// <summary>The ambient labels the run saw.</summary>
        public IReadOnlyDictionary<string, string>? SeenLabels { get; private set; }

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
            SeenUserId = AmbientRunAttributionScope.CurrentUserId;
            SeenLabels = AmbientRunAttributionScope.CurrentLabels;

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
