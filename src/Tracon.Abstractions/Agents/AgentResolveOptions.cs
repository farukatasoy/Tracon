namespace Tracon;

/// <summary>
/// Chooses what <see cref="IAgentCatalog.ResolveAsync"/> returns for an agent name: which
/// definition version, which instruction culture and which parameter values.
/// </summary>
/// <remarks>
/// A resolve input is a property here, not a new overload on the catalog, so the catalog
/// interface keeps one <c>ResolveAsync</c> member as inputs grow. Every property is optional;
/// an unset property keeps the default behavior.
/// </remarks>
public sealed record AgentResolveOptions
{
    /// <summary>
    /// Gets the definition version to resolve. <see langword="null"/> resolves the current version.
    /// </summary>
    /// <remarks>
    /// Code agents (<see cref="AgentDefinitionOrigin.Code"/>) have no version history; resolving
    /// a code agent with a version throws <see cref="TraconException"/>.
    /// </remarks>
    public int? Version { get; init; }

    /// <summary>
    /// Gets the requested culture, resolved against the definition's culture-keyed instructions
    /// (see <c>AgentDefinition.InstructionsByCulture</c>). <see langword="null"/> uses the
    /// definition's default instructions.
    /// </summary>
    public string? Culture { get; init; }

    /// <summary>
    /// Gets the parameter values to bind into the definition's instructions at resolve time.
    /// <see langword="null"/> binds no values: the definition's defaults apply and no values are recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A non-<see langword="null"/> dictionary, even an empty one, is checked against the definition's
    /// parameter schema with the same rules as <c>POST /api/agents/{name}/run</c>. The agent is then
    /// compiled for exactly these values and is not shared through the compiled-agent cache. The
    /// decorators the catalog applies (run recording, telemetry, tool approval) still apply. The run
    /// records the final value set, defaults included, so replay and continuation repeat it. An empty
    /// dictionary therefore means "bind the defaults and record them".
    /// </para>
    /// <para>
    /// An agent whose source declares no parameter schema rejects every supplied name as unknown.
    /// A required value that is missing, a name the schema does not declare and a value over
    /// <c>MaxParameterValueLength</c> throw <see cref="TraconParameterException"/> from
    /// <see cref="IAgentCatalog.ResolveAsync"/>; no provider call happens and no run row is written.
    /// </para>
    /// <para>
    /// The recorder finds the final value set on the agent through <c>GetService</c>, so a decorator you register
    /// must forward <c>GetService</c> to the agent it wraps (a type that derives from <c>DelegatingAIAgent</c> does).
    /// A wrapper that hides it leaves the run without recorded values, and a replay cannot repeat it.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}
