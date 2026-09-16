using Tracon.Testing.Contracts.Storage;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>
/// Proves that <see cref="StoreCancellationContract{TStore}"/> is not test
/// theater: a store that ignores the token fails it, and -- the half that
/// matters -- so does a store that does the work first and checks the token
/// afterwards.
/// </summary>
/// <remarks>
/// The broken fixtures below derive from a real store contract the same way a
/// third-party store would, but they are <see langword="private"/> nested
/// types, so xunit's discovery (which only picks up public classes) never runs
/// them as tests of their own; this class invokes their inherited
/// <c>[Fact]</c> methods directly.
/// </remarks>
public sealed class StoreCancellationContractSelfProofTests
{
    [Fact]
    public Task Read_case_fails_for_a_store_that_ignores_the_token()
        => AssertGoesRedAsync(new TokenBlindFixture(), static contract => contract.Canceled_token_throws_on_read());

    [Fact]
    public Task Write_case_fails_for_a_store_that_ignores_the_token()
        => AssertGoesRedAsync(
            new TokenBlindFixture(),
            static contract => contract.Canceled_token_throws_on_write_and_leaves_no_trace());

    [Fact]
    public Task Write_case_fails_for_a_store_that_checks_the_token_too_late()
        => AssertGoesRedAsync(
            new LateCheckFixture(),
            static contract => contract.Canceled_token_throws_on_write_and_leaves_no_trace());

    [Fact]
    public Task Read_case_PASSES_for_that_same_store()
        // 🚨 The reason the write case reads the store back. The late-checking
        // store throws exactly as asked, so an assertion that only watched for
        // the exception would call it sound while it was writing rows.
        => AssertStaysGreenAsync(new LateCheckFixture(), static contract => contract.Canceled_token_throws_on_read());

    private static async Task AssertGoesRedAsync(AgentSkillStoreContract contract, Func<AgentSkillStoreContract, Task> fact)
    {
        await contract.InitializeAsync().ConfigureAwait(false);

        (await ThrowsAsync(() => fact(contract)).ConfigureAwait(false)).ShouldBeTrue(
            "This deliberately broken fixture should have made the scenario fail, but it passed.");
    }

    private static async Task AssertStaysGreenAsync(AgentSkillStoreContract contract, Func<AgentSkillStoreContract, Task> fact)
    {
        await contract.InitializeAsync().ConfigureAwait(false);

        (await ThrowsAsync(() => fact(contract)).ConfigureAwait(false)).ShouldBeFalse(
            "The scenario was expected to pass for this fixture; the proof below it is worthless otherwise.");
    }

    private static async Task<bool> ThrowsAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Drops the token on every call -- the archetypal defect.</summary>
    private sealed class TokenBlindSkillStore(IAgentSkillStore inner) : IAgentSkillStore
    {
        public ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(
            string tenantId, CancellationToken cancellationToken = default)
            => inner.ListAsync(tenantId, CancellationToken.None);

        public ValueTask<AgentSkillDefinition?> GetAsync(
            string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, name, CancellationToken.None);

        public ValueTask<AgentSkillDefinition> SaveAsync(
            AgentSkillDefinition skill, CancellationToken cancellationToken = default)
            => inner.SaveAsync(skill, CancellationToken.None);

        public ValueTask<bool> DeleteAsync(
            string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, name, CancellationToken.None);
    }

    /// <summary>
    /// Observes the token, but only after the write has already landed.
    /// </summary>
    private sealed class LateCheckSkillStore(IAgentSkillStore inner) : IAgentSkillStore
    {
        public ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(
            string tenantId, CancellationToken cancellationToken = default)
            => inner.ListAsync(tenantId, cancellationToken);

        public ValueTask<AgentSkillDefinition?> GetAsync(
            string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, name, cancellationToken);

        public async ValueTask<AgentSkillDefinition> SaveAsync(
            AgentSkillDefinition skill, CancellationToken cancellationToken = default)
        {
            var saved = await inner.SaveAsync(skill, CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return saved;
        }

        public ValueTask<bool> DeleteAsync(
            string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, name, cancellationToken);
    }

    private sealed class TokenBlindFixture : AgentSkillStoreContract
    {
        protected override ValueTask<IAgentSkillStore> CreateStoreAsync()
            => ValueTask.FromResult<IAgentSkillStore>(new TokenBlindSkillStore(new InMemoryAgentSkillStore()));
    }

    private sealed class LateCheckFixture : AgentSkillStoreContract
    {
        protected override ValueTask<IAgentSkillStore> CreateStoreAsync()
            => ValueTask.FromResult<IAgentSkillStore>(new LateCheckSkillStore(new InMemoryAgentSkillStore()));
    }
}
