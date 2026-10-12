using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Builds an agent for a run that carries parameter values, on every path that
/// does not go through <c>POST /api/agents/{name}/run</c>: a queued run, a
/// batch item, an approval resume, a workflow step.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the one place the "compile outside the catalog" sequence
/// lives.</strong> A parameterized run bypasses <see cref="CompiledAgentCache"/>
/// (the cache key has no notion of a value), which means it also bypasses
/// <see cref="IAgentCatalog.ResolveAsync(string, string, CancellationToken)"/> and the
/// decorator chain the catalog applies. The caller therefore has to find the
/// definition (code first, K-890), compile with the values, and apply
/// <see cref="AgentDecoratorPipeline"/> by hand or the run goes out with no
/// recording, no telemetry and no tool-approval gate (K-581). Four more callers
/// would have been four more copies of that sequence.
/// </para>
/// <para>
/// A run with no values keeps the catalog path (and its cache): the definition's
/// defaults are bound inside <c>BuildAgent</c>, which depends only on the
/// definition and the culture, both part of the cache key. The factory then only
/// reports the final value set so the run can record it.
/// </para>
/// </remarks>
internal sealed class ParameterizedAgentFactory
{
    private readonly CodeAgentRegistration[] _codeRegistrations;
    private readonly IAgentDefinitionStore _definitions;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly IAgentDecorator[] _decorators;
    private readonly IOptionsMonitor<TraconOptions> _options;

    /// <summary>Creates a new factory.</summary>
    /// <param name="codeRegistrations">The agents registered in code.</param>
    /// <param name="definitions">The definition store.</param>
    /// <param name="compiler">The definition compiler.</param>
    /// <param name="decorators">The wrappers the catalog would apply.</param>
    /// <param name="options">The runtime settings, for <see cref="TraconOptions.MaxParameterValueLength"/>.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ParameterizedAgentFactory(
        IEnumerable<CodeAgentRegistration> codeRegistrations,
        IAgentDefinitionStore definitions,
        AgentDefinitionCompiler compiler,
        IEnumerable<IAgentDecorator> decorators,
        IOptionsMonitor<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(codeRegistrations);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(decorators);
        ArgumentNullException.ThrowIfNull(options);

        _codeRegistrations = [.. codeRegistrations];
        _definitions = definitions;
        _compiler = compiler;

        // The same order CompositeAgentCatalog uses: the larger Order is applied
        // first, so the recording wrapper (Order = 0) ends up outermost.
        _decorators = [.. decorators.OrderByDescending(static decorator => decorator.Order)];
        _options = options;
    }

    /// <summary>Finds the definition the catalog would run for a name.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="version">A pinned version, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition (<see langword="null"/> for a factory agent or an unknown name) and its origin.</returns>
    public ValueTask<(AgentDefinition? Definition, AgentDefinitionOrigin Origin)> FindAsync(
        string agentName,
        int? version,
        CancellationToken cancellationToken)
        => AgentDefinitionLookup.FindAsync(_codeRegistrations, _definitions, agentName, version, cancellationToken);

    /// <summary>Checks supplied values against a schema with the configured length limit.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="values">The supplied values.</param>
    /// <returns>The outcome.</returns>
    public AgentParameterValidationResult Validate(
        IReadOnlyList<AgentParameter> schema,
        IReadOnlyDictionary<string, string>? values)
        => AgentParameterValidator.ValidateValues(schema, values, _options.CurrentValue.MaxParameterValueLength);

    /// <summary>
    /// Compiles a definition with values and applies the decorator chain.
    /// </summary>
    /// <param name="definition">The source definition.</param>
    /// <param name="origin">Where <paramref name="definition"/> comes from.</param>
    /// <param name="culture">The instruction culture.</param>
    /// <param name="values">The values to bind.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decorated agent, ready to run.</returns>
    /// <exception cref="TraconCompilationException">The definition cannot be compiled.</exception>
    public async ValueTask<AIAgent> CompileAsync(
        AgentDefinition definition,
        AgentDefinitionOrigin origin,
        string? culture,
        IReadOnlyDictionary<string, string>? values,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var agent = await _compiler
            .CompileParameterizedAsync(definition, culture, values, cancellationToken)
            .ConfigureAwait(false);

        return AgentDecoratorPipeline.Apply(
            agent,
            AgentDescriptorProjection.FromDefinition(definition, origin, AgentDefinitionLookup.SourceName(origin)),
            _decorators);
    }

    /// <summary>
    /// Prepares an agent run for a name and the values a caller supplied.
    /// </summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="version">A pinned version, or <see langword="null"/> for the current one.</param>
    /// <param name="culture">The instruction culture.</param>
    /// <param name="supplied">The values the caller supplied; <see langword="null"/> or empty for none.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The agent to run when values were supplied (<see langword="null"/> means
    /// "resolve it from the catalog as before"), and the final value set to
    /// record (<see langword="null"/> when the agent declares no schema).
    /// </returns>
    /// <exception cref="TraconException">
    /// The values do not satisfy the schema: a required one is missing, a name
    /// is not declared, or a value is over the length limit. The message names
    /// parameters only, never values.
    /// </exception>
    public async ValueTask<ParameterBinding> BindAsync(
        string agentName,
        int? version,
        string? culture,
        IReadOnlyDictionary<string, string>? supplied,
        CancellationToken cancellationToken)
    {
        var (definition, origin) = await FindAsync(agentName, version, cancellationToken).ConfigureAwait(false);
        var schema = definition?.Parameters ?? [];

        // An agent with no schema rejects any supplied value as unknown, and a
        // run that supplied none passes without a store read beyond the lookup.
        var validation = Validate(schema, supplied);

        if (!validation.IsValid)
        {
            throw new TraconException(
                $"The run's parameters are invalid for agent '{agentName}'. {ParameterValueSet.Describe(validation)}");
        }

        if (definition is null || schema.Count == 0)
        {
            return new ParameterBinding(null, null);
        }

        var final = ParameterValueSet.Resolve(schema, supplied);

        if (supplied is not { Count: > 0 })
        {
            // Defaults only: the catalog path binds them (and caches the result).
            return new ParameterBinding(null, final);
        }

        var agent = await CompileAsync(definition, origin, culture, supplied, cancellationToken).ConfigureAwait(false);

        return new ParameterBinding(agent, final);
    }
}

/// <summary>The outcome of <see cref="ParameterizedAgentFactory.BindAsync"/>.</summary>
/// <param name="Agent">
/// The agent compiled with the supplied values; <see langword="null"/> when the
/// caller should resolve the agent from the catalog.
/// </param>
/// <param name="Values">
/// The final value set the run should record; <see langword="null"/> when the
/// agent declares no parameters.
/// </param>
internal readonly record struct ParameterBinding(AIAgent? Agent, IReadOnlyDictionary<string, string>? Values);
