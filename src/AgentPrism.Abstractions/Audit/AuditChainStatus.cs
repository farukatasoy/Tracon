using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The result of walking a tenant's audit trail hash chain.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuditChainStatus>))]
public enum AuditChainStatus
{
    /// <summary>Every entry's hash matches its content and links to the previous entry.</summary>
    Valid = 0,

    /// <summary>An entry's stored hash does not match its content — the row was altered.</summary>
    Broken = 1,

    /// <summary>A link is missing — a row was deleted, or a write never completed.</summary>
    Gap = 2,
}
