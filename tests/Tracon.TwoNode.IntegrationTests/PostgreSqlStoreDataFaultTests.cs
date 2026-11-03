using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>A value too long for its column on PostgreSQL, which keeps text of any length.</summary>
/// <remarks>
/// The scenarios are in <see cref="StoreDataFaultAcceptanceContract"/>. The class joins the collection of the two-node
/// set so the PostgreSQL and SQL Server containers never run at once.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class PostgreSqlStoreDataFaultTests(PostgreSqlTwoNodeServer server)
    : StoreDataFaultAcceptanceContract, IClassFixture<PostgreSqlTwoNodeServer>
{
    /// <inheritdoc />
    protected override bool RefusesLongText => false;

    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();

    /// <inheritdoc />
    protected override void UseUnreachable(ITraconBuilder builder)
        => builder.UsePostgreSql(options =>
        {
            options.ConnectionString = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1";
            options.AutoApplyMigrations = false;
        });
}
