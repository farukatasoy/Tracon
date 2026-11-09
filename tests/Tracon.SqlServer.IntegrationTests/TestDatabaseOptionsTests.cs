using Microsoft.Data.SqlClient;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The tests run in a database of their own on the container, and its commits do not wait for the log flush.
/// </summary>
/// <remarks>
/// The options are set once, when the container starts (<see cref="SqlServerFixture"/>). If a change to the fixture lost
/// them, every test would still pass and only the module would get slower; this case says which option went missing.
/// </remarks>
public sealed class TestDatabaseOptionsTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task The_container_opens_master_and_the_tests_open_a_database_of_their_own_without_the_log_flush()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var server = new SqlConnection(fixture.ServerConnectionString))
        {
            await server.OpenAsync(cancellationToken);

            await using var name = server.CreateCommand();
            name.CommandText = "SELECT DB_NAME();";

            (await name.ExecuteScalarAsync(cancellationToken)).ShouldBe("master");
        }

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DB_NAME(), delayed_durability_desc, recovery_model_desc,
                   CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')),
                   CONVERT(nvarchar(128), SERVERPROPERTY('Collation'))
            FROM sys.databases WHERE database_id = DB_ID();
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue();
        reader.GetString(0).ShouldBe(SqlServerFixture.DatabaseName);
        reader.GetString(1).ShouldBe("FORCED");
        reader.GetString(2).ShouldBe("SIMPLE");

        // The collation matrix legs set the server collation; a database of its own must take the server's, or those
        // legs would test a collation no table of the run has.
        reader.GetString(3).ShouldBe(reader.GetString(4));
    }
}
