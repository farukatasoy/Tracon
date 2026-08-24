using System.Reflection;
using AgentPrism.Testing.Contracts.Storage;

namespace AgentPrism.Core.UnitTests.Contracts;

/// <summary>
/// Proves the in-memory store exercises every contract class the
/// AgentPrism.Testing.Contracts.Xunit package ships -- a contract class
/// dropped during a refactor, or never picked up for a new store, fails the
/// build instead of silently shrinking coverage.
/// </summary>
public sealed class StoreContractCoverageTests
{
    /// <summary>
    /// Contracts this project deliberately does not exercise, and why. The
    /// three SQL integration test projects cover both in full.
    /// </summary>
    private static readonly string[] Exemptions =
    [
        // No in-memory AgentFileStore is registered by AddAgentPrism() without
        // a persistence package; only the three SQL providers back file memory.
        "AgentFileStoreContract",

        // The in-memory deployment registers NullRetentionStore (a no-op):
        // there is no data plane to sweep when nothing is persisted.
        "RetentionStoreContract",
    ];

    [Fact]
    public void Every_contract_class_has_a_derived_test_or_a_documented_exemption()
        => ContractCoverage.MissingDerivedTypes(Assembly.GetExecutingAssembly(), Exemptions).ShouldBeEmpty();
}
