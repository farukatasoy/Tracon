using Tracon.Testing.Contracts.Storage;
using Tracon.Tests.Common;
using Tracon.Tests.Differential;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>
/// Runs the differential suite against the in-memory stores: the same generated sequences and the same hand-written model
/// the SQL providers run.
/// </summary>
public sealed class InMemoryStoreDifferentialTests : StoreDifferentialSuite
{
    /// <inheritdoc />
    protected override long Iterations => 300;

    /// <inheritdoc />
    internal override ValueTask<DifferentialStores> CreateStoresAsync()
    {
        var tenant = new MutableTenantContext("tenant-a");
        var clock = new ManualClock();
        var flow = new FlowTenantContext();

        return ValueTask.FromResult(new DifferentialStores
        {
            // The in-memory stores keep a value as it came: U+0000 stays and a timestamp keeps every tick.
            Traits = new ProviderTraits { Name = "in-memory" },
            Tenant = tenant,
            Sessions = new InMemorySessionStore(tenant),
            Runs = new InMemoryRunStore(tenantContext: tenant),
            FlowTenant = flow,
            FlowRuns = new InMemoryRunStore(tenantContext: flow),
            Idempotency = new InMemoryIdempotencyStore(),
            ApiKeys = new InMemoryApiKeyStore(),
            Clock = clock,
            Jobs = new InMemoryJobStore(clock),
            Quotas = new InMemoryQuotaStore(),
            Webhooks = new InMemoryWebhookStore(),
        });
    }
}
