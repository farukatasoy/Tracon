namespace AgentPrism;

/// <summary>Response caching settings for a <see cref="ModelBinding"/>.</summary>
/// <remarks>
/// <para>
/// Disabled by default: with no cache configured, today's behavior is
/// preserved exactly — every call reaches the model, and no cache ring is
/// added to the pipeline.
/// </para>
/// <para>
/// The cache key is tenant-, provider- and tool-set-aware; it is not the raw
/// key <c>Microsoft.Extensions.AI.DistributedCachingChatClient</c> computes on
/// its own. Two agents with a different <see cref="AgentDefinition.ToolNames"/>
/// never share a cached response.
/// </para>
/// </remarks>
public sealed record ResponseCacheSettings
{
    /// <summary>Gets whether the cache ring is added to the pipeline.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets how long a cached response stays valid.</summary>
    /// <remarks>
    /// A model version can change under the same name, and a cache hit skips
    /// the content guard (it never reaches the model call it would inspect);
    /// this bounds both risks to a limited window instead of leaving a cached
    /// response valid forever.
    /// </remarks>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(10);
}
