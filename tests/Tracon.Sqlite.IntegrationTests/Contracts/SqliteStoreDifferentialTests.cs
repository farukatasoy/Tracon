using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Tracon.Tests.Common;
using Tracon.Tests.Differential;

namespace Tracon.Sqlite.IntegrationTests.Contracts;

/// <summary>Runs the differential suite against the SQLite stores.</summary>
public sealed class SqliteStoreDifferentialTests(SqliteSchemaFixture schema)
    : StoreDifferentialSuite, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    internal override async ValueTask<DifferentialStores> CreateStoresAsync()
    {
        await schema.ResetAsync();

        var clock = new ManualClock();
        var flow = new FlowTenantContext();

        return new DifferentialStores
        {
            Traits = new ProviderTraits { Name = "SQLite", NulBecomesReplacementCharacter = true },
            Tenant = schema.Tenant,
            Sessions = schema.Context.Sessions,
            Runs = schema.Context.Runs,
            FlowTenant = flow,
            FlowRuns = new SqlRunStore(schema.Context.StoreContext, flow),
            Idempotency = schema.Context.IdempotencyKeys,
            ApiKeys = schema.Context.ApiKeys,
            Clock = DifferentialClock.Of(clock),
            Jobs = new SqlJobStore(schema.Context.StoreContext, timeProvider: clock),
            Quotas = schema.Context.Quotas,
            Webhooks = schema.Context.Webhooks,
        };
    }
}
