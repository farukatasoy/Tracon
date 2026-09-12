namespace Tracon;

/// <summary>
/// Exposes the real store implementation an audit trail decorator wraps.
/// </summary>
/// <remarks>
/// The store decorators that write the audit trail (<c>Auditing*Store</c>) must stay
/// transparent on the code paths that tell the consumer which storage implementation is
/// in use (<c>{prefix}/api/meta</c>, for example) — the user interface must get the name
/// of the real implementation, not of the decorator, when it asks which store is
/// registered.
/// </remarks>
public interface IAuditDecorated
{
    /// <summary>Gets the real store instance the decorator wraps.</summary>
    object AuditedInner { get; }
}
