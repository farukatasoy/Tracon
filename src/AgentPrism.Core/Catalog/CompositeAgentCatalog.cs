using System.Runtime.ExceptionServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

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
    private readonly AgentPrismMetrics? _metrics;

    /// <summary>Initializes a new composite catalog.</summary>
    /// <param name="sources">The agent sources.</param>
    /// <param name="decorators">The decorators applied to resolved agents.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The source-failure metrics recorder.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public CompositeAgentCatalog(
        IEnumerable<IAgentSource> sources,
        IEnumerable<IAgentDecorator> decorators,
        ILogger<CompositeAgentCatalog> logger,
        AgentPrismMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(decorators);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = [.. sources.OrderBy(static source => source.Priority)];
        _decorators = [.. decorators.OrderByDescending(static decorator => decorator.Order)];
        _logger = logger;
        _metrics = metrics;
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RecordSourceFailure(source, "list", exception, LogLevel.Error);
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
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        foreach (var source in _sources)
        {
            AIAgent? agent;

            try
            {
                agent = await source.ResolveAsync(agentName, culture, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw HandleSourceFailure(source, "resolve", exception);
            }

            if (agent is null)
            {
                continue;
            }

            AgentDescriptor descriptor;

            try
            {
                descriptor = await FindDescriptorAsync(source, agentName, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw HandleSourceFailure(source, "resolve", exception);
            }

            foreach (var decorator in _decorators)
            {
                try
                {
                    agent = decorator.Decorate(agent, descriptor);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw HandleSourceFailure(source, "decorate", exception);
                }
            }

            return agent;
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(
        string agentName,
        int? version,
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        if (version is null)
        {
            return await ResolveAsync(agentName, culture, cancellationToken).ConfigureAwait(false);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        foreach (var source in _sources)
        {
            // If this source does not define the agent, continue with the next source.
            // Priority order matches ListAsync and ResolveAsync: lower Priority runs first.
            AgentDescriptor? descriptor;

            try
            {
                descriptor = await FindDescriptorOrNullAsync(source, agentName, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw HandleSourceFailure(source, "resolve", exception);
            }

            if (descriptor is null)
            {
                continue;
            }

            descriptor = Freeze(descriptor);

            if (source is not IVersionedAgentSource versioned)
            {
                throw new AgentPrismException(
                    $"Agent '{agentName}' comes from source '{source.Name}' and does not keep version history " +
                    "(code source). Runs or experiments against a specific version are not supported for this agent.");
            }

            AIAgent agent;

            try
            {
                agent = await versioned.ResolveVersionAsync(agentName, version.Value, culture, cancellationToken).ConfigureAwait(false)
                    ?? throw new AgentPrismException($"Version {version.Value} of agent '{agentName}' was not found.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw HandleSourceFailure(source, "resolve", exception);
            }

            foreach (var decorator in _decorators)
            {
                try
                {
                    agent = decorator.Decorate(agent, descriptor);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw HandleSourceFailure(source, "decorate", exception);
                }
            }

            return agent;
        }

        return null;
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

        RecordContractViolation(source, "consistency", $"resolved '{agentName}' but did not list it.", LogLevel.Warning);

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

    /// <summary>
    /// Records a source failure and re-throws it — normalized, unless it was already a
    /// normalized or otherwise-AgentPrism error.
    /// </summary>
    /// <remarks>
    /// <see cref="AgentPrismException"/> (any subtype — <see cref="AgentPrismAgentSourceException"/>
    /// itself, or a compilation error such as <c>AgentPrismCompilationException</c> that
    /// <c>AgentDefinitionCompiler.CompileCachedAsync</c> raised while the source built the
    /// agent) is already a safe, normalized AgentPrism error: it carries no raw
    /// third-party text, and a caller further up may be matching its SPECIFIC type (the
    /// run endpoint's <c>400</c>/<c>502</c> split reads <see cref="AgentPrismException.ErrorType"/>).
    /// Re-wrapping it here would both lose that type and misreport an ordinary
    /// compilation problem as an "agent source failure" it is not. Only a genuinely raw,
    /// unexpected exception — the actual case 101.3 exists to contain — gets wrapped.
    /// </remarks>
    private AgentPrismAgentSourceException HandleSourceFailure(IAgentSource source, string operation, Exception exception)
    {
        RecordSourceFailure(source, operation, exception, LogLevel.Error);

        if (exception is AgentPrismException)
        {
            // Always throws; the exception this method appears to return is never
            // actually reached in that case. Callers still write `throw
            // HandleSourceFailure(...)` — a return type of Exception, not void, is what
            // lets the compiler see the "always throws" invariant `agent`/`descriptor`
            // definite-assignment checks further down each caller need.
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return new AgentPrismAgentSourceException(
            source.Name,
            AgentPrismAgentSourceException.SourceFailedErrorType,
            $"Agent source '{source.Name}' failed during {operation} ({AgentPrismAgentSourceException.SourceFailedErrorType}).",
            exception);
    }

    private void RecordContractViolation(IAgentSource source, string operation, string detail, LogLevel level = LogLevel.Error)
    {
        var exception = new AgentPrismAgentSourceException(
            source.Name,
            AgentPrismAgentSourceException.SourceContractErrorType,
            $"Agent source '{source.Name}' violated its contract during {operation}: {detail}");
        RecordSourceFailure(source, operation, exception, level);
    }

    private void RecordSourceFailure(IAgentSource source, string operation, Exception exception, LogLevel level)
    {
        _metrics?.RecordAgentSourceFailure(source.Name, operation);
        _logger.Log(level, exception, "Agent source '{SourceName}' failed during {Operation}.", source.Name, operation);
    }

    private List<AgentDescriptor>? FreezeAndValidate(IAgentSource source, IReadOnlyList<AgentDescriptor> descriptors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var frozen = new List<AgentDescriptor>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Name))
            {
                RecordContractViolation(source, "list", "returned a descriptor with an empty name.");
                return null;
            }

            if (!names.Add(descriptor.Name))
            {
                RecordContractViolation(source, "list", $"returned '{descriptor.Name}' more than once.");
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
            CallableAgentNames = [.. descriptor.CallableAgentNames],
        };
}
