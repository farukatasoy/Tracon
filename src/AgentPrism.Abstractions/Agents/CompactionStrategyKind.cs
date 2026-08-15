using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The kind of context compaction strategy that can be bound to an agent definition.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompactionStrategyKind>))]
public enum CompactionStrategyKind
{
    /// <summary>No compaction. The default.</summary>
    None,

    /// <summary>Drops the oldest turns while keeping their count above a lower bound.</summary>
    SlidingWindow,

    /// <summary>Truncates the excluded groups down to a fixed lower bound.</summary>
    Truncation,

    /// <summary>Shortens only the tool call and tool result groups.</summary>
    ToolResult,

    /// <summary>Summarizes the excluded groups with a model.</summary>
    Summarization,

    /// <summary>Evicts or truncates automatically against the model context window limit.</summary>
    ContextWindow,

    /// <summary>Applies a fixed chain: ToolResult, then SlidingWindow, then Summarization.</summary>
    Pipeline,
}
