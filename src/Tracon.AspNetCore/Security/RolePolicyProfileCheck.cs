namespace Tracon;

/// <summary>
/// Answers whether the three endpoint role policies are registered.
/// </summary>
/// <remarks>
/// <para>
/// Built by <c>MapTracon</c> from the answer it already resolved and added to
/// <see cref="LateProductionProfileChecks"/>, not registered in the container: which
/// policies exist is known only there, and asking again would run the policy provider a
/// second time. A host that never calls <c>MapTracon</c> has no such check, and the
/// validator reports the decision as not applicable.
/// </para>
/// <para>
/// With <see cref="TraconEndpointOptions.RequireRolePolicies"/> on, <c>MapTracon</c> has
/// already failed for a missing policy, so this answer is always satisfied then.
/// </para>
/// </remarks>
internal sealed class RolePolicyProfileCheck : IProductionProfileCheck
{
    private const string Setting = "AddAuthorization(TraconPolicies.*)";

    private readonly string[] _missing;

    /// <summary>Creates the check from the policies <c>MapTracon</c> resolved.</summary>
    /// <param name="roles">The resolved role policies.</param>
    /// <exception cref="ArgumentNullException"><paramref name="roles"/> is <see langword="null"/>.</exception>
    public RolePolicyProfileCheck(TraconRolePolicies roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var missing = new List<string>(3);

        if (roles.Reader is null)
        {
            missing.Add(TraconPolicies.Reader);
        }

        if (roles.Operator is null)
        {
            missing.Add(TraconPolicies.Operator);
        }

        if (roles.Admin is null)
        {
            missing.Add(TraconPolicies.Admin);
        }

        _missing = [.. missing];
    }

    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnregisteredRolePolicies;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
        => _missing.Length == 0
            ? ProductionProfileResult.Satisfied(Setting)
            : ProductionProfileResult.Permissive(
                Setting,
                $"{_missing.Length} of the 3 role policies are not registered ({string.Join(", ", _missing)}), so " +
                "the endpoints they guard get no role check and any caller that passes the outer layers reaches them",
                "define Tracon.Reader, Tracon.Operator and Tracon.Admin inside " +
                "builder.Services.AddAuthorization(...) with TraconPolicies.Reader, TraconPolicies.Operator and " +
                "TraconPolicies.Admin, and set TraconEndpointOptions.RequireRolePolicies to true so a missing " +
                "one stops MapTracon too");
}
