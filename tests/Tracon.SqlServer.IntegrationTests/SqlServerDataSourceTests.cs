namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// <see cref="SqlServerDataSource.ConnectionString"/> must not return the password.
/// </summary>
/// <remarks>
/// <para>
/// The shared store layer sees the database only through
/// <see cref="System.Data.Common.DbDataSource"/>, and the PostgreSQL side of that
/// seam is <c>NpgsqlDataSource</c>, whose <c>ConnectionString</c> strips the
/// password. The SQL Server adapter must honor the same contract: whatever reads
/// <c>DbDataSource.ConnectionString</c> — a diagnostics page, a log line — must
/// never receive a credential. Needs no Docker: only the adapter runs.
/// </para>
/// </remarks>
public sealed class SqlServerDataSourceTests
{
    // A fake local value used only to prove redaction.
    private const string FullConnectionString =
        "Server=localhost,1433;Database=tracon;User Id=app;Password=fake-local-pw;TrustServerCertificate=true"; // SYNTHETIC-CREDENTIAL

    [Fact]
    public void Connection_string_property_does_not_expose_the_password()
    {
        using var dataSource = new SqlServerDataSource(FullConnectionString);

        dataSource.ConnectionString.ShouldNotContain("fake-local-pw");
        dataSource.ConnectionString.ShouldContain("localhost,1433");
    }

    [Fact]
    public void Created_connections_still_receive_the_full_connection_string()
    {
        using var dataSource = new SqlServerDataSource(FullConnectionString);

        using var connection = dataSource.CreateConnection();

        connection.ConnectionString.ShouldContain("fake-local-pw");
    }
}
