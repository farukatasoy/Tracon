using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// A tool Tracon adds to a run itself is held by a code-defined approval policy for its name the way a registered tool is.
/// </summary>
public sealed class BuiltInToolGovernancePolicyTests
{
    [Fact]
    public void A_built_in_tool_whose_name_has_a_policy_is_held_for_approval_without_the_flag()
    {
        var governance = BuiltInToolGovernanceTestSupport.Create(
            approvalPolicies: TestData.ApprovalPolicies(("search_knowledge", static _ => ToolApprovalPolicyDecision.Required)));

        var governed = governance.Govern(TestData.Tool("search_knowledge"), ToolEffect.External, requiresApproval: false);

        governed.GetService<ApprovalRequiredAIFunction>().ShouldNotBeNull();
        governed.GetService<PolicyGatedApprovalAIFunction>().ShouldNotBeNull();
    }

    [Fact]
    public void A_built_in_tool_without_a_policy_and_without_the_flag_is_not_held()
    {
        var governance = BuiltInToolGovernanceTestSupport.Create(
            approvalPolicies: TestData.ApprovalPolicies(("another_tool", static _ => ToolApprovalPolicyDecision.Required)));

        var governed = governance.Govern(TestData.Tool("search_knowledge"), ToolEffect.External, requiresApproval: false);

        governed.GetService<ApprovalRequiredAIFunction>().ShouldBeNull();
    }

    [Fact]
    public void A_flagged_built_in_tool_is_held_once_and_carries_no_marker()
    {
        var governance = BuiltInToolGovernanceTestSupport.Create(
            approvalPolicies: TestData.ApprovalPolicies(("file_access_write", static _ => ToolApprovalPolicyDecision.NotRequired)));

        var governed = governance.Govern(TestData.Tool("file_access_write"), ToolEffect.Write, requiresApproval: true);

        governed.GetService<ApprovalRequiredAIFunction>().ShouldNotBeNull();
        governed.GetService<PolicyGatedApprovalAIFunction>().ShouldBeNull();
    }
}
