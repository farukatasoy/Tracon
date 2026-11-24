using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The tenants a retention fan-out sweeps when the host registers no <see cref="IRetentionTenantSource"/> of its own:
/// the host tenant, the registered tenants, and the tenants that own a retention policy.
/// </summary>
/// <remarks>
/// A tenant that is in none of the three is not named: Tracon has no other way to learn that it exists. That limit is
/// stated on <see cref="IRetentionTenantSource"/>, and a host closes it by registering a source of its own.
/// </remarks>
internal sealed class DefaultRetentionTenantSource(
    ITenantStore tenantStore,
    IRetentionPolicyStore policyStore,
    IOptionsMonitor<TraconOptions> coreOptions) : IRetentionTenantSource
{
    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListTenantsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The host tenant first: it is the one tenant that exists without a registration or a policy row.
        yield return coreOptions.CurrentValue.DefaultTenantId;

        var registered = await tenantStore.ListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var tenant in registered)
        {
            yield return tenant.Slug;
        }

        await foreach (var tenantId in policyStore.ListPolicyTenantsAsync(cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return tenantId;
        }
    }
}
