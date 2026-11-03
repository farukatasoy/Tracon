using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Tests.Common;
using Tracon.Tests.Differential;

namespace Tracon.PostgreSql.IntegrationTests.Contracts;

/// <summary>Runs the differential suite against the PostgreSQL stores.</summary>
public sealed class PostgresStoreDifferentialTests(PostgresSchemaFixture schema)
    : StoreDifferentialSuite, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override long Iterations => 60;

    /// <inheritdoc />
    internal override async ValueTask<DifferentialStores> CreateStoresAsync()
    {
        await schema.ResetAsync();

        var clock = new ManualClock();

        return new DifferentialStores
        {
            Traits = new ProviderTraits
            {
                Name = "PostgreSQL",
                NulBecomesReplacementCharacter = true,

                // timestamptz keeps microseconds, counted from 2000-01-01.
                TimestampGranularityTicks = 10,
                TimestampOriginTicks = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks,
                MaxInstantIsExact = true,
                ReadEchoesRequestedKey = true,
                CreateEchoesInputText = true,
                OutcomeAnswersForASubscriptionThatIsOff = true,
                CostDecimals = 10,
            },
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
