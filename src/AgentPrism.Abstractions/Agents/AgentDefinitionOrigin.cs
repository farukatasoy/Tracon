using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Tells where an agent definition came from.</summary>
/// <remarks>
/// The value is written <strong>as a name</strong> in JSON (<c>"Code"</c>), not as a
/// number. The wire contract is therefore self describing and survives a change in the
/// order of the values. The converter sits at type level: it gives the same format
/// everywhere without touching the consumer's application-wide JSON settings. No enum
/// is PERSISTED as JSON (RunStatus and RunEventType are smallint in the database, and
/// AgentDefinitionOrigin is rebuilt on read), so a change of format does not affect
/// stored data.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentDefinitionOrigin>))]
public enum AgentDefinitionOrigin
{
    /// <summary>
    /// The definition is made in code. It is validated at compile time, so it wins over
    /// a database definition when the names clash.
    /// </summary>
    Code = 0,

    /// <summary>The definition is stored in the database and built at run time.</summary>
    Database = 1,
}
