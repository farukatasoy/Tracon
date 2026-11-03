using Microsoft.Data.Sqlite;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// SQLite never refuses a value for its size, and none of its constraint errors is read as if it did.
/// </summary>
/// <remarks>
/// Every SQLite column is an untyped <c>TEXT</c> without a length, so the dialect's two answers are constant
/// <see langword="false"/>. The cases below keep that true against the real engine: the longest text and the largest number
/// are stored, and the errors the engine DOES raise (a duplicate key, a failed check, a missing table) are not data faults.
/// </remarks>
public sealed class SqliteDataFaultTests
{
    private static readonly SqliteDialect Dialect = new("tracon_");

    private static readonly SqlStoreDataFaultClassifier Classifier = new(Dialect);

    [Fact]
    public async Task A_very_long_text_and_a_very_large_number_are_stored()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "CREATE TABLE probe (v TEXT NOT NULL, n NUMERIC NOT NULL);");

        await ExecuteAsync(connection, $"INSERT INTO probe (v, n) VALUES ('{new string('a', 1_000_000)}', 1e300);");

        // Stored whole, not cut: the engine's own answer, read back.
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT length(v), n FROM probe;";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        reader.GetInt64(0).ShouldBe(1_000_000);
        reader.GetDouble(1).ShouldBe(1e300);
    }

    [Fact]
    public async Task The_errors_the_engine_does_raise_are_not_data_faults()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "CREATE TABLE probe (id INTEGER PRIMARY KEY, n INTEGER NOT NULL CHECK (n > 0));");
        await ExecuteAsync(connection, "INSERT INTO probe (id, n) VALUES (1, 1);");

        var errors = new[]
        {
            await CaptureAsync(connection, "INSERT INTO probe (id, n) VALUES (1, 1);"),
            await CaptureAsync(connection, "INSERT INTO probe (id, n) VALUES (2, -1);"),
            await CaptureAsync(connection, "INSERT INTO probe (id, n) VALUES (3, NULL);"),
            await CaptureAsync(connection, "SELECT 1 FROM no_such_table;"),
        };

        foreach (var error in errors)
        {
            Dialect.IsTextTooLong(error).ShouldBeFalse(error.Message);
            Dialect.IsNumberOutOfRange(error).ShouldBeFalse(error.Message);
            Classifier.TryClassify(error, out _).ShouldBeFalse(error.Message);
        }
    }

    [Fact]
    public void An_exception_that_is_not_the_engines_answer_is_not_a_data_fault()
    {
        Exception[] exceptions =
        [
            new OperationCanceledException(),
            new TaskCanceledException(),
            new TimeoutException(),
            new InvalidOperationException("string or binary data would be truncated"),
            new IOException("disk I/O error"),
        ];

        foreach (var exception in exceptions)
        {
            Classifier.TryClassify(exception, out _).ShouldBeFalse(exception.GetType().Name);
        }
    }

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<SqliteException> CaptureAsync(SqliteConnection connection, string sql)
    {
        try
        {
            await ExecuteAsync(connection, sql);
        }
        catch (SqliteException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The statement was expected to fail: " + sql);
    }
}
