namespace Tracon.Tests.Differential;

/// <summary>
/// A tenant context whose value belongs to the asynchronous flow that set it, so several tasks that call one store at
/// the same time each run under a tenant of their own.
/// </summary>
/// <remarks>
/// The shared tenant context of the contract tests is one value: a race between calls that name different ambient tenants
/// would see whichever tenant was set last. A store built with this context reads the tenant its caller's flow set. A flow
/// sets it before the call and never relies on it afterwards: a value written inside an async method does not reach its
/// caller.
/// </remarks>
internal sealed class FlowTenantContext : ITenantContext
{
    private readonly AsyncLocal<string?> _current = new();

    /// <inheritdoc />
    public string TenantId => _current.Value ?? throw new InvalidOperationException("No tenant was set for this flow.");

    /// <summary>Sets the tenant of the current flow.</summary>
    /// <param name="tenantId">The tenant spelling the stores under test receive.</param>
    public void Set(string tenantId) => _current.Value = tenantId;
}
