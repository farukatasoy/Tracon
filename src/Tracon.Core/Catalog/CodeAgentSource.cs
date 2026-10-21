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
        IServiceProvider services,
        ITenantContext tenantContext,
        ILogger<CodeAgentSource> logger)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);

        _compiler = compiler;
        _cache = cache;
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
                Model = registration.Definition?.Model,
                ToolNames = registration.Definition?.ToolNames ?? [],
                SkillNames = registration.Definition?.SkillNames ?? [],
                Skills = registration.Definition?.Skills,
                ExtensionNames = registration.Definition?.ExtensionNames ?? [],
                CallableAgentNames = registration.Definition?.CallableAgentNames ?? [],
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
            var agent = registration.Factory!(_services);
            WarnWhenModelRingsAreBypassed(agentName, agent, cancellationToken);
            return agent;
        }

        return await _compiler
            .CompileCachedAsync(definition, _cache, _tenantContext.TenantId, culture, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Warns, once per agent name, when a factory agent talks to a model through a chat
    /// client that did not come from <see cref="IModelProviderRegistry"/>.
    /// </summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="agent">The agent the factory returned.</param>
    /// <param name="cancellationToken">The caller's token; a genuine cancellation is never swallowed.</param>
    /// <remarks>
    /// <para>
    /// The registry wraps every client it builds in <see cref="ProviderFailureNormalizingChatClient"/>
    /// as the outermost ring. <c>GetService</c> walks the chat client chain, so finding that type
    /// proves the client came from the registry, even when the consumer wrapped it further.
    /// </para>
    /// <para>
    /// An agent that exposes no <see cref="IChatClient"/> is unknown, not bypassed: it may call
    /// another runtime, and a warning there would be noise. The check is advice. A factory agent
    /// whose <c>GetService</c> throws still resolves, and the warning is skipped.
    /// </para>
    /// </remarks>
    private void WarnWhenModelRingsAreBypassed(string agentName, AIAgent agent, CancellationToken cancellationToken)
    {
        if (agent is null || _bypassWarned.ContainsKey(agentName) || !_logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        try
        {
            if (agent.GetService<IChatClient>() is not { } chatClient
                || chatClient.GetService<ProviderFailureNormalizingChatClient>() is not null)
            {
                return;
            }
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            return;
        }

        if (_bypassWarned.TryAdd(agentName, 0))
        {
            ModelRingsBypassed(_logger, agentName);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Code agent '{AgentName}' was built with a chat client that did not come from " +
                  "IModelProviderRegistry. Its model calls skip the model rings: run budget, circuit breaker, " +
                  "fallback, response cache, content guards, and provider failure normalization. Build the client " +
                  "with IModelProviderRegistry.CreateChatClient, or serve the agent from an IAgentSource that " +
                  "calls CreateChatClientAsync, which also applies the tenant provider key and egress policy. " +
                  "This message is written once per agent name.")]
    private static partial void ModelRingsBypassed(ILogger logger, string agentName);
}
