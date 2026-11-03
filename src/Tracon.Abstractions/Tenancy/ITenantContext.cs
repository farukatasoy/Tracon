namespace Tracon;

/// <summary>
/// Resolves the current request's tenant. In a single-tenant setup, returns a
/// fixed value and requires no additional configuration.
/// </summary>
/// <remarks>
/// In multi-tenant scenarios, the application binds this interface
/// to its own identity infrastructure: an HTTP header, a claim, or a subdomain.
/// </remarks>
public interface ITenantContext
{
    /// <summary>The current tenant's identifier. Never returns empty.</summary>
    /// <remarks>
    /// <para>
    /// <strong>The value must be canonical</strong> — the form
    /// <see cref="AmbientTenantScope.Normalize"/> returns. The tenant
    /// identifier is matched case-insensitively across the product, and every
    /// layer achieves that by normalizing the value rather than by comparing
    /// case-insensitively.
    /// </para>
    /// <para>
    /// Every store folds the value it is given, in memory and in SQL alike, so
    /// a context that returns <c>Acme</c> does not split the stored data. The
    /// code that keys on the text it receives does split it: a metric tag or a
    /// per-tenant cache entry counts <c>Acme</c> and <c>acme</c> as two tenants,
    /// and an ordinal comparison with a canonical value says they differ.
    /// </para>
    /// <para>
    /// <strong>The value may hold only the ASCII letters, the digits, <c>_</c>, <c>.</c> and <c>-</c>.</strong>
    /// A store refuses any other character with an <see cref="ArgumentException"/> on the first call, so a
    /// context that derives the value from client text (a header, a claim, a subdomain) checks it first with
    /// <see cref="AmbientTenantScope.TryNormalize"/> and answers the client, instead of letting the request
    /// fail inside a store. A relational engine compares text by the collation of its column, and a
    /// collation may join two spellings outside this set (an accented letter composed or decomposed, a
    /// full-width letter) into one tenant.
    /// </para>
    /// </remarks>
    string TenantId { get; }
}
