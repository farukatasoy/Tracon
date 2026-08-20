namespace AgentPrism;

/// <summary>
/// Determines the memory providers bound to an agent.
/// </summary>
/// <remarks>
/// The <c>ChatHistoryMemoryProvider</c> of MAF (vector-based in-session memory) is
/// deliberately absent here, and must not be confused with
/// <see cref="EnableVectorSearch"/> — that one opens a semantic search tool over a
/// persistent knowledge base and does not bind that particular MAF contract. Rationale
/// and boundary:
/// </remarks>
public sealed record MemorySettings
{
    /// <summary>
    /// Gets a value that turns on file-based memory. In this phase it works only with
    /// the in-memory store; the persistent version is left to a later phase.
    /// </summary>
    public bool EnableFileMemory { get; init; }

    /// <summary>Gets a value that turns on todo tracking.</summary>
    public bool EnableTodo { get; init; }

    /// <summary>
    /// Gets a value that turns on text search over the file store. The search runs over
    /// the registered <c>AgentFileStore</c>, which in this phase is the in-memory store
    /// by default.
    /// </summary>
    public bool EnableTextSearch { get; init; }

    /// <summary>
    /// Gets a value that turns on the <c>search_knowledge</c> tool.
    /// PostgreSQL only: the single concrete implementation of
    /// <see cref="IVectorSearchStore"/> lives in <c>AgentPrism.PostgreSql</c>. When this
    /// flag is turned on while another provider is registered, the build stops with
    /// <see cref="AgentPrismCompilationException"/>; it does not silently return an
    /// empty result.
    /// </summary>
    public bool EnableVectorSearch { get; init; }

    /// <summary>
    /// Gets the name of the collection to search. The agent name is used when it is empty.
    /// </summary>
    public string? VectorCollection { get; init; }
}
