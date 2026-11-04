using Microsoft.Data.SqlClient;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// A run that asks for a server collation gets a server that already has it.
/// </summary>
/// <remarks>
/// The image applies <c>MSSQL_COLLATION</c> after the server first accepts connections: it starts with the default collation,
/// reports itself ready, then rebuilds the system databases for the one asked for and drops every connection while it does.
/// The readiness probe of the container passes at the first of those moments, so a fixture that returned at once raced the
/// rebuild: tests failed with "Connection was terminated", with a failed pre-login handshake, or with a connection reset that
/// "results in a different state than the initial login", and which tests failed changed from run to run. The fixture now waits
/// until the server answers with the collation asked for. A run that asks for none uses the default of the image and the case
/// does not apply.
/// </remarks>
public sealed class ServerCollationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task The_server_runs_with_the_collation_the_run_asked_for()
    {
        var expected = Environment.GetEnvironmentVariable(SqlServerFixture.CollationVariable);

        Assert.SkipWhen(string.IsNullOrWhiteSpace(expected), $"{SqlServerFixture.CollationVariable} is not set: the server has the default collation of the image.");

        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString) { Pooling = false };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('Collation'));";

        var actual = Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);

        actual.ShouldBe(expected);
    }
}
