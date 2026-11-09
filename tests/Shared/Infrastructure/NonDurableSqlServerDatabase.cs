using Microsoft.Data.SqlClient;

namespace Tracon.Tests.Common;

/// <summary>
/// The database a SQL Server test container gives its tests: a database of their own, with the durability a test does
/// not need turned off.
/// </summary>
/// <remarks>
/// <para>
/// The container's own connection string opens <c>master</c>, and a system database does not take these options. The
/// tests get a database of their own, and every connection string handed to a test names it.
/// </para>
/// <para>
/// <c>DELAYED_DURABILITY = FORCED</c> lets a commit return before its log records reach the disk, and
/// <c>RECOVERY SIMPLE</c> lets a checkpoint truncate the log. A test container is removed after the run and never
/// recovers from a crash, so neither option changes what a test can see: isolation, locks and the order of commits stay
/// what they are on a production server. Only the wait for the log flush goes.
/// </para>
/// </remarks>
internal static class NonDurableSqlServerDatabase
{
    /// <summary>Creates the database on the server and returns a connection string that opens it.</summary>
    /// <param name="serverConnectionString">The container's connection string.</param>
    /// <param name="databaseName">The database's name: letters, digits and underscores.</param>
    /// <returns>The container's connection string with the new database as its initial catalog.</returns>
    public static async Task<string> CreateAsync(string serverConnectionString, string databaseName)
    {
        var server = new SqlConnectionStringBuilder(serverConnectionString) { Pooling = false };

        await using (var connection = new SqlConnection(server.ConnectionString))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE DATABASE [{databaseName}];
                ALTER DATABASE [{databaseName}] SET RECOVERY SIMPLE;
                ALTER DATABASE [{databaseName}] SET DELAYED_DURABILITY = FORCED;
                """;
            await command.ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = databaseName }.ConnectionString;
    }
}
