using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>How an inbound trigger's request body becomes the agent/workflow message.</summary>
/// <remarks>
/// Written as a name in JSON, stored as <c>smallint</c> in the database. The
/// value order <strong>must not change</strong> — only append; existing rows
/// reference the numeric value.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<InboundTriggerPayloadMode>))]
public enum InboundTriggerPayloadMode
{
    /// <summary>The whole request body becomes the message, as JSON text.</summary>
    WholeBody = 0,

    /// <summary>A single field, selected by <see cref="InboundTrigger.PayloadPath"/>, becomes the message.</summary>
    Path = 1,
}
