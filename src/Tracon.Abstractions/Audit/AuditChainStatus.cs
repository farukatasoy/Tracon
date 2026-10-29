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
    /// A key may have been removed from the configuration or never reached this host, or the entry's
    /// key id may have been changed by hand: the walk cannot tell which, and it is never <c>Valid</c>.
    /// Put the key back under <c>Tracon:AuditChainKeys</c>, verify again, and compare the key id the
    /// entry names with the history of your keys.
    /// </summary>
    UnknownKey = 3,
}
