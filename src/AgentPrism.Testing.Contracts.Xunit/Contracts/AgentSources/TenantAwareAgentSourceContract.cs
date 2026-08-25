namespace AgentPrism.Testing.Contracts.AgentSources;

/// <summary>Additional behavior tests for a tenant-aware agent source.</summary>
public abstract class TenantAwareAgentSourceContract : AgentSourceContract
{
    /// <summary>Switches the source to a tenant and returns its known agent name.</summary>
    protected abstract ValueTask<string> UseTenantAsync(string tenantId);

    [Fact]
    public async Task Tenant_lists_do_not_leak_between_tenants()
    {
        var first = await UseTenantAsync("contract-tenant-a").ConfigureAwait(false);
        var firstNames = (await Source.ListAsync().ConfigureAwait(false)).Select(static descriptor => descriptor.Name).ToArray();
        var second = await UseTenantAsync("contract-tenant-b").ConfigureAwait(false);
        var secondNames = (await Source.ListAsync().ConfigureAwait(false)).Select(static descriptor => descriptor.Name).ToArray();

        firstNames.ShouldContain(first, StringComparer.Ordinal);
        secondNames.ShouldContain(second, StringComparer.Ordinal);
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            secondNames.ShouldNotContain(first, StringComparer.Ordinal);
        }
    }
}
