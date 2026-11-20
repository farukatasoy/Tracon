using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Builds an agent for a run that carries parameter values, on every path that
/// does not go through <c>POST /api/agents/{name}/run</c>: a queued run, a
/// batch item, an approval resume, a workflow step — and, for the catalog's own bound
/// resolve, the compile half of the sequence.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the one place the "compile outside the catalog" sequence
/// lives.</strong> A parameterized run bypasses <see cref="CompiledAgentCache"/>
/// (the cache key has no notion of a value), which means it also bypasses the
/// decorator chain the catalog applies on
/// <see cref="IAgentCatalog.ResolveAsync(string, AgentResolveOptions?, CancellationToken)"/>.
/// The caller therefore has to find the
/// definition, compile with the values, and apply
/// <see cref="AgentDecoratorPipeline"/> by hand or the run goes out with no
/// recording, no telemetry and no tool-approval gate. Four more callers
/// would have been four more copies of that sequence.
/// </para>
/// <para>
/// A run with no values keeps the catalog path (and its cache): the definition's
/// defaults are bound inside <c>BuildAgent</c>, which depends only on the
/// definition and the culture, both part of the cache key. The factory then only
/// reports the final value set so the run can record it.
/// </para>
/// <para>
/// The definition is found by <see cref="AgentDefinitionLookup"/>, which walks the agent sources in
/// the catalog's own order, so the schema this factory binds is the schema of the agent the catalog
/// would run.
/// </para>
/// </remarks>
internal sealed class ParameterizedAgentFactory
{
    private readonly AgentDefinitionLookup _lookup;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly IAgentDecorator[] _decorators;
    private readonly IOptionsMonitor<TraconOptions> _options;

    /// <summary>Creates a new factory.</summary>
    /// <param name="lookup">Finds the definition behind an agent name.</param>
    /// <param name="compiler">The definition compiler.</param>
    /// <param name="decorators">The wrappers the catalog would apply.</param>
    /// <param name="options">The runtime settings, for <see cref="TraconOptions.MaxParameterValueLength"/>.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ParameterizedAgentFactory(
        AgentDefinitionLookup lookup,
        AgentDefinitionCompiler compiler,
        IEnumerable<IAgentDecorator> decorators,
        IOptionsMonitor<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(decorators);
        ArgumentNullException.ThrowIfNull(options);

        _lookup = lookup;
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
    /// <returns>
    /// Where the name lives and, when the agent has one, its definition (<see langword="null"/> for a
    /// factory agent, an opaque source's agent or an unknown name).
    /// </returns>
    public ValueTask<FoundDefinition> FindAsync(
        string agentName,
        int? version,
        CancellationToken cancellationToken)
        => _lookup.FindAsync(agentName, version, cancellationToken);

    /// <summary>Checks supplied values against a schema with the configured length limit.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="values">The supplied values.</param>
    /// <returns>The outcome.</returns>
    public AgentParameterValidationResult Validate(
        IReadOnlyList<AgentParameter> schema,
        IReadOnlyDictionary<string, string>? values)
        => AgentParameterValidator.ValidateValues(schema, values, _options.CurrentValue.MaxParameterValueLength);

    /// <summary>
    /// Decides which values a run that repeats or carries on an earlier run is
    /// bound with: the source run's recorded values, with the caller's overrides
    /// laid over them.
    /// </summary>
    /// <param name="schema">The schema of the definition the new run will use.</param>
    /// <param name="recorded">
    /// The source run's recorded values. <see langword="null"/> means nothing was
    /// recorded, which is NOT the same as an empty set.
    /// </param>
    /// <param name="overrides">Values the caller supplies over the recorded ones; <see langword="null"/> for none.</param>
    /// <returns>The final values, or why none can be decided.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Fail closed.</strong> A definition that declares parameters and a
    /// source that recorded none cannot be repeated: running on the defaults
    /// would change the instructions the source run was given. A
    /// definition that declares none is unaffected.
    /// </para>
    /// <para>
    /// A recorded name the schema no longer declares is dropped (removing a
    /// parameter is the operator's change to make, and a value no instruction
    /// can reach is harmless); a required name the recorded values lack is
    /// refused; an <em>override</em> the schema does not declare is refused, so
    /// a typo does not vanish.
    /// </para>
    /// </remarks>
    public RecordedValues ResolveRecorded(
        IReadOnlyList<AgentParameter> schema,
        IReadOnlyDictionary<string, string>? recorded,
        IReadOnlyDictionary<string, string>? overrides)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (schema.Count == 0)
        {
            // Nothing to bind; an override has nowhere to go.
            return overrides is { Count: > 0 }
                ? RecordedValues.Invalid(ParameterValueSet.Describe(Validate(schema, overrides)))
                : new RecordedValues(null, RecordedValuesProblem.None, null);
        }

        if (recorded is null)
        {
            return new RecordedValues(null, RecordedValuesProblem.NotRecorded, null);
        }

        var merged = new Dictionary<string, string>(ParameterValueSet.Intersect(schema, recorded), StringComparer.Ordinal);

        if (overrides is not null)
        {
            foreach (var (name, value) in overrides)
            {
                merged[name] = value;
            }
        }

        var check = Validate(schema, merged);

        return check.IsValid
            ? new RecordedValues(ParameterValueSet.Resolve(schema, merged), RecordedValuesProblem.None, null)
            : RecordedValues.Invalid(ParameterValueSet.Describe(check));
    }

    /// <summary>
    /// Compiles a definition with values and applies the decorator chain.
    /// </summary>
    /// <param name="found">The definition to compile and where it comes from.</param>
    /// <param name="culture">The instruction culture.</param>
    /// <param name="values">The values to bind.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decorated agent, ready to run.</returns>
    /// <exception cref="TraconCompilationException">The definition cannot be compiled.</exception>
    public async ValueTask<AIAgent> CompileAsync(
        FoundDefinition found,
        string? culture,
        IReadOnlyDictionary<string, string>? values,
        CancellationToken cancellationToken)
    {
        var agent = await CompileBoundAsync(found, culture, values, markValues: false, cancellationToken).ConfigureAwait(false);

        return AgentDecoratorPipeline.Apply(agent, Describe(found), _decorators);
    }

    /// <summary>
    /// Compiles a definition with values and stops before the decorator chain: the catalog applies
    /// its own, with its own failure handling.
    /// </summary>
    /// <param name="found">The definition to compile and where it comes from.</param>
    /// <param name="culture">The instruction culture.</param>
    /// <param name="values">The values to bind.</param>
    /// <param name="markValues">
    /// Whether the agent carries the final value set as a <c>BoundParameterValues</c> marker, for a caller
    /// that has no run option to put it in.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The compiled agent, not yet decorated.</returns>
    /// <exception cref="TraconCompilationException">The definition cannot be compiled.</exception>
    public async ValueTask<AIAgent> CompileBoundAsync(
        FoundDefinition found,
        string? culture,
        IReadOnlyDictionary<string, string>? values,
        bool markValues,
        CancellationToken cancellationToken)
    {
        var definition = found.Definition
            ?? throw new ArgumentException("The agent has no definition to compile.", nameof(found));

        var agent = await _compiler
            .CompileParameterizedAsync(definition, culture, values, cancellationToken)
            .ConfigureAwait(false);

        return markValues
            ? new BoundParameterValues(agent, ParameterValueSet.Resolve(definition.Parameters, values))
            : agent;
    }

    /// <summary>The catalog summary of the definition a bound agent was compiled from.</summary>
    /// <param name="found">The definition and where it comes from.</param>
    /// <returns>The summary the decorators read.</returns>
    public static AgentDescriptor Describe(FoundDefinition found)
        => AgentDescriptorProjection.FromDefinition(
            found.Definition ?? throw new ArgumentException("The agent has no definition to describe.", nameof(found)),
            found.Origin,
            found.Source?.Name ?? AgentDefinitionLookup.BuiltInSourceName(found.Origin));

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
    /// <exception cref="TraconParameterException">
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
        var found = await FindAsync(agentName, version, cancellationToken).ConfigureAwait(false);
        var definition = found.Definition;
        var schema = definition?.Parameters ?? [];

        // An agent with no schema rejects any supplied value as unknown, and a
        // run that supplied none passes without a store read beyond the lookup.
        var validation = Validate(schema, supplied);

        if (!validation.IsValid)
        {
            throw ParameterValueSet.Failure(
                $"The run's parameters are invalid for agent '{agentName}'. {ParameterValueSet.Describe(validation)}",
                validation);
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

        var agent = await CompileAsync(found, culture, supplied, cancellationToken).ConfigureAwait(false);

        return new ParameterBinding(agent, final);
    }
}

/// <summary>Why <see cref="ParameterizedAgentFactory.ResolveRecorded"/> could not decide the values.</summary>
internal enum RecordedValuesProblem
{
    /// <summary>The values are decided (or the definition takes none).</summary>
    None = 0,

    /// <summary>The definition declares parameters and the source run recorded none.</summary>
    NotRecorded = 1,

    /// <summary>The values do not satisfy the definition's schema.</summary>
    Invalid = 2,
}

/// <summary>The outcome of <see cref="ParameterizedAgentFactory.ResolveRecorded"/>.</summary>
/// <param name="Values">
/// The final values to bind; <see langword="null"/> when the definition declares
/// no parameters or no decision could be made.
/// </param>
/// <param name="Problem">Why no decision could be made, if it could not.</param>
/// <param name="Detail">For <see cref="RecordedValuesProblem.Invalid"/>: names only, never a value.</param>
internal readonly record struct RecordedValues(
    IReadOnlyDictionary<string, string>? Values,
    RecordedValuesProblem Problem,
    string? Detail)
{
    /// <summary>Builds the invalid outcome.</summary>
    /// <param name="detail">The violations, by name.</param>
    /// <returns>The outcome.</returns>
    public static RecordedValues Invalid(string detail) => new(null, RecordedValuesProblem.Invalid, detail);
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
