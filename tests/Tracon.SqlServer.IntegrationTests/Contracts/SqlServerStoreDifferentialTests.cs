using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Tests.Common;
using Tracon.Tests.Differential;

namespace Tracon.SqlServer.IntegrationTests.Contracts;

/// <summary>Runs the differential suite against the SQL Server stores.</summary>
public sealed class SqlServerStoreDifferentialTests(SqlServerSchemaFixture schema)
    : StoreDifferentialSuite, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override long Iterations => 60;

    /// <inheritdoc />
    internal override async ValueTask<DifferentialStores> CreateStoresAsync()
    {
        try
        {
            await schema.ResetAsync();
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            // On a server started with a Turkish language the first pooled connection can come back from its reset with
            // a login error ("the reset results in a different state than the initial login"). The pool drops that
            // connection, so the second attempt opens a new one.
            await schema.ResetAsync();
        }

        // The lease statements read the database server's clock, so the sequence moves a stand-in for it; the node's own
        // clock is set three days away to show that the store never looks at it.
        var clock = await SqlServerDifferentialClock.CreateAsync(schema.Context);
        var nodeClock = new ManualClock();
        nodeClock.Advance(TimeSpan.FromDays(3));
        var flow = new FlowTenantContext();

        return new DifferentialStores
        {
            Traits = new ProviderTraits
            {
                Name = "SQL Server",
                NulBecomesReplacementCharacter = true,
                ReadEchoesRequestedKey = true,
                CreateEchoesInputText = true,
                OutcomeAnswersForASubscriptionThatIsOff = true,
                MultiRowReadIsNotASnapshot = true,
                CostDecimals = 10,
            },
            Tenant = schema.Tenant,
            Sessions = schema.Context.Sessions,
            Runs = schema.Context.Runs,
            FlowTenant = flow,
            FlowRuns = new SqlRunStore(schema.Context.StoreContext, flow),
            Idempotency = schema.Context.IdempotencyKeys,
            ApiKeys = schema.Context.ApiKeys,
            Clock = clock,
            Jobs = new SqlJobStore(schema.Context.StoreContext, timeProvider: nodeClock),
            Quotas = schema.Context.Quotas,
            Webhooks = schema.Context.Webhooks,
        };
    }
}
