using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>How an agent calls the agents it may call (<c>CallableAgentNames</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubAgentCallMode>))]
public enum SubAgentCallMode
{
    /// <summary>
    /// The agent starts a background task, waits for it and reads its result with the Microsoft
    /// Agent Framework task tools. Several sub-agents run in parallel. The default.
    /// </summary>
    Background,

    /// <summary>
    /// Each callable agent is one tool, named after the agent. One call runs the sub-agent and
    /// returns its answer. The model sees one tool per agent instead of the task tools, so a
    /// single delegation costs fewer model calls and fewer prompt tokens; the calling agent does
    /// not start sub-agents in parallel on its own account.
    /// </summary>
    Tool,
}
