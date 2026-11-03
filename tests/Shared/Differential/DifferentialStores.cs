using Tracon.Testing.Contracts.Storage;
using Tracon.Tests.Common;

namespace Tracon.Tests.Differential;

/// <summary>The store instances of one provider that the differential suite drives.</summary>
/// <remarks>
/// Every store reads the same <see cref="Tenant"/>: the stores that take the tenant from the ambient context see
/// whatever a step set, and the ones that take it as a parameter ignore it. A provider fills in the stores it has; a
/// state machine whose store is absent is skipped, so adding an interface to the suite is one property and one test.
/// </remarks>
internal sealed class DifferentialStores
{
    /// <summary>Gets the documented differences of the provider.</summary>
    public required ProviderTraits Traits { get; init; }

    /// <summary>Gets the mutable tenant context every ambient-tenant store was built with.</summary>
    public required MutableTenantContext Tenant { get; init; }

    /// <summary>
    /// Gets the clock the job store was built with, or <see langword="null"/> when the provider has none: the job
    /// state machine moves it instead of waiting, so a lease expires when a step says so.
    /// </summary>
    public ManualClock? Clock { get; init; }

    /// <summary>
    /// Gets the tenant context <see cref="FlowRuns"/> was built with: each asynchronous flow sets its own tenant, so
    /// tasks that run at the same time call the store under different ambient tenants.
    /// </summary>
    internal FlowTenantContext? FlowTenant { get; init; }

    /// <summary>
    /// Gets a run store built with <see cref="FlowTenant"/>, for the checks that run several calls at once under
    /// different ambient tenants; <see langword="null"/> when the provider has none.
    /// </summary>
    public IRunStore? FlowRuns { get; init; }

    /// <summary>Gets the session store.</summary>
    public ISessionStore? Sessions { get; init; }

    /// <summary>Gets the run store.</summary>
    public IRunStore? Runs { get; init; }

    /// <summary>Gets the idempotency store.</summary>
    public IIdempotencyStore? Idempotency { get; init; }

    /// <summary>Gets the API key store.</summary>
    public IApiKeyStore? ApiKeys { get; init; }

    /// <summary>Gets the job store.</summary>
    public IJobStore? Jobs { get; init; }

    /// <summary>Gets the quota store.</summary>
    public IQuotaStore? Quotas { get; init; }

    /// <summary>Gets the webhook store.</summary>
    public IWebhookStore? Webhooks { get; init; }

    /// <summary>Gets a store, or fails the test with the name of the missing one.</summary>
    /// <typeparam name="TStore">The store type.</typeparam>
    /// <param name="store">The store a provider supplied, or <see langword="null"/>.</param>
    /// <returns>The store.</returns>
    public static TStore Require<TStore>(TStore? store)
        where TStore : class
        => store ?? throw new InvalidOperationException($"The provider did not supply a {typeof(TStore).Name}.");
}
