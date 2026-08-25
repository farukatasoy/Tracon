namespace AgentPrism;

/// <summary>Reserved priority values for AgentPrism's built-in agent sources.</summary>
public static class AgentSourcePriority
{
    /// <summary>Priority of the code-defined agent source.</summary>
    public const int Code = 0;

    /// <summary>Priority of the database-backed agent source.</summary>
    public const int Database = 100;
}
