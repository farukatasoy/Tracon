namespace AgentPrism;

/// <summary>
/// The priority values AgentPrism's own built-in agent sources use.
/// </summary>
/// <remarks>
/// Nothing enforces these values against a custom <see cref="IAgentSource"/>: a
/// third-party source may use any <see cref="int"/>, including <see cref="Code"/> or
/// <see cref="Database"/> themselves. A source that shares a built-in value ties with
/// it, and DI registration order breaks the tie (see <see cref="IAgentSource.Priority"/>).
/// A custom source usually picks a value between <see cref="Code"/> and
/// <see cref="Database"/> to run after code but before the database, or a value above
/// <see cref="Database"/> to run last.
/// </remarks>
public static class AgentSourcePriority
{
    /// <summary>Priority of the code-defined agent source.</summary>
    public const int Code = 0;

    /// <summary>Priority of the database-backed agent source.</summary>
    public const int Database = 100;
}
