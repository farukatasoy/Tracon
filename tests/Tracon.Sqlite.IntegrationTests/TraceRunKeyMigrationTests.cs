using System.Data.Common;
using System.Globalization;
using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// A trace header was unique on the tenant and the W3C trace id; the migration makes it unique on the tenant and the run. SQLite
/// cannot drop that constraint, so the migration rebuilds <c>traces</c> and its child <c>spans</c>.
/// </summary>
/// <remarks>
/// Every test writes under the schema of every migration before the new one, with plain SQL, runs the real
/// <see cref="MigrationRunner"/> over the rest, and reads the result both raw and through the store. The data holds one
/// case of every kind the migration has to tell apart: two headers of one run, two headers of one run that end at the same
/// moment, one header that two runs already share, rows with no run, and the same run id under a second tenant.
/// </remarks>
public sealed class TraceRunKeyMigrationTests(SqliteFixture fixture)
{
    private const string Tenant = "default";

    private const string OtherTenant = "other";

    private const string Seeded = "2026-01-01T00:00:00.0000000Z";

    private static readonly DateTimeOffset Origin = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Headers_of_one_run_merge_into_the_one_that_ended_last_and_no_span_is_lost()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix, Tenant);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        var data = await SeedAsync(context, prefix);

        (await CountAsync(context, prefix, "traces")).ShouldBe(11);
        (await CountAsync(context, prefix, "spans")).ShouldBe(16);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await CountAsync(context, prefix, "traces")).ShouldBe(8);
        (await CountAsync(context, prefix, "spans")).ShouldBe(16);

        // The header that has an end outranks the one that has none, whatever the dialect does with NULL in a descending order.
        var retried = await context.Traces.GetTraceByRunAsync(data.Retried);

        retried.ShouldNotBeNull();
        retried.TraceId.ShouldBe("11111111111111111111111111111111");
        retried.RunId.ShouldBe(data.Retried);
        retried.StartedAt.ShouldBe(Origin);
        retried.EndedAt.ShouldBe(Origin.AddSeconds(5));
        retried.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(["first-child", "first-root", "second-child", "second-root"]);

        // Two headers that end together: the later start wins, and the kept header still covers both.
        var tied = await context.Traces.GetTraceByRunAsync(data.Tied);

        tied.ShouldNotBeNull();
        tied.TraceId.ShouldBe("88888888888888888888888888888888");
        tied.StartedAt.ShouldBe(Origin.AddSeconds(180));
        tied.EndedAt.ShouldBe(Origin.AddSeconds(190));
        tied.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray().ShouldBe(["tied-a", "tied-b"]);

        // Two headers that both ended: the one that ended last stays, whichever started later; its extent covers both.
        var spread = await context.Traces.GetTraceByRunAsync(data.Spread);

        spread.ShouldNotBeNull();
        spread.TraceId.ShouldBe("cccccccccccccccccccccccccccccccc");
        spread.StartedAt.ShouldBe(Origin.AddSeconds(300));
        spread.EndedAt.ShouldBe(Origin.AddSeconds(320));
        spread.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray().ShouldBe(["spread-a", "spread-b"]);
    }

    [Fact]
    public async Task Headers_that_already_share_a_trace_id_and_headers_with_no_run_stay_as_they_were()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix, Tenant);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        var data = await SeedAsync(context, prefix);

        await context.Migrations.ApplyAsync();

        // The header is the last run's, the spans are the spans of both, and the first run still answers nothing: no row was changed.
        (await context.Traces.GetTraceByRunAsync(data.SharedFirst)).ShouldBeNull();

        var shared = await context.Traces.GetTraceByRunAsync(data.SharedLast);

        shared.ShouldNotBeNull();
        shared.TraceId.ShouldBe("33333333333333333333333333333333");
        shared.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(["r2-child", "r2-root", "r3-child", "r3-root"]);

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}traces WHERE run_id IS NULL;")).ShouldBe(2);

        // A third row with no run is accepted, and a second header for a run that has one is refused.
        await InsertTraceAsync(context, prefix, Guid.NewGuid(), Tenant, "99999999999999999999999999999999", runId: null, Origin, ended: null);
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}traces WHERE run_id IS NULL;")).ShouldBe(3);

        await Should.ThrowAsync<DbException>(async () =>
            await InsertTraceAsync(context, prefix, Guid.NewGuid(), Tenant, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", data.Plain, Origin, ended: null));
    }

    [Fact]
    public async Task The_same_run_id_under_another_tenant_is_not_merged()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix, Tenant);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        var data = await SeedAsync(context, prefix);

        await context.Migrations.ApplyAsync();

        (await context.ScalarAsync<string>(
            $"SELECT trace_id FROM {prefix}traces WHERE tenant_id = '{OtherTenant}' AND run_id = '{Id(data.Retried)}';"))
            .ShouldBe("66666666666666666666666666666666");
        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {prefix}spans WHERE trace_id IN (SELECT id FROM {prefix}traces WHERE tenant_id = '{OtherTenant}');"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task After_the_upgrade_two_runs_that_share_a_trace_id_are_written_and_read_apart()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix, Tenant);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        await SeedAsync(context, prefix);
        await context.Migrations.ApplyAsync();

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await context.Runs.StartRunAsync(TestData.Run(first));
        await context.Runs.StartRunAsync(TestData.Run(second));

        const string traceId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        await context.Traces.WriteSpansAsync(Batch(first, traceId, "after-first"));
        await context.Traces.WriteSpansAsync(Batch(second, traceId, "after-second"));

        (await context.Traces.GetTraceByRunAsync(first)).ShouldNotBeNull().Spans.ShouldHaveSingleItem().Name.ShouldBe("after-first");
        (await context.Traces.GetTraceByRunAsync(second)).ShouldNotBeNull().Spans.ShouldHaveSingleItem().Name.ShouldBe("after-second");
    }

    [Fact]
    public async Task The_rebuilt_tables_keep_their_foreign_keys_their_indexes_and_the_json_check()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix, Tenant);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        var data = await SeedAsync(context, prefix);

        await context.Migrations.ApplyAsync();

        (await context.ScalarAsync<long>("SELECT COUNT(*) FROM pragma_foreign_key_check;")).ShouldBe(0);

        // The child points at the table that now carries the old name, not at the one it was built against.
        (await context.ScalarAsync<string>($"SELECT \"table\" FROM pragma_foreign_key_list('{prefix}spans');")).ShouldBe($"{prefix}traces");
        (await context.ScalarAsync<string>($"SELECT on_delete FROM pragma_foreign_key_list('{prefix}spans');")).ShouldBe("CASCADE");
        (await context.ScalarAsync<string>(
            $"SELECT on_delete FROM pragma_foreign_key_list('{prefix}traces') WHERE \"table\" = '{prefix}runs';")).ShouldBe("CASCADE");

        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name IN ('{prefix}traces_run_idx', '{prefix}spans_trace_started_idx');"))
            .ShouldBe(2);
        (await context.ScalarAsync<string>($"SELECT sql FROM sqlite_master WHERE name = '{prefix}spans';")).ShouldNotBeNull().ShouldContain("json_valid");
        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE name IN ('{prefix}traces_new', '{prefix}spans_new');"))
            .ShouldBe(0);

        // Deleting a run still takes its trace and the spans of the trace with it, so both foreign keys work on the new tables.
        var spansBefore = await CountAsync(context, prefix, "spans");

        await context.ExecuteAsync($"DELETE FROM {prefix}runs WHERE id = '{Id(data.Plain)}';");

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}traces WHERE run_id = '{Id(data.Plain)}';")).ShouldBe(0);
        (await CountAsync(context, prefix, "spans")).ShouldBe(spansBefore - 1);
    }

    [Fact]
    public async Task The_migration_leaves_nothing_behind_when_it_is_applied_a_second_time_and_after_a_run_that_stopped_halfway()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix, Tenant);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        await SeedAsync(context, prefix);

        // What a run that stopped after it built the new tables would leave (both new tables, whichever step it stopped at).
        await context.ExecuteAsync($"CREATE TABLE {prefix}traces_new (id TEXT);");
        await context.ExecuteAsync($"CREATE TABLE {prefix}spans_new (id TEXT);");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var headers = await CountAsync(context, prefix, "traces");
        var spans = await CountAsync(context, prefix, "spans");

        var migration = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.")
            .Single(migration => migration.Id == MigrationId);

        await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));

        (await CountAsync(context, prefix, "traces")).ShouldBe(headers);
        (await CountAsync(context, prefix, "spans")).ShouldBe(spans);
        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE name IN ('{prefix}traces_new', '{prefix}spans_new');"))
            .ShouldBe(0);
        (await context.ScalarAsync<long>("SELECT COUNT(*) FROM pragma_foreign_key_check;")).ShouldBe(0);
    }

    [Fact]
    public async Task The_migration_passes_with_the_read_views_in_place()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix, Tenant, enableReadViews: true);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        await ApplyViewsThroughAsync(context, prefix);
        await SeedAsync(context, prefix);

        // SQLite parses every view of the file again when a table is renamed; the views the package ships read runs, not traces.
        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await CountAsync(context, prefix, "traces")).ShouldBe(8);
        (await CountAsync(context, prefix, "spans")).ShouldBe(16);
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'view' AND name = '{prefix}runs_v1';")).ShouldBe(1);
    }

    [Fact]
    public async Task A_view_of_the_host_over_traces_stops_the_migration_and_leaves_the_tables_as_they_were()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix, Tenant);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        await SeedAsync(context, prefix);

        // The host reads the table the migration drops and builds again. SQLite parses every view of the file when a table is renamed
        // and refuses while a view names a table that is not there.
        await context.ExecuteAsync($"CREATE VIEW {prefix}host_traces AS SELECT id, run_id FROM {prefix}traces;");

        var failure = await Should.ThrowAsync<TraconException>(async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain($"error in view {prefix}host_traces");

        // Nothing changed: both tables, their rows, their key and the ledger are as they were.
        (await CountAsync(context, prefix, "traces")).ShouldBe(11);
        (await CountAsync(context, prefix, "spans")).ShouldBe(16);
        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE name IN ('{prefix}traces_new', '{prefix}spans_new');"))
            .ShouldBe(0);
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}__migrations WHERE set_name = 'core' AND id = {MigrationId};")).ShouldBe(0);
    }

    private static int MigrationId => MigrationDescriptor
        .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.")
        .Single(static migration => migration.Name.Contains("trace_run_key", StringComparison.Ordinal))
        .Id;

    /// <summary>An identifier in the shape a non-nullable Guid parameter takes.</summary>
    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

    private static string Stamp(DateTimeOffset moment) => moment.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

    private static async Task<long> CountAsync(SqliteTestContext context, string prefix, string table)
        => await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}{table};");

    private static TraceSpanBatch Batch(Guid runId, string traceId, string name)
        => new()
        {
            TraceId = traceId,
            TenantId = Tenant,
            RunId = runId,
            Spans =
            [
                new TraceSpan
                {
                    Id = TraconId.NewId(),
                    SpanId = Guid.NewGuid().ToString("N")[..16],
                    Name = name,
                    StartedAt = Origin,
                    EndedAt = Origin.AddSeconds(1),
                },
            ],
        };

    /// <summary>
    /// Writes the rows the schema before the migration allows: it is unique on the tenant and the trace id, so a run can have two
    /// headers and two runs can have one.
    /// </summary>
    private static async Task<Data> SeedAsync(SqliteTestContext context, string prefix)
    {
        var data = new Data(
            Retried: Guid.NewGuid(),
            Tied: Guid.NewGuid(),
            Spread: Guid.NewGuid(),
            SharedFirst: Guid.NewGuid(),
            SharedLast: Guid.NewGuid(),
            Plain: Guid.NewGuid());

        foreach (var runId in new[] { data.Retried, data.Tied, data.Spread, data.SharedFirst, data.SharedLast, data.Plain })
        {
            await context.Runs.StartRunAsync(TestData.Run(runId));
        }

        // A run that was written under two trace ids; the first header has an end and the second has none.
        var retriedFirst = Guid.NewGuid();
        var retriedSecond = Guid.NewGuid();
        await InsertTraceAsync(context, prefix, retriedFirst, Tenant, "11111111111111111111111111111111", data.Retried, Origin, Origin.AddSeconds(5));
        await InsertTraceAsync(context, prefix, retriedSecond, Tenant, "22222222222222222222222222222222", data.Retried, Origin.AddSeconds(10), ended: null);
        await InsertSpanAsync(context, prefix, retriedFirst, "first-root", Origin);
        await InsertSpanAsync(context, prefix, retriedFirst, "first-child", Origin.AddSeconds(1));
        await InsertSpanAsync(context, prefix, retriedSecond, "second-root", Origin.AddSeconds(10));
        await InsertSpanAsync(context, prefix, retriedSecond, "second-child", Origin.AddSeconds(11));

        // Two runs that already share one header: the header carries the last writer's run.
        var shared = Guid.NewGuid();
        await InsertTraceAsync(context, prefix, shared, Tenant, "33333333333333333333333333333333", data.SharedLast, Origin.AddSeconds(60), Origin.AddSeconds(69));
        await InsertSpanAsync(context, prefix, shared, "r2-root", Origin.AddSeconds(60));
        await InsertSpanAsync(context, prefix, shared, "r2-child", Origin.AddSeconds(61));
        await InsertSpanAsync(context, prefix, shared, "r3-root", Origin.AddSeconds(65));
        await InsertSpanAsync(context, prefix, shared, "r3-child", Origin.AddSeconds(66));

        // Rows written with no run.
        var orphanA = Guid.NewGuid();
        var orphanB = Guid.NewGuid();
        await InsertTraceAsync(context, prefix, orphanA, Tenant, "44444444444444444444444444444444", runId: null, Origin, Origin.AddSeconds(1));
        await InsertTraceAsync(context, prefix, orphanB, Tenant, "55555555555555555555555555555555", runId: null, Origin, Origin.AddSeconds(1));
        await InsertSpanAsync(context, prefix, orphanA, "orphan-a", Origin);
        await InsertSpanAsync(context, prefix, orphanB, "orphan-b", Origin);

        // A run with one header, and a header of another tenant that names the first run's id.
        var plain = Guid.NewGuid();
        await InsertTraceAsync(context, prefix, plain, Tenant, "77777777777777777777777777777777", data.Plain, Origin.AddSeconds(120), Origin.AddSeconds(121));
        await InsertSpanAsync(context, prefix, plain, "plain", Origin.AddSeconds(120));

        var other = Guid.NewGuid();
        await InsertTraceAsync(context, prefix, other, OtherTenant, "66666666666666666666666666666666", data.Retried, Origin.AddSeconds(30), Origin.AddSeconds(33));
        await InsertSpanAsync(context, prefix, other, "other-tenant", Origin.AddSeconds(30));

        // Two headers of one run that end at the same moment: the one that started later is kept.
        // Fixed identifiers, chosen so that the order of the identifiers disagrees with the order of the starts: a rule that fell
        // through to the identifier would keep the first header, and only the start of the second one makes it the one that stays.
        var tiedFirst = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var tiedSecond = Guid.Parse("00000000-0000-0000-0000-000000000001");
        await InsertTraceAsync(context, prefix, tiedFirst, Tenant, "77777777777777777777777777777778", data.Tied, Origin.AddSeconds(180), Origin.AddSeconds(190));
        await InsertTraceAsync(context, prefix, tiedSecond, Tenant, "88888888888888888888888888888888", data.Tied, Origin.AddSeconds(185), Origin.AddSeconds(190));
        await InsertSpanAsync(context, prefix, tiedFirst, "tied-a", Origin.AddSeconds(180));
        await InsertSpanAsync(context, prefix, tiedSecond, "tied-b", Origin.AddSeconds(185));

        // Two headers of one run that both ended: the one that ended last stays, though the other one started later.
        var spreadFirst = Guid.NewGuid();
        var spreadSecond = Guid.NewGuid();
        await InsertTraceAsync(context, prefix, spreadFirst, Tenant, "cccccccccccccccccccccccccccccccc", data.Spread, Origin.AddSeconds(300), Origin.AddSeconds(320));
        await InsertTraceAsync(context, prefix, spreadSecond, Tenant, "dddddddddddddddddddddddddddddddd", data.Spread, Origin.AddSeconds(310), Origin.AddSeconds(315));
        await InsertSpanAsync(context, prefix, spreadFirst, "spread-a", Origin.AddSeconds(300));
        await InsertSpanAsync(context, prefix, spreadSecond, "spread-b", Origin.AddSeconds(310));

        return data;
    }

    private static async Task InsertTraceAsync(
        SqliteTestContext context,
        string prefix,
        Guid id,
        string tenant,
        string traceId,
        Guid? runId,
        DateTimeOffset started,
        DateTimeOffset? ended)
        => await context.ExecuteAsync(
            $"INSERT INTO {prefix}traces (id, tenant_id, trace_id, run_id, started_at, ended_at) " +
            $"VALUES ('{Id(id)}', '{tenant}', '{traceId}', {(runId is { } run ? $"'{Id(run)}'" : "NULL")}, " +
            $"'{Stamp(started)}', {(ended is { } end ? $"'{Stamp(end)}'" : "NULL")});");

    private static async Task InsertSpanAsync(SqliteTestContext context, string prefix, Guid headerId, string name, DateTimeOffset startedAt)
        => await context.ExecuteAsync(
            $"INSERT INTO {prefix}spans (id, trace_id, parent_span_id, name, kind, started_at, ended_at, attributes, status, span_id) " +
            $"VALUES ('{Id(Guid.NewGuid())}', '{Id(headerId)}', NULL, '{name}', 0, '{Stamp(startedAt)}', '{Stamp(startedAt.AddSeconds(1))}', " +
            $"'{{\"k\":\"v\"}}', 1, '{Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16]}');");

    private static async Task ApplyThroughAsync(SqliteTestContext context, string prefix, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', '{Seeded}');");
        }
    }

    private static async Task ApplyViewsThroughAsync(SqliteTestContext context, string prefix)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.MigrationsViews.")
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty();

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('views', {migration.Id}, '{migration.Name}', '{migration.Checksum}', '{Seeded}');");
        }
    }

    private sealed record Data(Guid Retried, Guid Tied, Guid Spread, Guid SharedFirst, Guid SharedLast, Guid Plain);
}
