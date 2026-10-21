namespace Tracon;

/// <summary>
/// Determines the memory providers bound to an agent.
/// </summary>
/// <remarks>
/// <see cref="EnableVectorSearch"/> and <see cref="EnableConversationMemory"/> are different
/// things. The first opens a semantic search tool over a knowledge base a host loaded; the
/// second remembers what one user said to this agent in earlier sessions.
/// </remarks>
public sealed record MemorySettings
{
    /// <summary>
    /// Gets a value that turns on file-based memory. It reads whichever
    /// <c>AgentFileStore</c> is registered; the built-in store keeps files in
    /// memory, so they do not survive a restart unless you register your own.
    /// </summary>
    public bool EnableFileMemory { get; init; }

    /// <summary>Gets a value that turns on todo tracking.</summary>
    public bool EnableTodo { get; init; }

    /// <summary>
    /// Gets a value that turns on text search over the file store. The search runs over
    /// the registered <c>AgentFileStore</c>, which is the in-memory store unless you
    /// register another one.
    /// </summary>
    public bool EnableTextSearch { get; init; }

    /// <summary>
    /// Gets a value that turns on the <c>search_knowledge</c> tool.
    /// PostgreSQL only: the single concrete implementation of
    /// <see cref="IVectorSearchStore"/> lives in <c>Tracon.PostgreSql</c>. When this
    /// flag is turned on while another provider is registered, the build stops with
    /// <see cref="TraconCompilationException"/>; it does not silently return an
    /// empty result.
    /// </summary>
    public bool EnableVectorSearch { get; init; }

    /// <summary>
    /// Gets the name of the collection to search. The agent name is used when it is empty.
    /// </summary>
    public string? VectorCollection { get; init; }

    /// <summary>
    /// Gets a value that turns on conversation memory: the agent finds what the same user said
    /// to this agent in earlier sessions and gets it as context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It needs <c>UseConversationMemory</c> and a <c>VectorStore</c> in the service collection;
    /// when either is missing, the build stops with <see cref="TraconCompilationException"/> and
    /// names it. Records are scoped by tenant, agent and user. A run with no user, or no session,
    /// neither reads nor writes memory. The records live in the host's vector store and go when
    /// their session goes: an erasure of its data subject, <c>DELETE /api/sessions/{id}</c> or its
    /// retention period.
    /// </para>
    /// <para>
    /// The store embeds what it keeps; Tracon does not see that model call, so its tokens and cost
    /// are not on the run.
    /// </para>
    /// </remarks>
    public bool EnableConversationMemory { get; init; }

    /// <summary>
    /// Gets when the agent searches its conversation memory. Read only while
    /// <c>EnableConversationMemory</c> is on.
    /// </summary>
    public ConversationMemorySearch ConversationMemorySearch { get; init; }

    /// <summary>
    /// Gets the most records one search returns. The framework's default (3) applies when it is
    /// <see langword="null"/>. Must be between 1 and 20.
    /// </summary>
    public int? ConversationMemoryMaxResults { get; init; }
}
