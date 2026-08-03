using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Workflows.UnitTests.Fakes;

/// <summary>
/// Gercek bir model olmadan workflow calistirmayi mumkun kilan en kucuk kurulum.
/// </summary>
/// <remarks>
/// Agent'lar <see cref="EchoAgent"/>'tir ve kendilerine gelen son mesaji
/// isaretleyip geri dondururler. Boylece Sequential zincirinde her halkanin
/// bir oncekinin ciktisini gordugu <em>metinden</em> dogrulanabilir.
/// </remarks>
internal sealed class WorkflowTestHost
{
    private readonly Dictionary<string, AIAgent> _agents = new(StringComparer.Ordinal);

    public WorkflowTestHost(params string[] agentNames)
    {
        TenantContext = new FixedTenantContext();
        RunStore = new InMemoryRunStore();
        DefinitionStore = new InMemoryWorkflowDefinitionStore();
        CheckpointStore = new InMemoryWorkflowCheckpointStore();

        foreach (var name in agentNames)
        {
            // Her agent kayit sarmalayicisiyla sarilir: gercek katalogda da oyle
            // gelir ve alt calistirma satirlari ancak boyle olusur.
            _agents[name] = new RunRecordingAgent(
                new EchoAgent(name),
                RunStore,
                TenantContext,
                new AgentPrismRunRecordingOptions(),
                NullLogger<RunRecordingAgent>.Instance);
        }

        Resolver = new CallableAgentResolver(new CatalogServices(_agents));
        AgentCache = new WorkflowAgentCache(Resolver, TenantContext, NullLoggerFactory.Instance);
        Compiler = new WorkflowDefinitionCompiler(Resolver, AgentCache);
    }

    public FixedTenantContext TenantContext { get; }

    public InMemoryRunStore RunStore { get; }

    public InMemoryWorkflowDefinitionStore DefinitionStore { get; }

    public InMemoryWorkflowCheckpointStore CheckpointStore { get; }

    public CallableAgentResolver Resolver { get; }

    /// <summary>Kimligi kararli agent sarmalayicilarinin onbellegi.</summary>
    public WorkflowAgentCache AgentCache { get; }

    public WorkflowDefinitionCompiler Compiler { get; }

    /// <summary>Bir kosucu kurar. Kod workflow'lari istege baglidir.</summary>
    /// <remarks>
    /// <paramref name="services"/> yalnizca kod workflow fabrikalarina gecirilir.
    /// <c>GetWorkflowAgent</c> kullanan bir fabrika gercek bir kap ister; bos
    /// bir saglayici verildiginde fabrika patlar ve calistirma sessizce
    /// bos gorunur.
    /// </remarks>
    public WorkflowRunner CreateRunner(
        Action<AgentPrismWorkflowOptions>? configure = null,
        IServiceProvider? services = null,
        params CodeWorkflowRegistration[] codeWorkflows)
    {
        var settings = new AgentPrismWorkflowOptions();
        configure?.Invoke(settings);

        var catalog = new WorkflowCatalog(
            codeWorkflows,
            DefinitionStore,
            Compiler,
            TenantContext,
            services ?? EmptyServices.Instance);

        return new WorkflowRunner(
            catalog,
            RunStore,
            CheckpointStore,
            TenantContext,
            Options.Create(settings),
            Options.Create(new AgentPrismOptions()),
            NullLogger<WorkflowRunner>.Instance);
    }

    /// <summary>Bir tanimi kiraciya kaydeder.</summary>
    public ValueTask<WorkflowDefinition> SaveAsync(WorkflowDefinition definition)
        => DefinitionStore.SaveAsync(TenantContext.TenantId, definition);

    internal sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId { get; set; } = "test";
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static EmptyServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }

    private sealed class CatalogServices(Dictionary<string, AIAgent> agents) : IServiceProvider, IAgentCatalog
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IAgentCatalog) ? this : null;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                .. agents.Keys.Select(static name => new AgentDescriptor
                {
                    Name = name,
                    Description = $"{name} rolunu ustlenir.",
                    Origin = AgentDefinitionOrigin.Database,
                    SourceName = "database",
                }),
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
            => new(agents.GetValueOrDefault(agentName));

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, CancellationToken cancellationToken = default)
            => new(agents.GetValueOrDefault(agentName));
    }
}

/// <summary>Kendisine gelen son mesaji isaretleyip geri donduren agent.</summary>
internal sealed class EchoAgent(string name) : AIAgent
{
    public override string Name => name;

    public override string Description => $"{name} rolunu ustlenir.";

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var last = messages.LastOrDefault()?.Text ?? string.Empty;

        return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, $"[{name}]{last}")));
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
        => new(new EchoSession());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => new(new EchoSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => new(EmptyState);

    private static JsonElement EmptyState { get; } = JsonDocument.Parse("{}").RootElement.Clone();

    private sealed class EchoSession : AgentSession;
}
