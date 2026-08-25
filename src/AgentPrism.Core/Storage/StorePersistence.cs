namespace AgentPrism;

/// <summary>
/// Answers whether an installation keeps its data across a restart.
/// </summary>
/// <remarks>
/// <para>
/// This judgement has two readers: the startup warning
/// (<see cref="NonPersistentStorageWarningService"/>) and the <c>/api/meta</c>
/// endpoint, which reports it to the console as <c>storage.persistent</c>.
/// They must never disagree, so the type checks live here once instead of
/// being repeated at each call site. A fourth non-persistent store added later
/// is handled in one place.
/// </para>
/// <para>
/// The audit trail decorators (<c>Auditing*Store</c>) are transparent to this
/// question: a decorated store is judged by the real implementation it wraps,
/// not by the decorator.
/// </para>
/// </remarks>
internal static class StorePersistence
{
    /// <summary>The label the run store carries in a warning message.</summary>
    private const string Runs = "runs";

    /// <summary>The label the session store carries in a warning message.</summary>
    private const string Sessions = "sessions";

    /// <summary>The label the agent definition store carries in a warning message.</summary>
    private const string Definitions = "agent definitions";

    /// <summary>Strips an audit trail decorator, if there is one.</summary>
    /// <param name="store">The registered store.</param>
    /// <returns>The real implementation.</returns>
    internal static object Unwrap(object store)
        => store is IAuditDecorated decorated ? decorated.AuditedInner : store;

    /// <summary>
    /// Tells whether all three stores survive a restart.
    /// </summary>
    /// <param name="definitions">The registered agent definition store.</param>
    /// <param name="runs">The registered run store.</param>
    /// <param name="sessions">The registered session store.</param>
    /// <returns><see langword="true"/> when no store is in memory.</returns>
    internal static bool IsPersistent(
        IAgentDefinitionStore definitions,
        IRunStore runs,
        ISessionStore sessions)
        => NonPersistentStores(definitions, runs, sessions).Count == 0;

    /// <summary>
    /// Names the stores that lose their data when the process ends.
    /// </summary>
    /// <param name="definitions">The registered agent definition store.</param>
    /// <param name="runs">The registered run store.</param>
    /// <param name="sessions">The registered session store.</param>
    /// <returns>The labels, in a stable order. Empty when everything is persistent.</returns>
    internal static IReadOnlyList<string> NonPersistentStores(
        IAgentDefinitionStore definitions,
        IRunStore runs,
        ISessionStore sessions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(sessions);

        var names = new List<string>(3);

        if (Unwrap(definitions) is InMemoryAgentDefinitionStore)
        {
            names.Add(Definitions);
        }

        if (Unwrap(runs) is InMemoryRunStore)
        {
            names.Add(Runs);
        }

        if (Unwrap(sessions) is InMemorySessionStore)
        {
            names.Add(Sessions);
        }

        return names;
    }
}
