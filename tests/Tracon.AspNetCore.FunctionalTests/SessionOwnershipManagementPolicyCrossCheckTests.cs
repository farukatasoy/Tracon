namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Holds <see cref="TraconSessionOwnershipOptions.ManagementPolicy"/>'s
/// default and <see cref="TraconPolicies.Operator"/> to the same string.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The two live in packages that cannot see each other.
/// <c>TraconPolicies</c> is in <c>Tracon.AspNetCore</c>;
/// <c>TraconSessionOwnershipOptions</c> is in
/// <c>Tracon.Abstractions</c>, because <c>Tracon.Core</c> has to read
/// its other two members and cannot reference the endpoint package (K-006).
/// So the default is written as a LITERAL, and the compiler cannot notice when
/// one side is renamed.
/// </para>
/// <para>
/// The failure this prevents is silent and severe: the default would name a
/// policy nobody registers, every caller would fall to the narrow side, and a
/// deployment's management listing would quietly return only the operator's
/// own sessions — with no error anywhere. This test lives in the functional
/// project because it is the lowest layer that references BOTH packages, the
/// same reason <c>CostAddendsCrossCheckTests</c> sits where it does.
/// </para>
/// </remarks>
public sealed class SessionOwnershipManagementPolicyCrossCheckTests
{
    [Fact]
    public void The_default_management_policy_is_the_operator_policy_name()
        => new TraconSessionOwnershipOptions().ManagementPolicy.ShouldBe(
            TraconPolicies.Operator,
            "the literal default in Tracon.Abstractions must stay equal to TraconPolicies.Operator; " +
            "a drift here silently narrows every management listing instead of failing");
}
