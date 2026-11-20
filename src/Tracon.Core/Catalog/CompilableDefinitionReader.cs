namespace Tracon;

/// <summary>
/// Reads the definition that a path outside the catalog compiles again for an agent that already ran: a
/// replay, an automatic continuation and the reconciler that decides whether to queue one.
/// </summary>
/// <remarks>
/// <para>
/// These paths used to read <see cref="IAgentDefinitionStore"/> by name. A custom source can outrank the store for
/// a name (<see cref="IAgentSource.Priority"/>), and then the catalog runs the source's agent while the store still
/// holds an older copy under the same name: the replay or the continuation compiled the copy and ran an agent the
/// original run never ran. The definition now comes from the same walk the parameter schema uses
/// (<see cref="AgentDefinitionLookup"/>), so the first source that holds the name decides.
/// </para>
/// <para>
/// Only a definition Tracon compiles itself is returned: a stored one, and one a
/// <see cref="IAgentDefinitionSource"/> returns. An agent registered in code is built by the host's own factory, and
/// compiling its definition again would skip that code; an agent an opaque <see cref="IAgentSource"/> builds has no
/// definition. For both, <see langword="null"/> sends the caller to the catalog, as for a deleted agent.
/// </para>
/// </remarks>
internal static class CompilableDefinitionReader
{
    /// <summary>Reads the definition Tracon compiles for an agent name.</summary>
    /// <param name="store">The definition store, read when the host has no parameter factory (and so no lookup).</param>
    /// <param name="factory">The factory that holds the lookup; <see langword="null"/> reads the store alone.</param>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="version">A pinned version; <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The definition (<see langword="null"/> when Tracon compiles none for this name) and where it comes from.
    /// </returns>
    /// <exception cref="TraconException">A source raised a Tracon error while the name was looked up.</exception>
    public static async ValueTask<FoundDefinition> ReadAsync(
        IAgentDefinitionStore store,
        ParameterizedAgentFactory? factory,
        string agentName,
        int? version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (factory is null)
        {
            var stored = version is { } pinned
                ? await store.GetVersionAsync(agentName, pinned, cancellationToken).ConfigureAwait(false)
                : await store.GetAsync(agentName, cancellationToken).ConfigureAwait(false);

            return new FoundDefinition(stored, AgentDefinitionOrigin.Database, Source: null);
        }

        var found = await factory.FindAsync(agentName, version, cancellationToken).ConfigureAwait(false);

        // A source that keeps no history has one definition, and the version a run recorded is the number that
        // definition carried (the catalog stamps every run with it), not a version somebody pinned. Asking such a source
        // for it finds nothing, so the current definition is read: it is the only one the source has.
        if (version is not null && found is { Held: true, Definition: null } && found.Source is not IVersionedAgentSource)
        {
            found = await factory.FindAsync(agentName, version: null, cancellationToken).ConfigureAwait(false);
        }

        return found.Origin == AgentDefinitionOrigin.Code ? found with { Definition = null } : found;
    }
}
