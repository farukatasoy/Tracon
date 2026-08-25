using System.Reflection;
using AgentPrism.Testing.Contracts;
using AgentPrism.Testing.Contracts.Tools;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Tools;

public sealed class CustomToolContractTests : CustomToolContract
{
    protected override ValueTask<AgentPrismToolRegistration> CreateRegistrationAsync()
        => new(new AgentPrismToolRegistration(
            AIFunctionFactory.Create((Func<string>)(() => "ok"), "contract_status", "Returns a status."),
            effect: ToolEffect.Read));
}

public sealed class RepeatableToolContractTests : RepeatableToolContract
{
    protected override ValueTask<AgentPrismToolRegistration> CreateRegistrationAsync()
        => new(new AgentPrismToolRegistration(
            AIFunctionFactory.Create((Func<string>)(() => "ok"), "contract_retry", "Returns a retry-safe status."),
            effect: ToolEffect.External,
            safeToRepeat: true));
}

public sealed class ToolContractCoverageTests
{
    [Fact]
    public void All_tool_contracts_are_covered()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(),
            ContractCoverage.ToolContracts).ShouldBeEmpty();
}
