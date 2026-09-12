using System.Text.Json;
using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;
using Microsoft.Data.Sqlite;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// Tests for SQLite-specific type and behavior differences.
/// </summary>
/// <remarks>
/// The contract tests preserve behavioral <em>equality</em>. The tests here
/// close traps that exist only on the SQLite side; PostgreSQL/SQL Server have
/// no counterpart for them, or close them through different mechanisms.
/// </remarks>
public sealed class SqliteDialectTests(SqliteFixture fixture)
{
    /// <summary>
    /// In SQLite, <c>decimal</c> is always written as TEXT; there is NO
    /// conversion to <c>REAL</c> (double). This avoids floating-point
    /// precision loss.
    /// </summary>
    [Fact]
    public async Task Cost_decimal_round_trips_without_truncation()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

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
    /// Timestamps are written as UTC and read back as UTC.
    /// <c>SqliteDialect.AddTimestamp</c> always writes
    /// <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>; the offset read back must be zero.
    /// </summary>
    [Fact]
    public async Task Timestamp_round_trips_as_UTC()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

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
    /// Polymorphic JSON round-trips intact. SQLite stores JSON as plain text;
    /// key order is preserved, unlike PostgreSQL's <c>jsonb</c>.
    /// </summary>
    [Fact]
    public async Task Polymorphic_JSON_round_trips_intact()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

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

        raw.TrimStart().ShouldStartWith("{\"$type\"");
        loaded.State.GetProperty("aaaaaaaaaaaa").GetProperty("$type").GetString().ShouldBe("inner");
    }

    /// <summary>
    /// Array columns are carried as JSON and searched with <c>json_each</c>
    /// for an EXACT match; a suffixed value ('run.completed.v2') must not
    /// match incorrectly.
    /// </summary>
    [Fact]
    public async Task Event_array_is_searched_with_an_exact_match()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

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
    /// 🚨 <c>Microsoft.Data.Sqlite</c> writes guids in UPPERCASE. This does
    /// not break the lexical ordering of uuid v7's time-ordered prefix; but
    /// it is critical that <c>SqliteDialect</c> does NOT CONVERT it to
    /// lowercase (see the class documentation) — because required Guids are
    /// written through <c>DbHelpers.Add</c> (the driver default), while
    /// nullable Guids go through <c>Dialect.AddUuid</c>; if the two used
    /// DIFFERENT casing, the same logical id would be stored under two
    /// representations, and the JOIN/WHERE equality this test relies on
    /// (trace lookup via run_id) would fail SILENTLY.
    /// </summary>
    [Fact]
    public async Task Nullable_and_required_guid_write_paths_are_consistent()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        var runId = TraconId.NewId();
        await context.Runs.StartRunAsync(TestData.Run(runId));

        // WriteSpansAsync -> UpsertTraceAsync writes run_id via Dialect.AddUuid (nullable).
        await context.Traces.WriteSpansAsync(new TraceSpanBatch
        {
            TenantId = "default",
            TraceId = Guid.NewGuid().ToString("N"),
            RunId = runId,
            Spans =
            [
                new TraceSpan
                {
                    Id = TraconId.NewId(),
                    SpanId = Guid.NewGuid().ToString("N")[..16],
                    Name = "test-span",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        // GetTraceByRunAsync filters the same run_id via DbHelpers.Add (required).
        var trace = await context.Traces.GetTraceByRunAsync(runId);

        trace.ShouldNotBeNull();
        trace.RunId.ShouldBe(runId);
    }

    /// <summary>
    /// WAL mode and <c>busy_timeout</c> are set automatically on every new
    /// connection; the consumer does not need to specify them separately in
    /// the connection string.
    /// </summary>
    [Fact]
    public async Task WAL_and_busy_timeout_are_set_when_the_connection_opens()
    {
        await using var dataSource = new SqliteDataSource(fixture.ConnectionString);
        await using var connection = (SqliteConnection)await dataSource.OpenConnectionAsync();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode;";
            ((string)command.ExecuteScalar()!).ShouldBe("wal");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA busy_timeout;";
            ((long)command.ExecuteScalar()!).ShouldBe(5000);
        }
    }

    /// <summary>
    /// Foreign key enforcement is OFF BY DEFAULT in SQLite; it must be turned
    /// on explicitly on every connection (<c>SqliteDataSource</c>). If left
    /// off, <c>ON DELETE CASCADE</c> clauses would be silently ignored.
    /// </summary>
    [Fact]
    public async Task Foreign_key_enforcement_is_enabled()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        var exception = await Should.ThrowAsync<SqliteException>(async () =>
            await context.ExecuteAsync(
                $"""
                INSERT INTO {context.TablePrefix}tool_invocations
                    (id, run_id, tool_name, created_at)
                VALUES ('{Guid.NewGuid():D}', '{Guid.NewGuid():D}', 't', '2026-01-01T00:00:00.0000000Z');
                """));

        exception.Message.ShouldContain("FOREIGN KEY");
    }
}
