using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Scheduling;

/// <summary><see cref="AgentRunJobHandler"/> testleri (Faz 46).</summary>
public sealed class AgentRunJobHandlerTests
{
    [Fact]
    public void Kind_AgentRun_dir()
    {
        NewHandler(new SingleAgentCatalog(new ScriptedAgent())).Kind.ShouldBe(JobKind.AgentRun);
    }

    [Fact]
    public async Task Basarili_calistirma_onceden_ayrilmis_kimlikle_kosar()
    {
        var runs = new InMemoryRunStore();
        var agent = new ScriptedAgent();
        var handler = NewHandler(new SingleAgentCatalog(agent), runs);
        var runId = AgentPrismId.NewId();

        await handler.ExecuteAsync(BuildContext(runId, "merhaba"));

        agent.Inputs.ShouldBe(["merhaba"]);
        agent.LastRunId.ShouldBe(runId);
    }

    [Fact]
    public async Task Agent_bulunamazsa_kuyruktaki_Queued_satiri_Failed_e_kapatilir()
    {
        var runs = new InMemoryRunStore();
        var handler = NewHandler(new SingleAgentCatalog(null), runs);
        var runId = AgentPrismId.NewId();

        // 202'nin sozunu veren yer tutucu satir: HTTP katmani enqueue ANINDA
        // boyle bir satir yazar (Faz 46, AgentEndpoints.RunQueuedAsync).
        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "yok-agent",
            Status = RunStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await Should.ThrowAsync<AgentPrismException>(
            () => handler.ExecuteAsync(BuildContext(runId, "merhaba", targetName: "yok-agent")).AsTask());

        var record = await runs.GetRunAsync(runId);
        record.ShouldNotBeNull();
        record!.Status.ShouldBe(RunStatus.Failed);
        record.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task Gecersiz_yuk_istisna_firlatir()
    {
        var handler = NewHandler(new SingleAgentCatalog(new ScriptedAgent()));

        var context = new JobContext
        {
            Job = new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                Kind = JobKind.AgentRun,
                TargetName = "sahte-agent",
                Status = JobStatus.Running,
                Payload = JsonDocument.Parse("""{"runId":"gecersiz"}""").RootElement,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = [],
            ReportItemAsync = (_, _) => default,
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };

        await Should.ThrowAsync<AgentPrismException>(() => handler.ExecuteAsync(context).AsTask());
    }

    private static AgentRunJobHandler NewHandler(IAgentCatalog catalog, IRunStore? runs = null)
        => new(
            catalog,
            new AgentSessionManager(new InMemorySessionStore(), FixedTenantContext.Default),
            runs ?? new InMemoryRunStore(),
            new InMemoryPendingApprovalStore(),
            Options.Create(new AgentPrismOptions()),
            timeProvider: null,
            logger: NullLogger<AgentRunJobHandler>.Instance);

    private static JobContext BuildContext(Guid runId, string message, string targetName = "sahte-agent")
        => new()
        {
            Job = new JobRecord
            {
                Id = runId,
                TenantId = "default",
                Kind = JobKind.AgentRun,
                TargetName = targetName,
                Status = JobStatus.Running,
                Payload = JsonSerializer.SerializeToElement(new { runId = runId.ToString(), message }),
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = [],
            ReportItemAsync = (_, _) => default,
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };

    private sealed class SingleAgentCatalog(AIAgent? agent) : IAgentCatalog
    {
        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
            => new(agent);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, CancellationToken cancellationToken = default)
            => new(agent);
    }

    /// <summary>Cagrildigi kimligi ve girdiyi kaydeden en kucuk sahte agent.</summary>
    private sealed class ScriptedAgent : AIAgent
    {
        public List<string> Inputs { get; } = [];

        public Guid? LastRunId { get; private set; }

        public override string Name => "sahte-agent";

        public override string Description => "test icin";

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var last = messages.LastOrDefault()?.Text ?? string.Empty;
            Inputs.Add(last);
            LastRunId = (options as AgentPrismRunOptions)?.RunId;

            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, $"tamam:{last}")));
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
