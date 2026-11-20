using System.Reflection;
using Tracon.Testing.Contracts;

namespace Tracon.Samples.CustomAgentSource.Tests;

/// <summary>
/// Proves this project derives every agent-source contract the package ships, or
/// documents why it does not.
/// </summary>
/// <remarks>
/// The sample source implements only the base <see cref="IAgentDefinitionSource"/> contract —
/// it is not versioned and not tenant-aware — so the versioned and tenant-aware definition suites
/// are out of scope for it, and so is the whole family for <see cref="IAgentSource"/>, which is a
/// different seam (a source that builds its own agent). The built-in sources already cover those in
/// <c>tests/Tracon.Core.UnitTests</c>.
/// </remarks>
public sealed class AgentDefinitionSourceContractCoverageTests
{
    /// <summary>Contracts this project deliberately does not exercise, and why.</summary>
    private static readonly string[] Exemptions =
    [
        // The sample source keeps no version history.
        "VersionedAgentDefinitionSourceContract",

        // The sample source is global: every tenant reads the same directory.
        "TenantAwareAgentDefinitionSourceContract",

        // The sample is a definition source, not a source that builds its own agent.
        "AgentSourceContract",
        "VersionedAgentSourceContract",
        "TenantAwareAgentSourceContract",
    ];

    [Fact]
    public void Every_applicable_agent_source_contract_has_a_derived_test_or_a_documented_exemption()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.AgentSourceContracts, Exemptions).ShouldBeEmpty();
}
