using System.Data.Common;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The recorder that the run deadlock probe relies on sees a real victim, and only a victim of its own schema.
/// </summary>
/// <remarks>
/// A probe that expects no victims passes when its recorder is blind, so this proves the recorder with the one thing it must
/// find: two transactions that each wait for a row the other holds. The victim is a write in one test and a read in the other,
/// because the runs that the probes race are lost to reads as well as to writes.
/// </remarks>
public sealed class DeadlockVictimRecorderTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    [Fact]
    public async Task A_command_the_server_ends_as_a_deadlock_victim_is_counted()
    {
        var table = await CreateTableAsync();
        using var recorder = DeadlockVictimRecorder.Start(schema.Context.SchemaName);

        await CloseDeadlockCycleAsync(table);

        recorder.Victims.Count.ShouldBe(1, "exactly one of the two transactions is the victim");
        recorder.Victims.Single().ShouldStartWith("UPDATE " + table);
    }

    [Fact]
    public async Task A_read_the_server_ends_as_a_deadlock_victim_is_counted()
    {
        var table = await CreateTableAsync();
        using var recorder = DeadlockVictimRecorder.Start(schema.Context.SchemaName);

        await CloseDeadlockCycleAsync(table, closeWithReads: true);

        recorder.Victims.Count.ShouldBe(1, "exactly one of the two transactions is the victim");
        recorder.Victims.Single().ShouldStartWith("SELECT v FROM " + table);
    }

    [Fact]
    public async Task A_victim_of_another_schema_is_not_counted()
    {
        var table = await CreateTableAsync();
        using var recorder = DeadlockVictimRecorder.Start("another_schema_name");

        await CloseDeadlockCycleAsync(table);

        recorder.Victims.ShouldBeEmpty();
    }

    private async Task<string> CreateTableAsync()
    {
        var table = $"{schema.Context.SchemaName}.cycle_{Guid.NewGuid():N}";

        await using var connection = await schema.Context.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(
            connection,
            null,
            $"CREATE TABLE {table} (k int NOT NULL PRIMARY KEY, v int NOT NULL); INSERT INTO {table} (k, v) VALUES (1, 0), (2, 0);");

        return table;
    }

    /// <summary>
    /// The first transaction holds row 1 and asks for row 2, the second holds row 2 and asks for row 1. Whichever request
    /// reaches the server first waits, the other one closes the cycle, so the test does not have to order them. The requests
    /// that close the cycle are reads when <paramref name="closeWithReads"/> is set.
    /// </summary>
    private async Task CloseDeadlockCycleAsync(string table, bool closeWithReads = false)
    {
        await using var first = await schema.Context.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var second = await schema.Context.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var firstTransaction = await first.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var secondTransaction = await second.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(first, firstTransaction, $"UPDATE {table} SET v = 1 WHERE k = 1;");
        await ExecuteAsync(second, secondTransaction, $"UPDATE {table} SET v = 1 WHERE k = 2;");

        await Task.WhenAll(
            AttemptAsync(first, firstTransaction, closeWithReads ? $"SELECT v FROM {table} WHERE k = 2;" : $"UPDATE {table} SET v = 2 WHERE k = 2;"),
            AttemptAsync(second, secondTransaction, closeWithReads ? $"SELECT v FROM {table} WHERE k = 1;" : $"UPDATE {table} SET v = 2 WHERE k = 1;"));
    }

    private static async Task AttemptAsync(DbConnection connection, DbTransaction transaction, string sql)
    {
        try
        {
            await ExecuteAsync(connection, transaction, sql);
        }
        catch (DbException)
        {
            // The victim's statement fails; the other one finishes once the victim's locks are gone.
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        if (sql.StartsWith("SELECT", StringComparison.Ordinal))
        {
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
