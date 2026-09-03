using System.Globalization;
using AgentPrism.Sqlite.IntegrationTests.Infrastructure;

namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>
/// Phase 137 — the SQLite twin of the PostgreSQL mapping test.
/// </summary>
/// <remarks>
/// 🚨 SQLite deviates twice and both deviations need real rows to check.
/// The column is dropped IN PLACE (rebuilding the table would cascade-delete
/// <c>job_items</c> under <c>PRAGMA foreign_keys = ON</c>), and the new column
/// takes a deliberately invalid empty-string default because SQLite has no
/// <c>ALTER COLUMN</c>. Neither is visible to a compiler or to the other two
/// providers' SQL snapshots.
/// </remarks>
public sealed class JobHandlerKeyMigrationTests(SqliteFixture fixture)
{
    private const int HandlerKeyMigrationId = 30;

    private static readonly (short Kind, string HandlerKey)[] Expected =
    [
        (0, JobHandlerKeys.AgentBatch),
        (1, JobHandlerKeys.Workflow),
        (2, JobHandlerKeys.Eval),
        (3, JobHandlerKeys.WebhookDelivery),
        (4, JobHandlerKeys.Retention),
        (5, JobHandlerKeys.AgentRun),
        (6, JobHandlerKeys.OnlineEval),
        (7, JobHandlerKeys.ApprovalResume),
        (8, JobHandlerKeys.RunContinuation),
    ];

    [Fact]
    public async Task All_nine_kinds_are_mapped_and_no_row_or_job_item_is_lost()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, HandlerKeyMigrationId - 1);

        foreach (var (kind, _) in Expected)
        {
            await SeedScheduleAsync(context, prefix, kind);
            await SeedJobAsync(context, prefix, kind);
        }

        // 🚨 The point of the DROP COLUMN deviation: a rebuild of `jobs` would
        // fire job_items' ON DELETE CASCADE and silently empty this table.
        await context.ExecuteAsync(
            $"INSERT INTO {prefix}job_items (id, job_id, seq, input, status) " +
            $"SELECT hex(randomblob(16)), id, 0, 'input', 1 FROM {prefix}jobs;");

        var jobsBefore = await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}jobs;");
        var itemsBefore = await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}job_items;");

        jobsBefore.ShouldBe(9);
        itemsBefore.ShouldBe(9);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}jobs;")).ShouldBe(jobsBefore);
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}job_items;")).ShouldBe(itemsBefore);

        foreach (var (kind, handlerKey) in Expected)
        {
            (await context.ScalarAsync<string>(
                $"SELECT handler_key FROM {prefix}job_schedules WHERE name = 'schedule-{kind}';"))
                .ShouldBe(handlerKey, $"schedule seeded as kind {kind}");

            (await context.ScalarAsync<string>(
                $"SELECT handler_key FROM {prefix}jobs WHERE target_name = 'target-{kind}';"))
                .ShouldBe(handlerKey, $"job seeded as kind {kind}");
        }

        (await ColumnExistsAsync(context, prefix, "jobs", "kind")).ShouldBeFalse();
        (await ColumnExistsAsync(context, prefix, "job_schedules", "kind")).ShouldBeFalse();
        (await ColumnExistsAsync(context, prefix, "jobs", "handler_key")).ShouldBeTrue();
    }

    private static async Task ApplyThroughAsync(SqliteTestContext context, string prefix, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "AgentPrism.Sqlite.Migrations.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty();

        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', '2026-01-01T00:00:00Z');");
        }
    }

    private static ValueTask<int> SeedScheduleAsync(SqliteTestContext context, string prefix, short kind)
    {
        var value = kind.ToString(CultureInfo.InvariantCulture);

        return context.ExecuteAsync(
            $"INSERT INTO {prefix}job_schedules " +
            "(id, tenant_id, name, kind, target_name, payload, created_at, updated_at) " +
            $"VALUES (hex(randomblob(16)), 'test', 'schedule-{value}', {value}, 'target', " +
            "'{}', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
    }

    private static ValueTask<int> SeedJobAsync(SqliteTestContext context, string prefix, short kind)
    {
        var value = kind.ToString(CultureInfo.InvariantCulture);

        return context.ExecuteAsync(
            $"INSERT INTO {prefix}jobs " +
            "(id, tenant_id, kind, target_name, status, payload, scheduled_for, created_at) " +
            $"VALUES (hex(randomblob(16)), 'test', {value}, 'target-{value}', 3, " +
            "'{}', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteTestContext context,
        string prefix,
        string table,
        string column)
        => await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM pragma_table_info('{prefix}{table}') WHERE name = '{column}';") > 0;
}
