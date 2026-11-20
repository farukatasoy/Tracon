namespace Tracon;

/// <summary>
/// Chooses what <see cref="IAgentCatalog.ResolveAsync"/> returns for an agent name: which
/// definition version and which instruction culture.
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
}
