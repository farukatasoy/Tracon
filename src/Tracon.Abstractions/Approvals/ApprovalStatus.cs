using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>The status of a pending approval request.</summary>
/// <remarks>
/// The value is written <strong>as a name</strong> in JSON, not as a number — the
/// rationale is the same as for <see cref="RunStatus"/>. It is stored as <c>smallint</c>
/// in the database; the order of the values cannot change.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalStatus>))]
public enum ApprovalStatus
{
    /// <summary>The decision is still awaited.</summary>
    Pending = 0,

    /// <summary>The operator approved the request.</summary>
    Approved = 1,

    /// <summary>The operator rejected the request.</summary>
    Rejected = 2,

    /// <summary>The request expired before a decision was made.</summary>
    Expired = 3,
}
