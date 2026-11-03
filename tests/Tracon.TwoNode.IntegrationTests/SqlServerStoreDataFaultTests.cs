using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>A value too long for its column on SQL Server, which refuses it.</summary>
/// <remarks>
/// The scenarios are in <see cref="StoreDataFaultAcceptanceContract"/>; see
/// <see cref="PostgreSqlDefinitionTransferTests"/> for why the class shares a collection.
/// </remarks>
[Collection(TwoNodeAcceptanceSerial.Name)]
public sealed class SqlServerStoreDataFaultTests(SqlServerTwoNodeServer server)
    : StoreDataFaultAcceptanceContract, IClassFixture<SqlServerTwoNodeServer>
{
    /// <inheritdoc />
    protected override bool RefusesLongText => true;

    /// <inheritdoc />
    protected override TwoNodeDatabase NewDatabase() => server.NewDatabase();

    /// <inheritdoc />
    protected override void UseUnreachable(ITraconBuilder builder)
        => builder.UseSqlServer(options =>
        {
            options.ConnectionString = "Server=127.0.0.1,1;Database=unused;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=1";
            options.AutoApplyMigrations = false;
        });
}
