using Microsoft.Extensions.VectorData;

namespace Tracon;

/// <summary>
/// The shape of the records conversation memory keeps, and the filters that find them.
/// </summary>
/// <remarks>
/// <para>
/// <c>ChatHistoryMemoryProvider</c> of Microsoft Agent Framework writes the records and builds the
/// collection from a definition it keeps private. Tracon reads and deletes the same records for an
/// erasure, so it needs the same definition: the field names are the contract, and the property types
/// and indexes are what a store creates its collection from. <c>ConversationMemoryTests</c> compares
/// this definition with the one the framework hands the store, so a framework version that changes the
/// record fails there and not in a store.
/// </para>
/// </remarks>
internal static class ConversationMemorySchema
{
    /// <summary>The record field that holds the tenant.</summary>
    public const string TenantField = "ApplicationId";

    /// <summary>The record field that holds the agent name.</summary>
    public const string AgentField = "AgentId";

    /// <summary>The record field that holds the user.</summary>
    public const string UserField = "UserId";

    /// <summary>The record field that holds the session the message belongs to.</summary>
    public const string SessionField = "SessionId";

    /// <summary>The record field that holds the key.</summary>
    public const string KeyField = "Key";

    /// <summary>The record field that holds the message text.</summary>
    public const string ContentField = "Content";

    /// <summary>The record field that holds the role of the message author.</summary>
    public const string RoleField = "Role";

    /// <summary>The record field that holds the creation time.</summary>
    public const string CreatedAtField = "CreatedAt";

    /// <summary>Builds the definition of a memory collection.</summary>
    /// <param name="vectorDimensions">The size of the vectors.</param>
    /// <returns>The definition.</returns>
    public static VectorStoreCollectionDefinition CreateDefinition(int vectorDimensions)
        => new()
        {
            Properties =
            [
                new VectorStoreKeyProperty(KeyField, typeof(Guid)),
                new VectorStoreDataProperty(RoleField, typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("MessageId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("AuthorName", typeof(string)),
                new VectorStoreDataProperty(TenantField, typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(AgentField, typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(UserField, typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(SessionField, typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(ContentField, typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreDataProperty(CreatedAtField, typeof(string)) { IsIndexed = true },
                new VectorStoreVectorProperty("ContentEmbedding", typeof(string), vectorDimensions),
            ],
        };

    /// <summary>The filter for the records one session of one tenant owns.</summary>
    /// <param name="tenantId">The tenant. A session id is unique inside a tenant only.</param>
    /// <param name="sessionId">The session.</param>
    /// <returns>The filter.</returns>
    public static System.Linq.Expressions.Expression<Func<Dictionary<string, object?>, bool>> ForSession(string tenantId, string sessionId)
        => record => (string?)record[TenantField] == tenantId && (string?)record[SessionField] == sessionId;
}
