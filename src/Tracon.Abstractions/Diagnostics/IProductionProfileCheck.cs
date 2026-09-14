namespace Tracon;

/// <summary>
/// Answers one production decision while the host starts, for
/// <c>RequireProductionProfile()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Checks are registered with <c>TryAddEnumerable</c> and are only resolved
/// when an application actually called <c>RequireProductionProfile()</c>, so
/// contributing one costs a host that never calls it nothing.
/// </para>
/// <para>
/// <strong>More than one check may carry the same
/// <see cref="Risk"/>, and the strictest answer wins.</strong> That is how a
/// package refines a decision the core cannot see the whole of: the core reads
/// which <see cref="ITenantContext"/> is bound, the HTTP package adds whether
/// tenant resolution is switched on, and a deployment that satisfies only one
/// of the two is still reported as permissive.
/// </para>
/// <para>
/// A check is a COMPOSITION question, not a security proof. It reports that a
/// feature is switched on; it says nothing about whether the policy behind it
/// is right.
/// </para>
/// <para>
/// <strong>Lifetime:</strong> registered as a singleton and resolved once,
/// while the host starts. It is never resolved again, so it holds no state
/// between calls and needs no thread safety of its own.
/// </para>
/// <para>
/// <strong>Tenant mode:</strong> tenant-independent. The gate runs before the
/// first request, so there is no ambient tenant to read and the question it
/// answers is about the deployment rather than about one tenant's data.
/// </para>
/// <para>
/// <strong>Delivery:</strong> no delivery guarantee applies. This is not a
/// delivery seam; <see cref="Evaluate"/> is called once per host start, and a
/// host that never calls <c>RequireProductionProfile()</c> never calls it at
/// all.
/// </para>
/// </remarks>
public interface IProductionProfileCheck
{
    /// <summary>Gets the decision this check answers.</summary>
    TraconProductionRisk Risk { get; }

    /// <summary>Answers the decision.</summary>
    /// <param name="services">
    /// A SCOPED service provider, so a check may resolve a scoped service. It
    /// is valid only for the duration of the call.
    /// </param>
    /// <returns>What the check found.</returns>
    /// <remarks>
    /// Runs once, while the host starts. An exception thrown here stops the
    /// host and names this check: a gate that fails open is worse than no gate.
    /// </remarks>
    ProductionProfileResult Evaluate(IServiceProvider services);
}
