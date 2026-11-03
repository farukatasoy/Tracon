using Npgsql;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Pins which PostgreSQL errors the dialect calls "the store rejected the value", against the real server.
/// </summary>
/// <remarks>
/// PostgreSQL keeps text in <c>text</c> columns that take any length, so only the two bounded columns (a
/// <c>varchar(128)</c> and a <c>char(64)</c>) and the <c>numeric</c> ones can refuse a value. The negatives matter as much
/// as the positives: a deadlock, a missing table and a duplicate key are the server answering too, and a class that took
/// them for a refused value would turn a store that is down into a <c>400</c>.
/// </remarks>
public sealed class PostgreSqlDataFaultTests(PostgresFixture fixture)
{
    private static readonly PostgresDialect Dialect = new("tracon");

    private static readonly SqlStoreDataFaultClassifier Classifier = new(Dialect);

    [Fact]
    public async Task A_text_over_a_bounded_column_is_a_text_too_long_fault()
    {
        // The two bounded columns of the schema (a varchar(128) and a char(64)) are guarded before the statement is sent
        // (a score's evaluator version is checked by RunScoreRules; the answer hash is computed), so the refusal is
        // provoked on a table of the same column types.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var table = "fault_text_" + Guid.NewGuid().ToString("N");

        await ExecuteAsync(connection, $"CREATE TABLE {table} (v varchar(5) NOT NULL, h char(3) NOT NULL);");

        try
        {
            var varchar = await CaptureAsync(connection, $"INSERT INTO {table} (v, h) VALUES ('abcdef', 'abc');");
            var character = await CaptureAsync(connection, $"INSERT INTO {table} (v, h) VALUES ('abc', 'abcd');");

            foreach (var error in new[] { varchar, character })
            {
                error.SqlState.ShouldBe("22001");
                Dialect.IsTextTooLong(error).ShouldBeTrue();
                Dialect.IsNumberOutOfRange(error).ShouldBeFalse();
                Classify(error).ShouldBe(StoreDataFault.TextTooLong);
            }
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP TABLE IF EXISTS {table};");
        }
    }

    [Fact]
    public async Task The_limit_counts_characters_and_not_utf16_units()
    {
        // Five emoji are ten UTF-16 code units and five characters: varchar(5) takes them, six are refused. A length
        // check in .NET units would turn the first away, which is why the refusal is read from the server and not
        // predicted.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var table = "fault_chars_" + Guid.NewGuid().ToString("N");

        await ExecuteAsync(connection, $"CREATE TABLE {table} (v varchar(5) NOT NULL);");

        try
        {
            await ExecuteAsync(connection, $"INSERT INTO {table} (v) VALUES ('{string.Concat(Enumerable.Repeat("\U0001F600", 5))}');");

            var error = await CaptureAsync(connection, $"INSERT INTO {table} (v) VALUES ('{string.Concat(Enumerable.Repeat("\U0001F600", 6))}');");

            Dialect.IsTextTooLong(error).ShouldBeTrue();
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP TABLE IF EXISTS {table};");
        }
    }

    [Fact]
    public async Task A_number_over_its_column_is_a_number_out_of_range_fault()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var runId = TraconId.NewId();

        await context.Runs.StartRunAsync(
            new RunStartInfo
            {
                RunId = runId,
                AgentName = "agent",
                StartedAt = DateTimeOffset.UtcNow,
                TenantId = context.TenantContext.TenantId,
            },
            TestContext.Current.CancellationToken);

        // numeric(20,10) keeps ten digits before the point.
        var error = await Should.ThrowAsync<PostgresException>(async () =>
            await context.Runs.UpdateRunCostAsync(
                runId,
                new RunCost { InputCost = 12345678901m, OutputCost = 0m, Currency = "USD", Source = PricingSource.Catalog },
                context.TenantContext.TenantId,
                TestContext.Current.CancellationToken));

        error.SqlState.ShouldBe("22003");
        Dialect.IsNumberOutOfRange(error).ShouldBeTrue();
        Dialect.IsTextTooLong(error).ShouldBeFalse();
        Classify(error).ShouldBe(StoreDataFault.NumberOutOfRange);
    }

    [Fact]
    public async Task Other_server_answers_are_not_data_faults()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var parent = "fault_parent_" + Guid.NewGuid().ToString("N");
        var child = "fault_child_" + Guid.NewGuid().ToString("N");

        await ExecuteAsync(connection, $"CREATE TABLE {parent} (id int PRIMARY KEY, n int CHECK (n > 0));");
        await ExecuteAsync(connection, $"CREATE TABLE {child} (parent_id int NOT NULL REFERENCES {parent} (id));");

        try
        {
            await ExecuteAsync(connection, $"INSERT INTO {parent} (id, n) VALUES (1, 1);");

            var errors = new[]
            {
                (Code: "23505", Error: await CaptureAsync(connection, $"INSERT INTO {parent} (id, n) VALUES (1, 1);")),
                (Code: "23503", Error: await CaptureAsync(connection, $"INSERT INTO {child} (parent_id) VALUES (99);")),
                (Code: "23514", Error: await CaptureAsync(connection, $"INSERT INTO {parent} (id, n) VALUES (2, -1);")),
                (Code: "42P01", Error: await CaptureAsync(connection, "SELECT 1 FROM no_such_table_" + Guid.NewGuid().ToString("N") + ";")),
                (Code: "22P02", Error: await CaptureAsync(connection, "SELECT 'not a number'::int;")),
            };

            foreach (var (code, error) in errors)
            {
                error.SqlState.ShouldBe(code);
                Dialect.IsTextTooLong(error).ShouldBeFalse(code);
                Dialect.IsNumberOutOfRange(error).ShouldBeFalse(code);
                Classifier.TryClassify(error, out _).ShouldBeFalse(code);
            }
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP TABLE IF EXISTS {child}; DROP TABLE IF EXISTS {parent};");
        }
    }

    [Fact]
    public void An_exception_that_is_not_the_servers_answer_is_not_a_data_fault()
    {
        Exception[] exceptions =
        [
            new OperationCanceledException(),
            new TaskCanceledException(),
            new TimeoutException("Timeout during reading attempt"),
            new InvalidOperationException("The connection pool has been exhausted"),
            new InvalidOperationException("value too long for type character varying(128)"),
            new IOException("Unable to read data from the transport connection."),
        ];

        foreach (var exception in exceptions)
        {
            Classifier.TryClassify(exception, out _).ShouldBeFalse(exception.GetType().Name);
        }
    }

    private static StoreDataFault Classify(Exception exception)
    {
        Classifier.TryClassify(exception, out var fault).ShouldBeTrue();

        return fault;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<PostgresException> CaptureAsync(NpgsqlConnection connection, string sql)
    {
        try
        {
            await ExecuteAsync(connection, sql);
        }
        catch (PostgresException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The statement was expected to fail: " + sql);
    }
}
