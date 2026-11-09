using System.Text;
using Microsoft.Data.Sqlite;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The secret canaries scan the bytes of a database a live host holds open. These pin the two states of that host in
/// which a plain read of its files failed on macOS with "being used by another process".
/// </summary>
/// <remarks>
/// Both set the state up with real connections and need no timing: a write transaction holds its lock until it ends,
/// and a connection's close runs to the end before the call returns.
/// </remarks>
public sealed class TempSqliteDatabaseTests
{
    [Fact]
    public void A_write_transaction_another_connection_holds_open_does_not_stop_the_read()
    {
        const string marker = "read-under-a-write-transaction-7f3e";
        using var database = new TempSqliteDatabase("read-under-write");
        using var keeper = OpenWithRow(database, marker);
        using var writer = Open(database);

        // The write lock a host takes for every write it makes; SQLite keeps it on a byte of the shared-memory file.
        Execute(writer, "BEGIN IMMEDIATE");

        var bytes = database.ReadAllBytes();

        Encoding.UTF8.GetString(bytes).ShouldContain(marker, Case.Sensitive, "the committed row is not in the bytes read");
    }

    [Fact]
    public void A_connection_that_closes_during_the_read_is_not_the_last_one_and_leaves_the_log_in_place()
    {
        const string marker = "read-across-a-close-2b9d";
        using var database = new TempSqliteDatabase("read-across-close");
        using var keeper = OpenWithRow(database, marker);
        var logAfterTheClose = false;

        var bytes = database.ReadAllBytes(whileHeld: () =>
        {
            // The host's last open connection closes, as a pool clear anywhere in the process makes it do. Were it the
            // last close, it would take an exclusive lock on the main file, fold the log in and delete it.
            keeper.Dispose();
            logAfterTheClose = File.Exists(database.Path + "-wal");
        });

        logAfterTheClose.ShouldBeTrue("the close was the last one while the files were being read");
        Encoding.UTF8.GetString(bytes).ShouldContain(marker, Case.Sensitive, "the committed row is not in the bytes read");
    }

    private static SqliteConnection OpenWithRow(TempSqliteDatabase database, string marker)
    {
        var connection = Open(database);

        Execute(connection, "PRAGMA journal_mode = WAL; CREATE TABLE marker (value TEXT NOT NULL)");

        using var insert = connection.CreateCommand();

        insert.CommandText = "INSERT INTO marker (value) VALUES ($value)";
        insert.Parameters.AddWithValue("$value", marker);
        insert.ExecuteNonQuery();

        return connection;
    }

    private static SqliteConnection Open(TempSqliteDatabase database)
    {
        var connection = new SqliteConnection(database.ConnectionString + ";Pooling=False");

        connection.Open();

        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
