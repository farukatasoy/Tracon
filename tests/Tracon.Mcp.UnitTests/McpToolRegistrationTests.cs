using Microsoft.Extensions.AI;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The registration every MCP-sourced tool is built with. A remote tool that
/// lost its server name would look like a tool defined in code, and one that
/// lost the approval setting would run a remote, changeable definition
/// without approval.
/// </summary>
public sealed class McpToolRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registration_carries_the_server_name_and_the_approval_setting(bool requiresApproval)
    {
        var function = AIFunctionFactory.Create(() => "ok", "crm_lookup");

        var registration = McpConnection.CreateRegistration(function, "crm", requiresApproval);

        registration.Function.ShouldBeSameAs(function);
        registration.Source.ShouldBe("crm");
        registration.RequiresApproval.ShouldBe(requiresApproval);
    }

    [Fact]
    public void Registration_leaves_every_other_setting_on_its_default()
    {
        var registration = McpConnection.CreateRegistration(
            AIFunctionFactory.Create(() => "ok", "crm_lookup"),
            "crm",
            requiresApproval: true);

        registration.Effect.ShouldBe(ToolEffect.Read);
        registration.RequiredPermission.ShouldBeNull();
        registration.Timeout.ShouldBeNull();
        registration.SafeToRepeat.ShouldBeFalse();
        registration.MaxOutputBytes.ShouldBeNull();
    }
}
