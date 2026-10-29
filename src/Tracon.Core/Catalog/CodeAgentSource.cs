using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// A source that exposes code-defined agents in the catalog.
/// </summary>
/// <remarks>
/// Its priority is 0, the highest. If a database definition has the same name,
/// code wins because it is validated at build time while the database definition is run-time data.
/// </remarks>
internal sealed partial class CodeAgentSource : IAgentSource
{
    private readonly Dictionary<string, CodeAgentRegistration> _registrations;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly CompiledAgentCache _cache;
    private readonly IModelProviderRegistry _models;
    private readonly IServiceProvider _services;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CodeAgentSource> _logger;

    // Names already warned about. The factory runs on every resolve, so the
    // warning is keyed by agent name and written once; TryAdd makes parallel
    // first resolves agree on a single writer.
    private readonly ConcurrentDictionary<string, byte> _bypassWarned = new(StringComparer.Ordinal);

    /// <summary>Initializes a new code source.</summary>
    /// <param name="registrations">The code agent registrations.</param>
    /// <param name="compiler">The compiler for declarative definitions.</param>
    /// <param name="cache">The compiled agent cache.</param>
    /// <param name="models">The registry that builds the chat client of a factory agent that declares its model.</param>
    /// <param name="services">The service provider supplied to factory registrations.</param>
    /// <param name="tenantContext">
    /// The tenant context. Code agent definitions are shared across tenants, but
    /// <see cref="AgentDefinitionCompiler"/> can bind tenant-dependent tools, such as
    /// semantic search, to the ambient tenant at compilation time. See <c>AddVectorSearchTool</c>.
    /// Adding the tenant to the cache key prevents that binding from leaking to another tenant.
    /// </param>
    /// <param name="logger">The logger that reports a factory agent built outside the model registry.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The same agent name is registered more than once.</exception>
    public CodeAgentSource(
        IEnumerable<CodeAgentRegistration> registrations,
        AgentDefinitionCompiler compiler,
        CompiledAgentCache cache,
        IModelProviderRegistry models,
        IServiceProvider services,
        ITenantContext tenantContext,
        ILogger<CodeAgentSource> logger)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);

        _compiler = compiler;
        _cache = cache;
        _models = models;
        _services = services;
        _tenantContext = tenantContext;
        _logger = logger;
        _registrations = new Dictionary<string, CodeAgentRegistration>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            if (!_registrations.TryAdd(registration.Name, registration))
            {
                throw new TraconException(
                    $"More than one code agent is registered with name '{registration.Name}'. Agent names must be unique.");
            }
        }
    }

    /// <inheritdoc />
    public string Name => "code";

    /// <inheritdoc />
    public int Priority => AgentSourcePriority.Code;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptors = new List<AgentDescriptor>(_registrations.Count);

        foreach (var registration in _registrations.Values)
        {
            descriptors.Add(new AgentDescriptor
            {
                Name = registration.Name,
                DisplayName = registration.DisplayName,
                Description = registration.Description,
                Origin = AgentDefinitionOrigin.Code,
                SourceName = Name,
                Model = registration.Model ?? registration.Definition?.Model,
                ToolNames = registration.Definition?.ToolNames ?? [],
                SkillNames = registration.Definition?.SkillNames ?? [],
                Skills = registration.Definition?.Skills,
                Workspace = registration.Definition?.Workspace,
                ExtensionNames = registration.Definition?.ExtensionNames ?? [],
                CallableAgentNames = registration.Definition is { } coded ? [.. coded.CallableAgents.Select(static agent => agent.Name)] : [],
                UsesHarness = registration.Definition?.Harness is not null,
            });
        }

        return new ValueTask<IReadOnlyList<AgentDescriptor>>(descriptors);
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registrations.TryGetValue(agentName, out var registration))
        {
            return null;
        }

        if (registration.Definition is not { } definition)
        {
            // The tenant-aware path, as a definition agent gets: egress first, then the tenant's own key.
            var built = await registration
                .BuildFactoryAgentAsync(_services, _models.CreateChatClientAsync, cancellationToken)
                .ConfigureAwait(false);

            if (built.Agent is { } agent)
            {
                DiagnoseModelRings(registration, agent, built.DeclaredChatClient, cancellationToken);
            }

            return built.Agent;
        }

        return await _compiler
            .CompileCachedAsync(definition, _cache, _tenantContext.TenantId, culture, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Warns, once per agent name, when the model calls of a factory agent do not match what it declared.
    /// </summary>
    /// <param name="registration">The registration of the agent.</param>
    /// <param name="agent">The agent the factory returned.</param>
    /// <param name="declaredChatClient">The client Tracon built for the declared model, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The caller's token; a genuine cancellation is never swallowed.</param>
    /// <remarks>
    /// <para>
    /// The registry wraps every client it builds in <see cref="ProviderFailureNormalizingChatClient"/>
    /// as the outermost ring. <c>GetService</c> walks the chat client chain, so finding that type
    /// proves the client came from the registry, even when the consumer wrapped it further. Its
    /// <em>instance</em> tells which client: the agent of a factory that declared its model must hold the
    /// ring of <paramref name="declaredChatClient"/>, or its calls skip the key and the egress policy the
    /// declaration promised and are priced at a model that did not answer.
    /// </para>
    /// <para>
    /// Three cases warn. An agent with no declared model whose client is not from the registry skips the
    /// model rings. An agent with no declared model whose client is from the registry carries no model,
    /// cost, tenant key or egress policy. An agent with a declared model that does not use the client
    /// Tracon gave it is not what it declared.
    /// </para>
    /// <para>
    /// An agent that exposes no <see cref="IChatClient"/> is unknown, not bypassed: it may call
    /// another runtime, and a warning there would be noise. The check is advice. A factory agent
    /// whose <c>GetService</c> throws still resolves, and the warning is skipped.
    /// </para>
    /// </remarks>
    private void DiagnoseModelRings(
        CodeAgentRegistration registration,
        AIAgent agent,
        IChatClient? declaredChatClient,
        CancellationToken cancellationToken)
    {
        var agentName = registration.Name;

        if (_bypassWarned.ContainsKey(agentName) || !_logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        ProviderFailureNormalizingChatClient? ring;

        try
        {
            if (agent.GetService<IChatClient>() is not { } chatClient)
            {
                return;
            }

            ring = chatClient.GetService<ProviderFailureNormalizingChatClient>();
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // The agent still resolves, so the name is not added to _bypassWarned: the factory runs
            // on every resolve and the lookup is tried again. Debug keeps a trace without a line per resolve at Warning.
            ModelRingCheckSkipped(_logger, exception, agentName);

            return;
        }

        if (declaredChatClient is not null && registration.Model is { } model)
        {
            var declaredRing = declaredChatClient.GetService<ProviderFailureNormalizingChatClient>();

            if (ring is not null && ReferenceEquals(ring, declaredRing))
            {
                return;
            }

            if (_bypassWarned.TryAdd(agentName, 0))
            {
                DeclaredModelNotUsed(_logger, agentName, model.Provider, model.Model);
            }

            return;
        }

        if (ring is null)
        {
            if (_bypassWarned.TryAdd(agentName, 0))
            {
                ModelRingsBypassed(_logger, agentName);
            }

            return;
        }

        if (_bypassWarned.TryAdd(agentName, 0))
        {
            ModelNotDeclared(_logger, agentName);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Code agent '{AgentName}' was built with a chat client that did not come from " +
                  "IModelProviderRegistry. Its model calls skip the model rings: run budget, circuit breaker, " +
                  "fallback, response cache, content guards, and provider failure normalization. Declare the model " +
                  "with AddAgent(name, model, factory) and build the agent around AgentFactoryContext.ChatClient, " +
                  "which also applies the tenant provider key and egress policy and names the model in the run record. " +
                  "This message is written once per agent name.")]
    private static partial void ModelRingsBypassed(ILogger logger, string agentName);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "Code agent '{AgentName}' was not checked for the model rings: looking up its chat client threw. " +
                  "The check is advice, so the agent still resolves; it is tried again on the next resolve.")]
    private static partial void ModelRingCheckSkipped(ILogger logger, Exception exception, string agentName);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Code agent '{AgentName}' calls a model through IModelProviderRegistry but declares none. " +
                  "Its calls use the key you set up at start, not the key of the tenant of the run, and skip the " +
                  "tenant's egress policy; its run records carry no model and no cost, and a cost quota does not stop it. " +
                  "Declare the model with AddAgent(name, model, factory) and build the agent around AgentFactoryContext.ChatClient. " +
                  "This message is written once per agent name.")]
    private static partial void ModelNotDeclared(ILogger logger, string agentName);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Code agent '{AgentName}' declares the model '{Provider}/{Model}' but its chat client is not " +
                  "AgentFactoryContext.ChatClient. Its model calls do not use the tenant provider key or the egress policy " +
                  "that the declaration promises, and its run records price them at the declared model, which may not be the " +
                  "model that answered. Build the agent around AgentFactoryContext.ChatClient. " +
                  "This message is written once per agent name.")]
    private static partial void DeclaredModelNotUsed(ILogger logger, string agentName, string provider, string model);
}
