namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Holds <see cref="AgentPrismSessionOwnershipOptions.ManagementPolicy"/>'s
/// default and <see cref="AgentPrismPolicies.Operator"/> to the same string.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The two live in packages that cannot see each other.
/// <c>AgentPrismPolicies</c> is in <c>AgentPrism.AspNetCore</c>;
/// <c>AgentPrismSessionOwnershipOptions</c> is in
/// <c>AgentPrism.Abstractions</c>, because <c>AgentPrism.Core</c> has to read
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
        => new AgentPrismSessionOwnershipOptions().ManagementPolicy.ShouldBe(
            AgentPrismPolicies.Operator,
            "the literal default in AgentPrism.Abstractions must stay equal to AgentPrismPolicies.Operator; " +
            "a drift here silently narrows every management listing instead of failing");
}
