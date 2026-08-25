using System.Reflection;
using AgentPrism.Testing.Contracts;

namespace AgentPrism.Samples.CustomAgentSource.Tests;

/// <summary>
/// Proves this project derives every agent-source contract the package ships, or
/// documents why it does not.
/// </summary>
/// <remarks>
/// The sample source implements only the base <see cref="IAgentSource"/> contract —
/// it is not versioned and not tenant-aware — so <c>VersionedAgentSourceContract</c>
/// and <c>TenantAwareAgentSourceContract</c> are out of scope for it; the built-in
/// <c>DefinitionStoreAgentSource</c> already covers those in
/// <c>tests/AgentPrism.Core.UnitTests</c>.
/// </remarks>
public sealed class AgentSourceContractCoverageTests
{
    /// <summary>
    /// Contracts this project deliberately does not exercise, and why.
    /// <c>DefinitionStoreAgentSource</c> already covers both in
    /// <c>tests/AgentPrism.Core.UnitTests</c>.
    /// </summary>
    private static readonly string[] Exemptions =
    [
        // The sample source keeps no version history.
        "VersionedAgentSourceContract",

        // The sample source is global: every tenant reads the same directory.
        "TenantAwareAgentSourceContract",
    ];

    [Fact]
    public void Every_applicable_agent_source_contract_has_a_derived_test_or_a_documented_exemption()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.AgentSourceContracts, Exemptions).ShouldBeEmpty();
}
