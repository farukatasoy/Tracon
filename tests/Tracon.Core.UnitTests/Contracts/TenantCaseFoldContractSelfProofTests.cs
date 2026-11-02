using System.Text;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>
/// Proves that the letter-case scenario of <see cref="TenantIsolationContract{TStore}"/> is not test theater: a
/// store that compares the tenant as raw text fails it, a store that folds the tenant passes it, and a store that
/// "folds" by merging every tenant fails it.
/// </summary>
/// <remarks>
/// <para>
/// The scenario is worth nothing if a raw store can pass it. The fixtures below derive from a real store contract
/// the way a third-party store would. They are <see langword="private"/> nested types, so xunit's discovery never
/// runs them as tests of their own; this class invokes the inherited scenario directly.
/// </para>
/// <para>
/// The raw store is the real in-memory store behind a wrapper that re-keys every tenant to the hex of its UTF-8
/// bytes. Hex digits are already lower case, so the wrapped store's own fold cannot merge two spellings, and the
/// result behaves exactly like a store that compares the tenant text as it was written.
/// </para>
/// </remarks>
public sealed class TenantCaseFoldContractSelfProofTests
{
    [Theory]
    [InlineData("Tenant-A", "tenant-a")]
    [InlineData("tenant-a", "TENANT-A")]
    public async Task The_scenario_fails_for_a_store_that_compares_the_tenant_as_written(string writtenAs, string readAs)
        => (await FailureOf(new RawTenantFixture(), writtenAs, readAs)).ShouldNotBeNull(
            "A store that compares the raw tenant text passed the scenario; the scenario proves nothing.");

    [Theory]
    [InlineData("Tenant-A", "tenant-a")]
    [InlineData("tenant-a", "TENANT-A")]
    public async Task The_scenario_passes_for_a_store_that_folds_the_tenant(string writtenAs, string readAs)
        // 🚨 The half that makes the proof above worth something: the same scenario, the same data, a sound store.
        // Without it a scenario that fails for every store would pass the proof above.
        => (await FailureOf(new FoldingTenantFixture(), writtenAs, readAs)).ShouldBeNull(
            "The scenario failed for a store that folds the tenant correctly.");

    [Theory]
    [InlineData("Tenant-A", "tenant-a")]
    [InlineData("tenant-a", "TENANT-A")]
    public async Task The_scenario_fails_for_a_store_that_merges_every_tenant(string writtenAs, string readAs)
    {
        var failure = await FailureOf(new MergedTenantsFixture(), writtenAs, readAs);

        failure.ShouldNotBeNull("A store that gives every tenant the same records passed the scenario.");

        // Failing is not enough: it has to fail for the right reason, the check on the other tenant.
        failure.ShouldContain("merged it with another tenant");
    }

    /// <summary>Runs the scenario and returns the failure message, or <see langword="null"/> when it passed.</summary>
    private static async Task<string?> FailureOf(AgentSkillStoreContract contract, string writtenAs, string readAs)
    {
        await contract.InitializeAsync().ConfigureAwait(false);

        try
        {
            await contract.Tenant_identifiers_that_differ_only_in_letter_case_are_one_tenant(writtenAs, readAs)
                .ConfigureAwait(false);

            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    /// <summary>Re-keys the tenant of every call, leaving the rest of the store as it is.</summary>
    private sealed class RekeyedSkillStore(IAgentSkillStore inner, Func<string, string> rekey) : IAgentSkillStore
    {
        public ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(
            string tenantId, CancellationToken cancellationToken = default)
            => inner.ListAsync(rekey(tenantId), cancellationToken);

        public ValueTask<AgentSkillDefinition?> GetAsync(
            string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.GetAsync(rekey(tenantId), name, cancellationToken);

        public ValueTask<AgentSkillDefinition> SaveAsync(
            AgentSkillDefinition skill, CancellationToken cancellationToken = default)
            => inner.SaveAsync(skill with { TenantId = rekey(skill.TenantId) }, cancellationToken);

        public ValueTask<bool> DeleteAsync(
            string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(rekey(tenantId), name, cancellationToken);
    }

    private sealed class RawTenantFixture : AgentSkillStoreContract
    {
        protected override ValueTask<IAgentSkillStore> CreateStoreAsync()
            => ValueTask.FromResult<IAgentSkillStore>(new RekeyedSkillStore(
                new InMemoryAgentSkillStore(),
                static tenantId => Convert.ToHexStringLower(Encoding.UTF8.GetBytes(tenantId))));
    }

    private sealed class FoldingTenantFixture : AgentSkillStoreContract
    {
        protected override ValueTask<IAgentSkillStore> CreateStoreAsync()
            => ValueTask.FromResult<IAgentSkillStore>(new InMemoryAgentSkillStore());
    }

    private sealed class MergedTenantsFixture : AgentSkillStoreContract
    {
        protected override ValueTask<IAgentSkillStore> CreateStoreAsync()
            => ValueTask.FromResult<IAgentSkillStore>(new RekeyedSkillStore(
                new InMemoryAgentSkillStore(),
                static _ => "one-tenant-for-everyone"));
    }
}
