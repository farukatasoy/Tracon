namespace Tracon.Testing.Contracts.AgentDefinitionSources;

/// <summary>Additional behavior tests for a tenant-aware agent definition source.</summary>
public abstract class TenantAwareAgentDefinitionSourceContract : AgentDefinitionSourceContract
{
    /// <summary>Switches the source to a tenant and returns its known agent name.</summary>
    /// <param name="tenantId">The tenant to switch to.</param>
    /// <returns>An agent name only that tenant holds.</returns>
    protected abstract ValueTask<string> UseTenantAsync(string tenantId);

    [Fact]
    public async Task Tenant_lists_do_not_leak_between_tenants()
    {
        var first = await UseTenantAsync("contract-tenant-a").ConfigureAwait(false);
        var firstNames = (await Source.ListAsync().ConfigureAwait(false)).Select(static definition => definition.Name).ToArray();
        var second = await UseTenantAsync("contract-tenant-b").ConfigureAwait(false);
        var secondNames = (await Source.ListAsync().ConfigureAwait(false)).Select(static definition => definition.Name).ToArray();

        firstNames.ShouldContain(first, StringComparer.Ordinal);
        secondNames.ShouldContain(second, StringComparer.Ordinal);

        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            secondNames.ShouldNotContain(first, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task Another_tenants_agent_cannot_be_fetched()
    {
        var first = await UseTenantAsync("contract-tenant-a").ConfigureAwait(false);
        var second = await UseTenantAsync("contract-tenant-b").ConfigureAwait(false);

        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            (await Source.GetAsync(first).ConfigureAwait(false)).ShouldBeNull();
        }
    }
}
