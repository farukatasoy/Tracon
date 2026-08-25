namespace AgentPrism;

/// <summary>Resolved inputs that determine an agent compilation and its cache key.</summary>
/// <remarks>
/// Internal, not public: 101.7 measured every definition-backed <see cref="IAgentSource"/>
/// this repo has — the two built-in sources and the published
/// <c>AgentPrism.Samples.CustomAgentSource</c> sample — and every one of them only ever
/// needs <see cref="AgentDefinitionCompiler.CompileCachedAsync"/>. This type exists to
/// carry <see cref="AgentDefinitionCompiler.ResolveDependenciesAsync"/>'s result to that
/// method internally. Making it public now, with zero real callers, would be exactly the
/// "maybe a third party needs it someday" surface YAGNI exists to prevent — it can go
/// public later, WITH its actual caller, if a source with its own cache is ever measured
/// needing it.
/// </remarks>
internal sealed record AgentCompilationDependencies
{
    /// <summary>Gets the resolved callable sub-agents.</summary>
    public required ResolvedCallableAgents CallableAgents { get; init; }

    /// <summary>Gets the combined skill, callable-agent, and shared-instructions fingerprint.</summary>
    public required string CacheFingerprint { get; init; }

    /// <summary>Gets whether this compilation contains a tenant-specific provider credential.</summary>
    public required bool BypassCache { get; init; }
}
