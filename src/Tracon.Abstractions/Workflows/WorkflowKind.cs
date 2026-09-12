using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// Represents the built-in workflow patterns that can be defined through the
/// UI.
/// </summary>
/// <remarks>
/// <para>
/// This list is deliberately <strong>closed</strong>. A workflow defined
/// through the UI only wires together agents from the catalog; it produces
/// no new behavior. A free-form graph (custom <c>Executor</c> types) can
/// only be defined in code. The code-only tools rule states that tools are only
/// defined in code".
/// </para>
/// <para>
/// Written as a <strong>name</strong> in JSON (<c>"Sequential"</c>), not a
/// number. Also stored as a name in the database: <c>workflows.definition</c>
/// is a JSON document, and making old rows unreadable if the value order
/// changes is not acceptable.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowKind>))]
public enum WorkflowKind
{
    /// <summary>Agents run in sequence; each one's output is the next one's input.</summary>
    Sequential = 0,

    /// <summary>Agents run at the same time; results are merged.</summary>
    Concurrent = 1,

    /// <summary>
    /// The first agent starts the work and hands off to another agent when
    /// needed. The model itself decides on the handoff.
    /// </summary>
    Handoff = 2,

    /// <summary>
    /// A manager distributes turns among participating agents.
    /// <see cref="WorkflowDefinition.MaxIterations"/> limits the turn count.
    /// </summary>
    GroupChat = 3,

    /// <summary>
    /// The manager agent builds a plan, tracks progress, and replans when
    /// needed. <see cref="WorkflowDefinition.ManagerAgentName"/> is required.
    /// </summary>
    Magentic = 4,
}
