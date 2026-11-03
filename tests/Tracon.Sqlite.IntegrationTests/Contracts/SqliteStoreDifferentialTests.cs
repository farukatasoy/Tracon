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

        return new DifferentialStores
        {
            Traits = new ProviderTraits { Name = "SQLite", NulBecomesReplacementCharacter = true, ReadEchoesRequestedKey = true, CreateEchoesInputText = true, OutcomeAnswersForASubscriptionThatIsOff = true, CostRelativeTolerance = 0.000000000001m },
            Tenant = schema.Tenant,
            Sessions = schema.Context.Sessions,
            Runs = schema.Context.Runs,
            Idempotency = schema.Context.IdempotencyKeys,
            ApiKeys = schema.Context.ApiKeys,
            Clock = clock,
            Jobs = new SqlJobStore(schema.Context.StoreContext, timeProvider: clock),
            Quotas = schema.Context.Quotas,
            Webhooks = schema.Context.Webhooks,
        };
    }
}
