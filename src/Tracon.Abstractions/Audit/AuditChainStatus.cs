using System.Text.Json.Serialization;

namespace Tracon;

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

    /// <summary>
    /// An entry names a signing key the configuration does not hold, so the walk cannot check it.
    /// This is not a sign of tampering: the key was removed from the configuration or never
    /// reached this host. Put the key back under <c>Tracon:AuditChainKeys</c> and verify again.
    /// </summary>
    UnknownKey = 3,
}
