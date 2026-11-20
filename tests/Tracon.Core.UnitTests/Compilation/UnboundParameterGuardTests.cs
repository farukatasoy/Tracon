using Tracon.Core.UnitTests.Contracts;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The error of a run that starts without a value for a required parameter tells every way to start it, the one that
/// runs in .NET code included.
/// </summary>
public sealed class UnboundParameterGuardTests
{
    [Fact]
    public void The_error_names_the_parameter_the_http_run_and_the_way_from_code()
    {
        var guard = new UnboundParameterGuard(
            new ContractAgent(),
            "billing-agent",
            AgentParameterValidator.ValidateValues(
                [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }],
                values: null));

        var message = guard.Failure().Message;

        message.ShouldContain("'billing-agent'", Case.Sensitive);
        message.ShouldContain("'customer'", Case.Sensitive);
        message.ShouldContain("POST /api/agents/{name}/run", Case.Sensitive);
        message.ShouldContain("IAgentCatalog.ResolveAsync", Case.Sensitive);
        message.ShouldContain("AgentResolveOptions.Parameters", Case.Sensitive);
        message.ShouldContain("default value", Case.Sensitive);
    }
}
