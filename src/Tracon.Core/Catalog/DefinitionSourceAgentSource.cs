using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Joins an <see cref="IAgentDefinitionSource"/> to the catalog as an <see cref="IAgentSource"/>: the
/// source hands back a definition and Tracon compiles it.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a definition source behave like the database source. Compilation goes through
/// <see cref="AgentDefinitionCompiler.CompileCachedAsync"/>, so the tenant key, the BYOK cache bypass
/// and the dependency fingerprints are the compiler's and not re-derived here. The descriptor comes
/// from the definition that was compiled (<see cref="AgentDescriptorProjection"/>), so an agent and
/// its descriptor can never describe two versions.
/// </para>
/// <para>
/// A definition the source returns under a name other than the one asked for is a contract
/// violation, and the adapter refuses it: the catalog would otherwise serve one agent under
/// another's name, and a run record would be written against the wrong agent.
/// </para>
/// <para>
/// A source that keeps a version history is wrapped by <see cref="Versioned"/>, the only shape that
/// is also an <see cref="IVersionedAgentSource"/>; the catalog decides whether a pinned run is
/// possible by that interface alone.
/// </para>
/// </remarks>
internal class DefinitionSourceAgentSource : IAgentSource, IDescribingAgentSource, IDefinitionBackedSource
{
    private readonly IAgentDefinitionSource _source;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly CompiledAgentCache _cache;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates the adapter.</summary>
    /// <param name="source">The definition source.</param>
    /// <param name="compiler">The compiler that compiles its definitions.</param>
    /// <param name="cache">The compiled agent cache.</param>
    /// <param name="tenantContext">The tenant context; its tenant enters the cache key.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected DefinitionSourceAgentSource(
        IAgentDefinitionSource source,
        AgentDefinitionCompiler compiler,
        CompiledAgentCache cache,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _source = source;
        _compiler = compiler;
        _cache = cache;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public string Name => _source.Name;

    /// <inheritdoc />
    public int Priority => _source.Priority;

    /// <inheritdoc />
    public AgentDefinitionOrigin Origin => AgentDefinitionOrigin.Custom;

    /// <summary>Wraps a definition source for the catalog.</summary>
    /// <param name="source">The definition source.</param>
    /// <param name="services">The services the compiler, the cache and the tenant context come from.</param>
    /// <returns>The catalog source; an <see cref="IVersionedAgentSource"/> when <paramref name="source"/> keeps a history.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IAgentSource Create(IAgentDefinitionSource source, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(services);

        var compiler = (AgentDefinitionCompiler)services.GetService(typeof(AgentDefinitionCompiler))!;
        var cache = (CompiledAgentCache)services.GetService(typeof(CompiledAgentCache))!;
        var tenantContext = (ITenantContext)services.GetService(typeof(ITenantContext))!;

        return source is IVersionedAgentDefinitionSource versioned
            ? new Versioned(versioned, compiler, cache, tenantContext)
            : new DefinitionSourceAgentSource(source, compiler, cache, tenantContext);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var definitions = await _source.ListAsync(cancellationToken).ConfigureAwait(false);
        var descriptors = new List<AgentDescriptor>(definitions.Count);

        foreach (var definition in definitions)
        {
            descriptors.Add(AgentDescriptorProjection.FromDefinition(definition, Origin, Name));
        }

        return descriptors;
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        return await GetDefinitionAsync(agentName, version: null, cancellationToken).ConfigureAwait(false) is { } definition
            ? await CompileAsync(definition, culture, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc />
    public async ValueTask<DescribedAgent?> ResolveDescribedAsync(
        string agentName,
        int? version,
        string? culture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        if (await GetDefinitionAsync(agentName, version, cancellationToken).ConfigureAwait(false) is not { } definition)
        {
            return null;
        }

        var agent = await CompileAsync(definition, culture, cancellationToken).ConfigureAwait(false);

        return new DescribedAgent(agent, AgentDescriptorProjection.FromDefinition(definition, Origin, Name));
    }

    /// <inheritdoc />
    public async ValueTask<HeldDefinition?> FindDefinitionAsync(string agentName, int? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        if (version is null)
        {
            return await GetDefinitionAsync(agentName, version: null, cancellationToken).ConfigureAwait(false) is { } current
                ? new HeldDefinition(current)
                : null;
        }

        if (await GetDefinitionAsync(agentName, version, cancellationToken).ConfigureAwait(false) is { } pinned)
        {
            return new HeldDefinition(pinned);
        }

        // A version this source does not hold still leaves the name here: the catalog says the
        // version is missing instead of serving another source's agent.
        return await GetDefinitionAsync(agentName, version: null, cancellationToken).ConfigureAwait(false) is null
            ? null
            : new HeldDefinition(null);
    }

    /// <summary>Reads one version of a definition; a source with no history holds only the current one.</summary>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="version">The version, which this source never has.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition, or <see langword="null"/> when this source does not hold the version.</returns>
    protected virtual ValueTask<AgentDefinition?> GetVersionAsync(string agentName, int version, CancellationToken cancellationToken)
        => new((AgentDefinition?)null);

    /// <summary>Reads the current or one pinned definition and refuses a definition of another name.</summary>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="version">The pinned version, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition, stamped as custom; <see langword="null"/> when the source does not hold it.</returns>
    /// <exception cref="TraconAgentSourceException">The source returned a definition under another name.</exception>
    private async ValueTask<AgentDefinition?> GetDefinitionAsync(string agentName, int? version, CancellationToken cancellationToken)
    {
        var definition = version is { } pinned
            ? await GetVersionAsync(agentName, pinned, cancellationToken).ConfigureAwait(false)
            : await _source.GetAsync(agentName, cancellationToken).ConfigureAwait(false);

        if (definition is null)
        {
            return null;
        }

        if (!string.Equals(definition.Name, agentName, StringComparison.Ordinal))
        {
            throw new TraconAgentSourceException(
                Name,
                TraconAgentSourceException.SourceContractErrorType,
                $"Agent source '{Name}' violated its contract during resolve: it returned a definition named " +
                $"'{definition.Name}' for the agent '{agentName}'.");
        }

        return definition with { Origin = Origin };
    }

    private ValueTask<AIAgent> CompileAsync(AgentDefinition definition, string? culture, CancellationToken cancellationToken)
        => _compiler.CompileCachedAsync(definition, _cache, _tenantContext.TenantId, culture, cancellationToken);

    /// <summary>The adapter of a definition source that keeps a version history.</summary>
    internal sealed class Versioned : DefinitionSourceAgentSource, IVersionedAgentSource
    {
        private readonly IVersionedAgentDefinitionSource _versioned;

        /// <summary>Creates the adapter.</summary>
        /// <param name="source">The versioned definition source.</param>
        /// <param name="compiler">The compiler that compiles its definitions.</param>
        /// <param name="cache">The compiled agent cache.</param>
        /// <param name="tenantContext">The tenant context; its tenant enters the cache key.</param>
        public Versioned(
            IVersionedAgentDefinitionSource source,
            AgentDefinitionCompiler compiler,
            CompiledAgentCache cache,
            ITenantContext tenantContext)
            : base(source, compiler, cache, tenantContext)
        {
            _versioned = source;
        }

        /// <inheritdoc />
        public async ValueTask<AIAgent?> ResolveVersionAsync(
            string agentName,
            int version,
            string? culture = null,
            CancellationToken cancellationToken = default)
            => (await ResolveDescribedAsync(agentName, version, culture, cancellationToken).ConfigureAwait(false))?.Agent;

        /// <inheritdoc />
        protected override ValueTask<AgentDefinition?> GetVersionAsync(string agentName, int version, CancellationToken cancellationToken)
            => _versioned.GetVersionAsync(agentName, version, cancellationToken);
    }
}
