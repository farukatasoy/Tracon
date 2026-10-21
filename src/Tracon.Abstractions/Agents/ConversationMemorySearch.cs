using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>When an agent searches its conversation memory (<c>EnableConversationMemory</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConversationMemorySearch>))]
public enum ConversationMemorySearch
{
    /// <summary>Searches before every agent turn and adds what it finds as context. The default.</summary>
    BeforeEachTurn,

    /// <summary>
    /// Offers the model a search tool, <c>search_conversation_memory</c>. The model decides when to
    /// search and what to ask for.
    /// </summary>
    OnDemand,
}
