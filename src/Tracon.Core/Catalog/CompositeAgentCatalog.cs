using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Combines all <see cref="IAgentSource"/> sources in one catalog and wraps resolved
/// agents with registered <see cref="IAgentDecorator"/> decorators.
/// </summary>
/// <remarks>
/// Sources are tried in <see cref="IAgentSource.Priority"/> order. When names collide,
/// the higher-priority source with the lower number wins. The losing source is omitted
/// from the list and the catalog logs a warning.
/// </remarks>
internal sealed class CompositeAgentCatalog : IAgentCatalog
{
    private readonly IAgentSource[] _sources;
    private readonly IAgentDecorator[] _decorators;
    private readonly ILogger<CompositeAgentCatalog> _logger;
    private readonly AgentSourceFailures _failures;
    private readonly AgentDefinitionLookup _lookup;
    private readonly ParameterizedAgentFactory? _parameterFactory;

    /// <summary>Initializes a new composite catalog.</summary>
    /// <param name="sources">The agent sources.</param>
    /// <param name="decorators">The decorators applied to resolved agents.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The source-failure metrics recorder.</param>
    /// <param name="lookup">
    /// Finds the definition behind a name for a resolve that binds parameter values. When
    /// <see langword="null"/> the catalog walks its own sources.
    /// </param>
    /// <param name="parameterFactory">
    /// Compiles an agent for a set of parameter values. Without it the catalog cannot bind values.
    /// </param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public CompositeAgentCatalog(
        IEnumerable<IAgentSource> sources,
        IEnumerable<IAgentDecorator> decorators,
        ILogger<CompositeAgentCatalog> logger,
        TraconMetrics? metrics = null,
        AgentDefinitionLookup? lookup = null,
        ParameterizedAgentFactory? parameterFactory = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(decorators);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = [.. sources.OrderBy(static source => source.Priority)];
        _decorators = [.. decorators.OrderByDescending(static decorator => decorator.Order)];
        _logger = logger;
        _failures = new AgentSourceFailures(logger, metrics);
        _lookup = lookup ?? new AgentDefinitionLookup(_sources, _failures);
        _parameterFactory = parameterFactory;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var byName = new Dictionary<string, AgentDescriptor>(StringComparer.Ordinal);

        foreach (var source in _sources)
        {
            IReadOnlyList<AgentDescriptor> descriptors;

            try
            {
                descriptors = await source.ListAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                _failures.Record(source, "list", exception, LogLevel.Error);
                continue;
            }

            var frozenDescriptors = FreezeAndValidate(source, descriptors);

            if (frozenDescriptors is null)
            {
                continue;
            }

            foreach (var descriptor in frozenDescriptors)
            {
                if (byName.TryGetValue(descriptor.Name, out var winner))
                {
                    _logger.LogWarning(
                        "Agent '{AgentName}' is defined in both source '{Winner}' and source '{Loser}'. " +
                        "Source '{Winner}' will be used based on priority.",
                        descriptor.Name,
                        winner.SourceName,
                        source.Name,
                        winner.SourceName);

                    continue;
                }

                byName.Add(descriptor.Name, descriptor);
            }
        }

        var result = byName.Values.ToList();
        result.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return result;
    }

    /// <inheritdoc />
    public ValueTask<AgentDescriptor?> DescribeAsync(string agentName, CancellationToken cancellationToken = default)
        => FindDescriptorAsync(agentName, cancellationToken);

    /// <summary>
    /// Finds the descriptor of one agent, the one <see cref="ListAsync"/> would list for the name, without listing
    /// the sources that can describe a name on their own.
    /// </summary>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The descriptor of the source that wins the name; <see langword="null"/> when none holds it.</returns>
    /// <remarks>
    /// <para>
    /// The sources are walked in the order <see cref="ListAsync"/> uses, so the same source wins the name, and a source
    /// that fails is recorded under <c>list</c> and skipped, as there. A source that implements
    /// <see cref="INamedDescriptorSource"/> (the database source and the adapter of a definition source) is asked for the
    /// one name; any other is listed and searched, as before. The code source is one of those, and listing it reads
    /// memory.
    /// </para>
    /// <para>
    /// Reading the whole listing to describe the few agents another agent calls cost about 9 µs for each agent of the
    /// tenant on every resolve of the caller, 45 ms with 5 000 agents on PostgreSQL.
    /// </para>
    /// </remarks>
    internal async ValueTask<AgentDescriptor?> FindDescriptorAsync(string agentName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        foreach (var source in _sources)
        {
            AgentDescriptor? found;

            try
            {
                if (source is INamedDescriptorSource named)
                {
                    found = await named.DescribeAsync(agentName, cancellationToken).ConfigureAwait(false);
                    found = found is null ? null : Freeze(found);
                }
                else
                {
                    var listed = await source.ListAsync(cancellationToken).ConfigureAwait(false);

                    found = FreezeAndValidate(source, listed)?
                        .Find(descriptor => string.Equals(descriptor.Name, agentName, StringComparison.Ordinal));
                }
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                _failures.Record(source, "list", exception, LogLevel.Error);

                continue;
            }

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(
        string agentName,
        AgentResolveOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        if (options?.Parameters is { } values)
        {
            return await ResolveBoundAsync(agentName, options, values, cancellationToken).ConfigureAwait(false);
        }

        return options?.Version is { } version
            ? await ResolveVersionAsync(agentName, version, options.Culture, cancellationToken).ConfigureAwait(false)
            : await ResolveCurrentAsync(agentName, options?.Culture, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves an agent for one set of parameter values: finds the definition the catalog would run,
    /// checks the values against its schema, compiles for exactly those values, and applies the same
    /// decorators every other resolve applies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The compiled agent is never shared: <see cref="CompiledAgentCache"/> has no notion of a value in
    /// its key. The decorators are applied here with the catalog's own failure handling, so a failing
    /// source or decorator is recorded on the same counter and raised in the same shape as on the
    /// other paths. The agent carries the final value set (<c>BoundParameterValues</c>), which the run
    /// recording wrapper writes to the run record.
    /// </para>
    /// <para>
    /// An agent that declares no schema has nothing to bind. It resolves exactly as a call without values, and the
    /// values are then checked against the empty schema, so a name it does not declare is refused.
    /// </para>
    /// </remarks>
    private async ValueTask<AIAgent?> ResolveBoundAsync(
        string agentName,
        AgentResolveOptions options,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var found = await _lookup.FindAsync(agentName, options.Version, cancellationToken).ConfigureAwait(false);

        if (!found.Held)
        {
            return null;
        }

        var factory = _parameterFactory ?? throw new TraconException(
            "This catalog was built without a parameter factory, so it cannot bind parameter values.");

        if (found.Definition is not { Parameters.Count: > 0 } definition)
        {
            // Nothing to bind: the agent has no definition to read a schema from (an agent a source built itself, a
            // factory agent, a version the source does not hold) or its schema is empty. It resolves exactly as
            // without values, so a failing source, a missing version and a source that keeps no history answer in
            // their own words. The values are checked afterwards: a name no schema declares is unknown.
            var plain = await ResolveAsync(agentName, options with { Parameters = null }, cancellationToken).ConfigureAwait(false);

            if (factory.Validate([], values) is { IsValid: false } unknown)
            {
                throw ParameterValueSet.Failure(
                    $"The parameters are invalid for agent '{agentName}'. {ParameterValueSet.Describe(unknown)}",
                    unknown);
            }

            return plain;
        }

        var validation = factory.Validate(definition.Parameters, values);

        if (!validation.IsValid)
        {
            throw ParameterValueSet.Failure(
                $"The parameters are invalid for agent '{agentName}'. {ParameterValueSet.Describe(validation)}",
                validation);
        }

        var source = found.Source!;
        AIAgent agent;
        AgentDescriptor descriptor;

        try
        {
            agent = await factory
                .CompileBoundAsync(found, options.Culture, values, markValues: true, cancellationToken)
                .ConfigureAwait(false);
            descriptor = Freeze(ParameterizedAgentFactory.Describe(found));
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            throw _failures.Handle(source, "resolve", exception);
        }

        return Decorate(source, agent, descriptor, cancellationToken);
    }

    private AIAgent Decorate(IAgentSource source, AIAgent agent, AgentDescriptor descriptor, CancellationToken cancellationToken)
    {
        foreach (var decorator in _decorators)
        {
            try
            {
                agent = decorator.Decorate(agent, descriptor);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                throw _failures.Handle(source, "decorate", exception);
            }
        }

        return agent;
    }

    private async ValueTask<AIAgent?> ResolveCurrentAsync(string agentName, string? culture, CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
        {
            AIAgent? agent;
            AgentDescriptor descriptor;

            try
            {
                // A source that can describe what it compiled does so
                // from the same definition read; the agent and its descriptor
                // can no longer come from two different versions.
                if (source is IDescribingAgentSource describing)
                {
                    if (await describing.ResolveDescribedAsync(agentName, version: null, culture, cancellationToken)
                            .ConfigureAwait(false) is not { } described)
                    {
                        continue;
                    }

                    agent = described.Agent;
                    descriptor = Freeze(described.Descriptor);
                }
                else
                {
                    agent = await source.ResolveAsync(agentName, culture, cancellationToken).ConfigureAwait(false);

                    if (agent is null)
                    {
                        continue;
                    }

                    descriptor = await FindDescriptorAsync(source, agentName, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                throw _failures.Handle(source, "resolve", exception);
            }

            return Decorate(source, agent, descriptor, cancellationToken);
        }

        return null;
    }

    private async ValueTask<AIAgent?> ResolveVersionAsync(
        string agentName,
        int version,
        string? culture,
        CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
        {
            // If this source does not define the agent, continue with the next source.
            // Priority order matches ListAsync and ResolveAsync: lower Priority runs first.
            AgentDescriptor? descriptor;

            try
            {
                descriptor = await FindDescriptorOrNullAsync(source, agentName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                throw _failures.Handle(source, "resolve", exception);
            }

            if (descriptor is null)
            {
                continue;
            }

            descriptor = Freeze(descriptor);

            if (source is not IVersionedAgentSource versioned)
            {
                throw new TraconException(
                    $"Agent '{agentName}' comes from source '{source.Name}' and does not keep version history. " +
                    "Runs or experiments against a specific version are not supported for this agent.");
            }

            AIAgent agent;

            try
            {
                // The descriptor listed above is the CURRENT version's.
                // The decorators read the response format, model and version
                // off it, so a pinned run was validated against the current
                // schema and recorded with the current model. The pinned
                // version's own definition describes it; a third-party source
                // that cannot provide one keeps the listed descriptor with the
                // pinned version number.
                if (source is IDescribingAgentSource describing)
                {
                    var described = await describing
                        .ResolveDescribedAsync(agentName, version, culture, cancellationToken)
                        .ConfigureAwait(false)
                        ?? throw new TraconException($"Version {version} of agent '{agentName}' was not found.");

                    agent = described.Agent;
                    descriptor = Freeze(described.Descriptor);
                }
                else
                {
                    agent = await versioned.ResolveVersionAsync(agentName, version, culture, cancellationToken).ConfigureAwait(false)
                        ?? throw new TraconException($"Version {version} of agent '{agentName}' was not found.");

                    descriptor = descriptor with { Version = version };
                }
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                throw _failures.Handle(source, "resolve", exception);
            }

            return Decorate(source, agent, descriptor, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Finds whether the source that serves <paramref name="agentName"/> keeps
    /// version history, which a pinned resolution needs.
    /// </summary>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the first source in priority order that
    /// lists the name is an <see cref="IVersionedAgentSource"/> — the same
    /// source <see cref="ResolveAsync(string, AgentResolveOptions?, CancellationToken)"/>
    /// pins against; <see langword="false"/> when it is not, or when no source
    /// lists the name.
    /// </returns>
    internal async ValueTask<bool> ServesFromVersionedSourceAsync(string agentName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        foreach (var source in _sources)
        {
            AgentDescriptor? descriptor;

            try
            {
                descriptor = await FindDescriptorOrNullAsync(source, agentName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                throw _failures.Handle(source, "resolve", exception);
            }

            if (descriptor is not null)
            {
                return source is IVersionedAgentSource;
            }
        }

        return false;
    }

    private async ValueTask<AgentDescriptor> FindDescriptorAsync(
        IAgentSource source,
        string agentName,
        CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorOrNullAsync(source, agentName, cancellationToken).ConfigureAwait(false);

        if (descriptor is not null)
        {
            return Freeze(descriptor);
        }

        _failures.RecordContractViolation(source, "consistency", $"resolved '{agentName}' but did not list it.", LogLevel.Warning);

        return new AgentDescriptor
        {
            Name = agentName,
            Origin = AgentDefinitionOrigin.Custom,
            SourceName = source.Name,
        };
    }

    private static async ValueTask<AgentDescriptor?> FindDescriptorOrNullAsync(
        IAgentSource source,
        string agentName,
        CancellationToken cancellationToken)
    {
        var descriptors = await source.ListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var descriptor in descriptors)
        {
            if (string.Equals(descriptor.Name, agentName, StringComparison.Ordinal))
            {
                return descriptor;
            }
        }

        return null;
    }

    private List<AgentDescriptor>? FreezeAndValidate(IAgentSource source, IReadOnlyList<AgentDescriptor> descriptors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var frozen = new List<AgentDescriptor>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Name))
            {
                _failures.RecordContractViolation(source, "list", "returned a descriptor with an empty name.");
                return null;
            }

            if (!names.Add(descriptor.Name))
            {
                _failures.RecordContractViolation(source, "list", $"returned '{descriptor.Name}' more than once.");
                return null;
            }

            frozen.Add(Freeze(descriptor));
        }

        return frozen;
    }

    private static AgentDescriptor Freeze(AgentDescriptor descriptor)
        => descriptor with
        {
            Model = descriptor.Model is null
                ? null
                : descriptor.Model with
                {
                    ProviderSettings = new Dictionary<string, System.Text.Json.JsonElement>(descriptor.Model.ProviderSettings, StringComparer.OrdinalIgnoreCase),
                    Fallbacks = [.. descriptor.Model.Fallbacks],
                },
            ToolNames = [.. descriptor.ToolNames],
            SkillNames = [.. descriptor.SkillNames],
            ExtensionNames = [.. descriptor.ExtensionNames],
            CallableAgentNames = [.. descriptor.CallableAgentNames],
            Parameters = [.. descriptor.Parameters],
        };
}
