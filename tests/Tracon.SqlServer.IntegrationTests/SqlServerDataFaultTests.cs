using Microsoft.Data.SqlClient;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// Pins which SQL Server errors the dialect calls "the store rejected the value", against the real server.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SqlException"/> cannot be built by hand, so every case provokes the real error. The numbers are not
/// something a reader can be trusted to remember: 2628 and 8152 are two texts for the same refusal, chosen by the
/// database's compatibility level, and a consumer's database can be at either.
/// </para>
/// <para>
/// The negatives matter as much as the positives. A deadlock, a missing table and a duplicate key are SQL Server answering
/// too, and a class that took them for a refused value would turn a store that is down into a <c>400</c>.
/// </para>
/// </remarks>
public sealed class SqlServerDataFaultTests(SqlServerFixture fixture)
{
    private static readonly SqlServerDialect Dialect = new("dbo");

    private static readonly SqlStoreDataFaultClassifier Classifier = new(Dialect);

    [Fact]
    public async Task A_text_over_its_column_is_a_text_too_long_fault()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // A new key runs the INSERT branch of the two-statement upsert.
        var inserted = await Should.ThrowAsync<SqlException>(async () =>
            await context.Tenants.SaveAsync(Tenant("insert-branch", new string('a', 201)), TestContext.Current.CancellationToken));

        inserted.Number.ShouldBe(2628);
        Classify(inserted).ShouldBe(StoreDataFault.TextTooLong);

        // An existing key runs the UPDATE branch, which is the other statement of the same text.
        await context.Tenants.SaveAsync(Tenant("update-branch", "short"), TestContext.Current.CancellationToken);

        var updated = await Should.ThrowAsync<SqlException>(async () =>
            await context.Tenants.SaveAsync(Tenant("update-branch", new string('a', 201)), TestContext.Current.CancellationToken));

        updated.Number.ShouldBe(2628);
        Classify(updated).ShouldBe(StoreDataFault.TextTooLong);
    }

    [Fact]
    public async Task The_limit_is_the_column_length_and_not_a_byte_count()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // nvarchar(200) holds 200 UTF-16 code units: 100 emoji (two units each) fit, 101 do not. A string of 199 ASCII
        // characters and one surrogate pair is 201 units.
        var hundredEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 100));
        await context.Tenants.SaveAsync(Tenant("emoji-fits", hundredEmoji), TestContext.Current.CancellationToken);

        var emojiOver = await Should.ThrowAsync<SqlException>(async () =>
            await context.Tenants.SaveAsync(
                Tenant("emoji-over", hundredEmoji + "\U0001F600"),
                TestContext.Current.CancellationToken));
        Classify(emojiOver).ShouldBe(StoreDataFault.TextTooLong);

        var pairOver = await Should.ThrowAsync<SqlException>(async () =>
            await context.Tenants.SaveAsync(
                Tenant("pair-over", new string('a', 199) + "\U0001F600"),
                TestContext.Current.CancellationToken));
        Classify(pairOver).ShouldBe(StoreDataFault.TextTooLong);

        await context.Tenants.SaveAsync(Tenant("exact-fit", new string('a', 200)), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_refused_write_leaves_the_earlier_row_and_the_connection_pool_usable()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        await context.Tenants.SaveAsync(Tenant("kept", "first"), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<SqlException>(async () =>
            await context.Tenants.SaveAsync(Tenant("kept", new string('a', 201)), TestContext.Current.CancellationToken));

        (await context.Tenants.ListAsync(TestContext.Current.CancellationToken))
            .Single(static tenant => string.Equals(tenant.Slug, "kept", StringComparison.Ordinal)).DisplayName.ShouldBe("first");

        await context.Tenants.SaveAsync(Tenant("kept", "second"), TestContext.Current.CancellationToken);

        (await context.Tenants.ListAsync(TestContext.Current.CancellationToken))
            .Single(static tenant => string.Equals(tenant.Slug, "kept", StringComparison.Ordinal)).DisplayName.ShouldBe("second");
    }

    [Fact]
    public async Task A_number_over_its_column_is_a_number_out_of_range_fault()
    {
        await using var probe = await ProbeTable.CreateAsync(fixture, "amount decimal(20,10) NOT NULL");

        // decimal(20,10) keeps ten digits before the point.
        var error = await probe.CaptureAsync("INSERT INTO {0} (amount) VALUES (CAST('12345678901.5' AS decimal(30,10)));");

        error.Number.ShouldBe(8115);
        Dialect.IsNumberOutOfRange(error).ShouldBeTrue();
        Dialect.IsTextTooLong(error).ShouldBeFalse();
        Classify(error).ShouldBe(StoreDataFault.NumberOutOfRange);
    }

    [Fact]
    public async Task The_older_truncation_text_is_a_text_too_long_fault_too()
    {
        // 2628 names the table, the column and the value; 8152 is the text a database below compatibility level 150 gives,
        // and the one every database gives once VERBOSE_TRUNCATION_WARNINGS is off. The shared container is at the
        // current level, so both are provoked in a database made for the purpose.
        var name = "truncation_probe_" + Guid.NewGuid().ToString("N");
        await using var master = new SqlConnection(fixture.ConnectionString);
        await master.OpenAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(master, $"CREATE DATABASE [{name}];");

        try
        {
            var builder = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = name, Pooling = false };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            await ExecuteAsync(connection, "CREATE TABLE dbo.probe (v nvarchar(10) NOT NULL);");

            var current = await CaptureAsync(connection, "INSERT INTO dbo.probe (v) VALUES (N'abcdefghijk');");
            current.Number.ShouldBe(2628);

            await ExecuteAsync(connection, "ALTER DATABASE SCOPED CONFIGURATION SET VERBOSE_TRUNCATION_WARNINGS = OFF;");

            var quiet = await CaptureAsync(connection, "INSERT INTO dbo.probe (v) VALUES (N'abcdefghijk');");
            quiet.Number.ShouldBe(8152);

            foreach (var error in new[] { current, quiet })
            {
                Dialect.IsTextTooLong(error).ShouldBeTrue();
                Classify(error).ShouldBe(StoreDataFault.TextTooLong);
            }
        }
        finally
        {
            await ExecuteAsync(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
        }
    }

    [Fact]
    public async Task Other_server_answers_are_not_data_faults()
    {
        await using var probe = await ProbeTable.CreateAsync(fixture, "id int NOT NULL PRIMARY KEY, other int NULL");
        await probe.ExecuteAsync("INSERT INTO {0} (id) VALUES (1);");

        // 2627: a duplicate key.
        var duplicate = await probe.CaptureAsync("INSERT INTO {0} (id) VALUES (1);");
        duplicate.Number.ShouldBe(2627);

        // 2601: a unique index (2627 above is a unique constraint).
        await probe.ExecuteAsync("CREATE UNIQUE INDEX ux_other ON {0} (other);");
        await probe.ExecuteAsync("INSERT INTO {0} (id, other) VALUES (10, 5);");
        var uniqueIndex = await probe.CaptureAsync("INSERT INTO {0} (id, other) VALUES (11, 5);");
        uniqueIndex.Number.ShouldBe(2601);

        // 547: a foreign key.
        await using var child = await ProbeTable.CreateAsync(fixture, "parent_id int NOT NULL");
        await child.ExecuteAsync($"ALTER TABLE {{0}} ADD CONSTRAINT fk_{Guid.NewGuid():N} FOREIGN KEY (parent_id) REFERENCES {probe.Name} (id);");
        var foreignKey = await child.CaptureAsync("INSERT INTO {0} (parent_id) VALUES (99);");
        foreignKey.Number.ShouldBe(547);

        // 208: a table that does not exist, which is what a store behind on its migrations answers.
        var missing = await probe.CaptureAsync("SELECT 1 FROM dbo.[no_such_table_" + Guid.NewGuid().ToString("N") + "];");
        missing.Number.ShouldBe(208);

        // 8114: a text that cannot be converted to the column's type is a conversion error and not an overflow.
        var conversion = await probe.CaptureAsync("INSERT INTO {0} (id) VALUES (CAST('not a number' AS int));");

        foreach (var error in new[] { duplicate, uniqueIndex, foreignKey, missing, conversion })
        {
            Dialect.IsTextTooLong(error).ShouldBeFalse($"error {error.Number}");
            Dialect.IsNumberOutOfRange(error).ShouldBeFalse($"error {error.Number}");
            Classifier.TryClassify(error, out _).ShouldBeFalse($"error {error.Number}");
        }
    }

    [Fact]
    public void An_exception_that_is_not_the_servers_answer_is_not_a_data_fault()
    {
        // Cancelling, running out of time and an exhausted pool are the caller or the host giving up, not the server
        // refusing a value.
        Exception[] exceptions =
        [
            new OperationCanceledException(),
            new TaskCanceledException(),
            new TimeoutException("Execution Timeout Expired."),
            new InvalidOperationException("Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool."),
            new InvalidOperationException("string or binary data would be truncated"),
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

    private static TenantDescriptor Tenant(string slug, string displayName)
        => new() { Id = Guid.NewGuid(), Slug = slug, DisplayName = displayName };

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<SqlException> CaptureAsync(SqlConnection connection, string sql)
    {
        try
        {
            await ExecuteAsync(connection, sql);
        }
        catch (SqlException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The statement was expected to fail: " + sql);
    }

    /// <summary>A throwaway table in <c>dbo</c> that the test provokes errors against.</summary>
    private sealed class ProbeTable : IAsyncDisposable
    {
        private readonly SqlConnection _connection;

        private ProbeTable(SqlConnection connection, string name)
        {
            _connection = connection;
            Name = name;
        }

        public string Name { get; }

        public static async Task<ProbeTable> CreateAsync(SqlServerFixture fixture, string columns)
        {
            var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            var name = "dbo.[fault_probe_" + Guid.NewGuid().ToString("N") + "]";
            await SqlServerDataFaultTests.ExecuteAsync(connection, $"CREATE TABLE {name} ({columns});");

            return new ProbeTable(connection, name);
        }

        public Task ExecuteAsync(string sql) => SqlServerDataFaultTests.ExecuteAsync(_connection, Bind(sql));

        public Task<SqlException> CaptureAsync(string sql) => SqlServerDataFaultTests.CaptureAsync(_connection, Bind(sql));

        private string Bind(string sql) => sql.Replace("{0}", Name, StringComparison.Ordinal);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await SqlServerDataFaultTests.ExecuteAsync(_connection, $"DROP TABLE IF EXISTS {Name};");
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }
    }
}
