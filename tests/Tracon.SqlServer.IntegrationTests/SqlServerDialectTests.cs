using System.Text.Json;
using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;
using Microsoft.Data.SqlClient;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// Tests for type and behavior differences specific to SQL Server.
/// </summary>
/// <remarks>
/// The contract tests preserve behavioral <em>equality</em>. The tests here,
/// on the other hand, close pitfalls that exist only on the SQL Server side;
/// PostgreSQL has no counterpart to them.
/// </remarks>
public sealed class SqlServerDialectTests(SqlServerFixture fixture)
{
    /// <summary>
    /// 🚨 If a decimal parameter is sent without precision, SQL Server assumes
    /// <c>decimal(18,0)</c> and SILENTLY drops the fractional part. Cost
    /// amounts would round to whole numbers.
    /// </summary>
    [Fact]
    public async Task Cost_decimal_round_trips_without_truncation()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        var runId = TraconId.NewId();
        await context.Runs.StartRunAsync(TestData.Run(runId));

        var cost = new RunCost
        {
            InputCost = 0.0000123456m,
            OutputCost = 12345.6789012345m,
            Currency = "USD",
            Source = PricingSource.Catalog,
        };

        await context.Runs.UpdateRunCostAsync(runId, cost);

        var run = await context.Runs.GetRunAsync(runId);

        run.ShouldNotBeNull();
        run.Cost.ShouldNotBeNull();
        run.Cost.InputCost.ShouldBe(0.0000123456m);
        run.Cost.OutputCost.ShouldBe(12345.6789012345m);
    }

    /// <summary>
    /// Timestamps are written as UTC and read back as UTC. <c>datetimeoffset</c>
    /// preserves the stored offset; since the write side always converts to
    /// UTC, the offset read back must be zero.
    /// </summary>
    [Fact]
    public async Task Timestamp_round_trips_as_UTC()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // Deliberately written with an offset that is NOT UTC.
        var startedAt = new DateTimeOffset(2026, 3, 15, 12, 30, 45, TimeSpan.FromHours(3));
        var runId = TraconId.NewId();

        await context.Runs.StartRunAsync(TestData.Run(runId) with { StartedAt = startedAt });

        var run = await context.Runs.GetRunAsync(runId);

        run.ShouldNotBeNull();
        run.StartedAt.Offset.ShouldBe(TimeSpan.Zero);
        run.StartedAt.ToUniversalTime().ShouldBe(startedAt.ToUniversalTime());
    }

    /// <summary>
    /// Polymorphic JSON round-trips unchanged. K-027's key-ordering issue does
    /// not exist on SQL Server; this test proves that this is really the case.
    /// </summary>
    [Fact]
    public async Task Polymorphic_JSON_round_trips_unchanged()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // A nested payload carrying the `$type` discriminator. If this were
        // jsonb, key order would be lost and reading would throw JsonException.
        const string state = """
            {"$type":"tracon.test","b":1,"aaaaaaaaaaaa":{"$type":"inner","z":"last","a":"first"}}
            """;

        var record = new SessionRecord
        {
            Id = "session-json",
            AgentName = "test-agent",
            State = JsonDocument.Parse(state).RootElement,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await context.Sessions.SaveAsync(record);

        var loaded = await context.Sessions.GetAsync("session-json");

        loaded.ShouldNotBeNull();

        var raw = loaded.State.GetRawText();

        // The first property must still be `$type`.
        raw.TrimStart().ShouldStartWith("{\"$type\"");
        loaded.State.GetProperty("aaaaaaaaaaaa").GetProperty("$type").GetString().ShouldBe("inner");
    }

    /// <summary>
    /// 🚨 SQL Server's <c>uniqueidentifier</c> ordering is NOT byte-order
    /// based; uuid v7 identifiers do not appear time-ordered in a clustered
    /// key. So in high-write tables the primary key is NONCLUSTERED, and the
    /// clustered index is built on the time column instead (K-185).
    /// </summary>
    [Fact]
    public async Task High_write_tables_primary_key_is_not_clustered()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        foreach (var table in new[] { "runs", "tool_invocations", "spans", "audit_log", "attachments" })
        {
            var clusteredOnPrimaryKey = await context.ScalarAsync<int>($"""
                SELECT COUNT(*)
                FROM sys.indexes i
                JOIN sys.tables t ON t.object_id = i.object_id
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = '{context.SchemaName}'
                  AND t.name = '{table}'
                  AND i.is_primary_key = 1
                  AND i.type_desc = 'CLUSTERED';
                """);

            clusteredOnPrimaryKey.ShouldBe(0, $"'{table}''s primary key must not be clustered.");
        }
    }

    /// <summary>
    /// Runs are read back sorted by time. The clustered index is on
    /// <c>(started_at, id)</c>; the ordering must not depend on the uuid's
    /// byte order.
    /// </summary>
    [Fact]
    public async Task Runs_are_returned_sorted_by_start_time()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        var start = DateTimeOffset.UtcNow.AddMinutes(-10);
        var expected = new List<Guid>();

        for (var index = 0; index < 10; index++)
        {
            var runId = TraconId.NewId();
            expected.Add(runId);

            await context.Runs.StartRunAsync(
                TestData.Run(runId) with { StartedAt = start.AddSeconds(index) });
        }

        var page = await context.Runs.QueryRunsAsync(new RunQuery { Take = 50 });

        // The query sorts newest to oldest.
        expected.Reverse();
        page.Select(static run => run.Id).ShouldBe(expected);
    }

    /// <summary>
    /// Array columns are stored as JSON and searched with an exact match; an
    /// appended suffix ('run.completed.v2') must not match by mistake.
    /// </summary>
    [Fact]
    public async Task Event_array_is_searched_with_exact_match()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        await context.Webhooks.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = "default",
            Name = "exact-match",
            Url = "https://example.test/hook",
            Events = ["run.completed.v2"],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var matches = await context.Webhooks.FindForEventAsync("default", "run.completed");

        matches.ShouldBeEmpty();

        var exact = await context.Webhooks.FindForEventAsync("default", "run.completed.v2");

        exact.Count.ShouldBe(1);
        exact[0].Events.ShouldBe(["run.completed.v2"]);
    }

    /// <summary>
    /// 🚨 A deadlock victim is TRANSIENT, not a schema error. Concurrent
    /// first-time migrations of DIFFERENT schemas (the migration lock is scoped
    /// to the schema, K-389) touch catalog objects shared by the whole database
    /// and SQL Server kills one of them with error 1205. Before this was
    /// classified, <c>MigrationRunner</c> wrapped it in an
    /// <c>TraconException</c> and the fixture never came up: measured on
    /// 2026-08-21, five <c>SqlServerRetentionStoreContractTests</c> cases failed
    /// in a batch run with "Migration '0017_approval_conditions' could not be
    /// applied: ... deadlocked ... (error 1205, state 51)".
    /// </summary>
    [Fact]
    public async Task Deadlock_victim_is_classified_as_a_deadlock()
    {
        var table = "deadlock_probe_" + Guid.NewGuid().ToString("N");

        await using var setup = new SqlConnection(fixture.ConnectionString);
        await setup.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(setup, null, $"CREATE TABLE dbo.[{table}] (id INT PRIMARY KEY, v INT NOT NULL);");
        await ExecuteAsync(setup, null, $"INSERT INTO dbo.[{table}] (id, v) VALUES (1, 0), (2, 0);");

        try
        {
            await using var first = new SqlConnection(fixture.ConnectionString);
            await using var second = new SqlConnection(fixture.ConnectionString);
            await first.OpenAsync(TestContext.Current.CancellationToken);
            await second.OpenAsync(TestContext.Current.CancellationToken);

            var firstTx = (SqlTransaction)await first.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var secondTx = (SqlTransaction)await second.BeginTransactionAsync(TestContext.Current.CancellationToken);

            // Each transaction takes ONE row's exclusive lock ...
            await ExecuteAsync(first, firstTx, $"UPDATE dbo.[{table}] SET v = 1 WHERE id = 1;");
            await ExecuteAsync(second, secondTx, $"UPDATE dbo.[{table}] SET v = 1 WHERE id = 2;");

            // ... then reaches for the OTHER one. The cycle is closed and the
            // server must kill exactly one of the two.
            var crossFirst = CaptureAsync(first, firstTx, $"UPDATE dbo.[{table}] SET v = 2 WHERE id = 2;");
            var crossSecond = CaptureAsync(second, secondTx, $"UPDATE dbo.[{table}] SET v = 2 WHERE id = 1;");

            var outcomes = await Task.WhenAll(crossFirst, crossSecond);

            var victim = outcomes.SingleOrDefault(static error => error is not null);

            victim.ShouldNotBeNull();
            victim.Number.ShouldBe(1205);

            new SqlServerDialect("dbo").IsDeadlock(victim).ShouldBeTrue();

            await RollbackAsync(firstTx);
            await RollbackAsync(secondTx);
        }
        finally
        {
            await ExecuteAsync(setup, null, $"DROP TABLE IF EXISTS dbo.[{table}];");
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Runs the statement and RETURNS the deadlock instead of throwing it.</summary>
    private static async Task<SqlException?> CaptureAsync(
        SqlConnection connection, SqlTransaction transaction, string sql)
    {
        try
        {
            await ExecuteAsync(connection, transaction, sql);

            return null;
        }
        catch (SqlException exception)
        {
            return exception;
        }
    }

    /// <summary>The victim is already rolled back by the server; that must not throw here.</summary>
    private static async Task RollbackAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
