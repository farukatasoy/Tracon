using System.Data.Common;
using System.Globalization;
using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// A trace header was unique on the tenant and the W3C trace id; the migration makes it unique on the tenant and the run, and
/// merges the headers that already share a run. The new key is a filtered unique index, because SQL Server counts two NULLs as
/// equal in a UNIQUE constraint and the rows written with no run have to stay.
/// </summary>
/// <remarks>
/// Every test writes under the schema of every migration before the new one, with plain SQL, runs the real
/// <see cref="MigrationRunner"/> over the rest, and reads the result both raw and through the store. The data holds one
/// case of every kind the migration has to tell apart: two headers of one run, two headers of one run that end at the same
/// moment, one header that two runs already share, rows with no run, and the same run id under a second tenant.
/// </remarks>
public sealed class TraceRunKeyMigrationTests(SqlServerFixture fixture)
{
    private const string Tenant = "default";

    private const string OtherTenant = "other";

    private static readonly DateTimeOffset Origin = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Headers_of_one_run_merge_into_the_one_that_ended_last_and_no_span_is_lost()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);
        var data = await SeedAsync(context, schemaName);

        (await CountAsync(context, schemaName, "traces")).ShouldBe(11);
        (await CountAsync(context, schemaName, "spans")).ShouldBe(16);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await CountAsync(context, schemaName, "traces")).ShouldBe(8);
        (await CountAsync(context, schemaName, "spans")).ShouldBe(16);

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
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);
        var data = await SeedAsync(context, schemaName);

        await context.Migrations.ApplyAsync();

        // The header is the last run's, the spans are the spans of both, and the first run still answers nothing: no row was changed.
        (await context.Traces.GetTraceByRunAsync(data.SharedFirst)).ShouldBeNull();

        var shared = await context.Traces.GetTraceByRunAsync(data.SharedLast);

        shared.ShouldNotBeNull();
        shared.TraceId.ShouldBe("33333333333333333333333333333333");
        shared.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(["r2-child", "r2-root", "r3-child", "r3-root"]);

        (await context.ScalarAsync<int>($"SELECT COUNT(*) FROM {schemaName}.traces WHERE run_id IS NULL;")).ShouldBe(2);

        // A third row with no run is accepted, and a second header for a run that has one is refused.
        await InsertTraceAsync(context, schemaName, Guid.NewGuid(), Tenant, "99999999999999999999999999999999", runId: null, Origin, ended: null);
        (await context.ScalarAsync<int>($"SELECT COUNT(*) FROM {schemaName}.traces WHERE run_id IS NULL;")).ShouldBe(3);

        await Should.ThrowAsync<DbException>(async () =>
            await InsertTraceAsync(context, schemaName, Guid.NewGuid(), Tenant, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", data.Plain, Origin, ended: null));
    }

    [Fact]
    public async Task The_same_run_id_under_another_tenant_is_not_merged()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);
        var data = await SeedAsync(context, schemaName);

        await context.Migrations.ApplyAsync();

        (await context.ScalarAsync<string>(
            $"SELECT trace_id FROM {schemaName}.traces WHERE tenant_id = N'{OtherTenant}' AND run_id = '{data.Retried}';"))
            .ShouldBe("66666666666666666666666666666666");
        (await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {schemaName}.spans WHERE trace_id IN (SELECT id FROM {schemaName}.traces WHERE tenant_id = N'{OtherTenant}');"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task After_the_upgrade_two_runs_that_share_a_trace_id_are_written_and_read_apart()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);
        await SeedAsync(context, schemaName);
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
    public async Task The_old_key_is_gone_the_new_one_is_unique_and_a_deleted_run_takes_only_its_own_trace()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);
        var data = await SeedAsync(context, schemaName);

        await context.Migrations.ApplyAsync();

        (await context.CatalogScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.key_constraints WHERE name = N'traces_tenant_trace_uq' AND parent_object_id = OBJECT_ID(N'{schemaName}.traces');"))
            .ShouldBe(0);

        // Filtered: a UNIQUE constraint would count two NULLs as equal.
        (await context.CatalogScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.indexes WHERE name = N'traces_tenant_run_uq' AND object_id = OBJECT_ID(N'{schemaName}.traces') AND is_unique = 1 AND has_filter = 1;"))
            .ShouldBe(1);

        // The delete cascade from runs finds a header by run_id alone, so that index is still needed.
        (await context.CatalogScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.indexes WHERE name = N'traces_run_idx' AND object_id = OBJECT_ID(N'{schemaName}.traces');"))
            .ShouldBe(1);

        var spansBefore = await CountAsync(context, schemaName, "spans");

        await context.ExecuteAsync($"DELETE FROM {schemaName}.runs WHERE id = '{data.Plain}';");

        (await context.ScalarAsync<int>($"SELECT COUNT(*) FROM {schemaName}.traces WHERE run_id = '{data.Plain}';")).ShouldBe(0);
        (await CountAsync(context, schemaName, "spans")).ShouldBe(spansBefore - 1);
    }

    [Fact]
    public async Task The_migration_changes_nothing_when_it_is_applied_a_second_time()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);
        await SeedAsync(context, schemaName);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var headers = await CountAsync(context, schemaName, "traces");
        var spans = await CountAsync(context, schemaName, "spans");

        var migration = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.")
            .Single(migration => migration.Id == MigrationId);

        await context.ExecuteRetryingDeadlockAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));

        (await CountAsync(context, schemaName, "traces")).ShouldBe(headers);
        (await CountAsync(context, schemaName, "spans")).ShouldBe(spans);
    }

    private static int MigrationId => MigrationDescriptor
        .Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.")
        .Single(static migration => migration.Name.Contains("trace_run_key", StringComparison.Ordinal))
        .Id;

    private static string Stamp(DateTimeOffset moment) => moment.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);

    private static async Task<int> CountAsync(SqlServerTestContext context, string schemaName, string table)
        => await context.ScalarAsync<int>($"SELECT COUNT(*) FROM {schemaName}.{table};");

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
    private static async Task<Data> SeedAsync(SqlServerTestContext context, string schemaName)
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
        await InsertTraceAsync(context, schemaName, retriedFirst, Tenant, "11111111111111111111111111111111", data.Retried, Origin, Origin.AddSeconds(5));
        await InsertTraceAsync(context, schemaName, retriedSecond, Tenant, "22222222222222222222222222222222", data.Retried, Origin.AddSeconds(10), ended: null);
        await InsertSpanAsync(context, schemaName, retriedFirst, "first-root", Origin);
        await InsertSpanAsync(context, schemaName, retriedFirst, "first-child", Origin.AddSeconds(1));
        await InsertSpanAsync(context, schemaName, retriedSecond, "second-root", Origin.AddSeconds(10));
        await InsertSpanAsync(context, schemaName, retriedSecond, "second-child", Origin.AddSeconds(11));

        // Two runs that already share one header: the header carries the last writer's run.
        var shared = Guid.NewGuid();
        await InsertTraceAsync(context, schemaName, shared, Tenant, "33333333333333333333333333333333", data.SharedLast, Origin.AddSeconds(60), Origin.AddSeconds(69));
        await InsertSpanAsync(context, schemaName, shared, "r2-root", Origin.AddSeconds(60));
        await InsertSpanAsync(context, schemaName, shared, "r2-child", Origin.AddSeconds(61));
        await InsertSpanAsync(context, schemaName, shared, "r3-root", Origin.AddSeconds(65));
        await InsertSpanAsync(context, schemaName, shared, "r3-child", Origin.AddSeconds(66));

        // Rows written with no run.
        var orphanA = Guid.NewGuid();
        var orphanB = Guid.NewGuid();
        await InsertTraceAsync(context, schemaName, orphanA, Tenant, "44444444444444444444444444444444", runId: null, Origin, Origin.AddSeconds(1));
        await InsertTraceAsync(context, schemaName, orphanB, Tenant, "55555555555555555555555555555555", runId: null, Origin, Origin.AddSeconds(1));
        await InsertSpanAsync(context, schemaName, orphanA, "orphan-a", Origin);
        await InsertSpanAsync(context, schemaName, orphanB, "orphan-b", Origin);

        // A run with one header, and a header of another tenant that names the first run's id.
        var plain = Guid.NewGuid();
        await InsertTraceAsync(context, schemaName, plain, Tenant, "77777777777777777777777777777777", data.Plain, Origin.AddSeconds(120), Origin.AddSeconds(121));
        await InsertSpanAsync(context, schemaName, plain, "plain", Origin.AddSeconds(120));

        var other = Guid.NewGuid();
        await InsertTraceAsync(context, schemaName, other, OtherTenant, "66666666666666666666666666666666", data.Retried, Origin.AddSeconds(30), Origin.AddSeconds(33));
        await InsertSpanAsync(context, schemaName, other, "other-tenant", Origin.AddSeconds(30));

        // Two headers of one run that end at the same moment: the one that started later is kept.
        // Fixed identifiers, chosen so that the order of the identifiers disagrees with the order of the starts: a rule that fell
        // through to the identifier would keep the first header, and only the start of the second one makes it the one that stays.
        var tiedFirst = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var tiedSecond = Guid.Parse("00000000-0000-0000-0000-000000000001");
        await InsertTraceAsync(context, schemaName, tiedFirst, Tenant, "77777777777777777777777777777778", data.Tied, Origin.AddSeconds(180), Origin.AddSeconds(190));
        await InsertTraceAsync(context, schemaName, tiedSecond, Tenant, "88888888888888888888888888888888", data.Tied, Origin.AddSeconds(185), Origin.AddSeconds(190));
        await InsertSpanAsync(context, schemaName, tiedFirst, "tied-a", Origin.AddSeconds(180));
        await InsertSpanAsync(context, schemaName, tiedSecond, "tied-b", Origin.AddSeconds(185));

        // Two headers of one run that both ended: the one that ended last stays, though the other one started later.
        var spreadFirst = Guid.NewGuid();
        var spreadSecond = Guid.NewGuid();
        await InsertTraceAsync(context, schemaName, spreadFirst, Tenant, "cccccccccccccccccccccccccccccccc", data.Spread, Origin.AddSeconds(300), Origin.AddSeconds(320));
        await InsertTraceAsync(context, schemaName, spreadSecond, Tenant, "dddddddddddddddddddddddddddddddd", data.Spread, Origin.AddSeconds(310), Origin.AddSeconds(315));
        await InsertSpanAsync(context, schemaName, spreadFirst, "spread-a", Origin.AddSeconds(300));
        await InsertSpanAsync(context, schemaName, spreadSecond, "spread-b", Origin.AddSeconds(310));

        return data;
    }

    private static async Task InsertTraceAsync(
        SqlServerTestContext context,
        string schemaName,
        Guid id,
        string tenant,
        string traceId,
        Guid? runId,
        DateTimeOffset started,
        DateTimeOffset? ended)
        => await context.ExecuteAsync(
            $"INSERT INTO {schemaName}.traces (id, tenant_id, trace_id, run_id, started_at, ended_at) " +
            $"VALUES ('{id}', N'{tenant}', N'{traceId}', {(runId is { } run ? $"'{run}'" : "NULL")}, " +
            $"'{Stamp(started)}', {(ended is { } end ? $"'{Stamp(end)}'" : "NULL")});");

    private static async Task InsertSpanAsync(SqlServerTestContext context, string schemaName, Guid headerId, string name, DateTimeOffset startedAt)
        => await context.ExecuteAsync(
            $"INSERT INTO {schemaName}.spans (id, trace_id, parent_span_id, name, kind, started_at, ended_at, attributes, status, span_id) " +
            $"VALUES ('{Guid.NewGuid()}', '{headerId}', NULL, N'{name}', 0, '{Stamp(startedAt)}', '{Stamp(startedAt.AddSeconds(1))}', " +
            $"N'{{\"k\":\"v\"}}', 1, N'{Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16]}');");

    private static async Task ApplyThroughAsync(SqlServerTestContext context, string schemaName, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");
        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in migrations)
        {
            await context.ExecuteRetryingDeadlockAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteRetryingDeadlockAsync(
                $"INSERT INTO {schemaName}.__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', SYSDATETIMEOFFSET());");
        }
    }

    private sealed record Data(Guid Retried, Guid Tied, Guid Spread, Guid SharedFirst, Guid SharedLast, Guid Plain);
}
