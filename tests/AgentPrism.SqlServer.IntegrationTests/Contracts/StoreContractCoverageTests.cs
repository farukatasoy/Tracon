using System.Reflection;
using AgentPrism.Testing.Contracts;
using AgentPrism.Testing.Contracts.Storage;

namespace AgentPrism.SqlServer.IntegrationTests.Contracts;

/// <summary>
/// Proves the SQL Server store exercises every contract class the
/// AgentPrism.Testing.Contracts.Xunit package ships -- a contract class
/// dropped during a refactor, or never picked up for a new store, fails the
/// build instead of silently shrinking coverage.
/// </summary>
public sealed class StoreContractCoverageTests
{
    [Fact]
    public void Every_contract_class_has_a_derived_test_in_this_project()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.StorageContracts).ShouldBeEmpty();
}
