namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// <see cref="SqliteDataSource.ConnectionString"/> must not return the password.
/// </summary>
/// <remarks>
/// <para>
/// Same contract as the SQL Server adapter (see
/// <c>SqlServerDataSourceTests</c>): <c>NpgsqlDataSource.ConnectionString</c>
/// strips the password, so every <see cref="System.Data.Common.DbDataSource"/>
/// this repository hands the shared store layer must do the same. SQLite carries
/// a password only in the SQLCipher configuration, but the adapter cannot know
/// whether the consumer's build is encrypted — it redacts unconditionally.
/// </para>
/// </remarks>
public sealed class SqliteDataSourceTests
{
    // A fake local value used only to prove redaction.
    private const string FullConnectionString = "Data Source=tracon-test.db;Password=fake-local-pw"; // SYNTHETIC-CREDENTIAL

    [Fact]
    public void Connection_string_property_does_not_expose_the_password()
    {
        using var dataSource = new SqliteDataSource(FullConnectionString);

        dataSource.ConnectionString.ShouldNotContain("fake-local-pw");
        dataSource.ConnectionString.ShouldContain("tracon-test.db");
    }

    [Fact]
    public void Created_connections_still_receive_the_full_connection_string()
    {
        using var dataSource = new SqliteDataSource(FullConnectionString);

        using var connection = dataSource.CreateConnection();

        connection.ConnectionString.ShouldContain("fake-local-pw");
    }
}
