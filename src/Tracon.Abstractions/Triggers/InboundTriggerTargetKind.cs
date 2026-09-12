using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>What kind of target an inbound trigger fires.</summary>
/// <remarks>
/// Written as a name in JSON, stored as <c>smallint</c> in the database. The
/// value order <strong>must not change</strong> — only append; existing rows
/// reference the numeric value.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<InboundTriggerTargetKind>))]
public enum InboundTriggerTargetKind
{
    /// <summary>The trigger starts a queued agent run (<see cref="JobHandlerKeys.AgentRun"/>).</summary>
    Agent = 0,

    /// <summary>The trigger starts a queued workflow run (<see cref="JobHandlerKeys.Workflow"/>).</summary>
    Workflow = 1,
}
