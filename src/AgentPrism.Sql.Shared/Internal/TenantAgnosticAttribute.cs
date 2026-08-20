namespace AgentPrism;

/// <summary>
/// Exempts a store method that carries no tenant concept from the tenant
/// isolation coverage check.
/// </summary>
/// <remarks>
/// <para>
/// <c>TenantCoverageTests</c> verifies that every public
/// method in the shared store layer is either exercised by the tenant isolation
/// contract or exempted by this attribute <strong>with a written reason</strong>.
/// A method added tomorrow therefore cannot stay untested silently.
/// </para>
/// <para>
/// The attribute is <c>internal</c> and does not grow the public contract.
/// Only the test project uses reflection over it; product code never reads this
/// type and the AOT posture is unaffected.
/// </para>
/// </remarks>
/// <param name="reason">
/// Why the method does not filter by tenant. Cannot be left empty; being visible
/// in code review is what separates this attribute from a list file.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TenantAgnosticAttribute(string reason) : Attribute
{
    /// <summary>Gets the reason for the exemption.</summary>
    public string Reason { get; } = reason;
}
