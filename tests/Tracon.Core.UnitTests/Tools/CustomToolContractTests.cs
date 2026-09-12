using System.Reflection;
using Microsoft.Extensions.AI;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Tools;

namespace Tracon.Core.UnitTests.Tools;

public sealed class CustomToolContractTests : CustomToolContract
{
    protected override string ExpectedResultText => "ok";

    protected override ValueTask<TraconToolRegistration> CreateRegistrationAsync()
        => new(new TraconToolRegistration(
            AIFunctionFactory.Create((Func<string>)(() => "ok"), "contract_status", "Returns a status."),
            effect: ToolEffect.Read));
}

public sealed class RepeatableToolContractTests : RepeatableToolContract
{
    protected override string ExpectedResultText => "ok";

    protected override ValueTask<TraconToolRegistration> CreateRegistrationAsync()
        => new(new TraconToolRegistration(
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
