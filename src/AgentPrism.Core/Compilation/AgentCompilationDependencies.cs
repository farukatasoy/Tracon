namespace AgentPrism;

/// <summary>Resolved inputs that determine an agent compilation and its cache key.</summary>
public sealed record AgentCompilationDependencies
{
    /// <summary>Gets the resolved callable sub-agents.</summary>
    public required ResolvedCallableAgents CallableAgents { get; init; }

    /// <summary>Gets the combined skill, callable-agent, and shared-instructions fingerprint.</summary>
    public required string CacheFingerprint { get; init; }

    /// <summary>Gets whether this compilation contains a tenant-specific provider credential.</summary>
    public required bool BypassCache { get; init; }
}
