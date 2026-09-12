using System.Reflection;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests.Contracts;

/// <summary>
/// Proves the SQL Server store exercises every contract class the
/// Tracon.Testing.Contracts.Xunit package ships -- a contract class
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
