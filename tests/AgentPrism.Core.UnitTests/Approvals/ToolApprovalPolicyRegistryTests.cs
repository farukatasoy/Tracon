namespace AgentPrism.Core.UnitTests.Approvals;

/// <summary>
/// <see cref="ToolApprovalPolicyRegistry"/>: holds the code-defined approval
/// policies registered through <c>AddToolApprovalPolicy</c> (Phase 63).
/// </summary>
public sealed class ToolApprovalPolicyRegistryTests
{
    [Fact]
    public void Registered_policy_is_found_by_tool_name()
    {
        var registry = new ToolApprovalPolicyRegistry(
        [
            new ToolApprovalPolicyRegistration("refund_order", static _ => ToolApprovalPolicyDecision.Undecided),
        ]);

        registry.TryGet("refund_order", out var policy).ShouldBeTrue();
        policy.ShouldNotBeNull();
    }

    [Fact]
    public void Unregistered_tool_is_not_found()
    {
        var registry = new ToolApprovalPolicyRegistry([]);

        registry.TryGet("refund_order", out _).ShouldBeFalse();
    }

    [Fact]
    public void Second_registration_for_the_same_tool_throws()
    {
        // A silently dropped policy would be a security regression: the
        // consumer expected it to be active. Registration fails loud instead.
        var registrations = new[]
        {
            new ToolApprovalPolicyRegistration("refund_order", static _ => ToolApprovalPolicyDecision.Required),
            new ToolApprovalPolicyRegistration("refund_order", static _ => ToolApprovalPolicyDecision.NotRequired),
        };

        Should.Throw<AgentPrismException>(() => new ToolApprovalPolicyRegistry(registrations));
    }
}
