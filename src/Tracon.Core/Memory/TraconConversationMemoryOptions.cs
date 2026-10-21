namespace Tracon;

/// <summary>
/// Setup for conversation memory (<see cref="MemorySettings.EnableConversationMemory"/>), set with
/// <c>UseConversationMemory</c>.
/// </summary>
/// <remarks>
/// The records live in the <c>VectorStore</c> the host registers. Tracon never creates one and never
/// calls the embedding model itself: the store turns each record's text into its vector.
/// </remarks>
public sealed class TraconConversationMemoryOptions
{
    /// <summary>
    /// Gets or sets the name of the vector store collection that holds the records of every agent
    /// and tenant. Defaults to <c>tracon_conversation_memory</c>.
    /// </summary>
    public string CollectionName { get; set; } = "tracon_conversation_memory";

    /// <summary>
    /// Gets or sets the size of the vectors the store's embedding model makes. Required: it must be at
    /// least 1, and it must match the collection if the collection already exists.
    /// </summary>
    public int VectorDimensions { get; set; }
}
