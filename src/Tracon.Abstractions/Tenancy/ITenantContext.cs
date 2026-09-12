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
    string TenantId { get; }
}
