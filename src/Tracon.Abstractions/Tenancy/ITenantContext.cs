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
    /// <strong>The value must be canonical</strong> — the form
    /// <see cref="AmbientTenantScope.Normalize"/> returns. The tenant
    /// identifier is matched case-insensitively across the product, and every
    /// layer achieves that by normalizing the value rather than by comparing
    /// case-insensitively. An implementation that returns <c>Acme</c> where
    /// another path resolves <c>acme</c> splits one tenant into two on
    /// PostgreSQL and SQLite, and merges them on SQL Server's default
    /// collation — the authorization layer and the storage layer then disagree.
    /// </remarks>
    string TenantId { get; }
}
