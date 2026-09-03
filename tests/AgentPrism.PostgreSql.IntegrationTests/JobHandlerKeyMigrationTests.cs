using System.Globalization;
using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// Phase 137 — <c>jobs.kind</c>/<c>job_schedules.kind</c> become
/// <c>handler_key</c>, and the backfill has to map all nine values.
/// </summary>
/// <remarks>
/// 🚨 A one-way migration over data that already exists. A single digit
/// transposed in the <c>CASE</c> table — 7 and 8, say, which K-583 keeps
/// deliberately apart — would silently relabel every historic
/// <c>ApprovalResume</c> job as a <c>RunContinuation</c> and could not be
/// undone once <c>kind</c> is dropped. No compiler and no snapshot sees SQL
/// text, so the mapping is asserted against real rows written under the OLD
/// schema.
/// </remarks>
public sealed class JobHandlerKeyMigrationTests(PostgresFixture fixture)
{
    /// <summary>The migration under test. Everything before it defines the pre-upgrade schema.</summary>
    private const int HandlerKeyMigrationId = 43;

    /// <summary>
    /// The mapping the migration promises, in <c>JobKind</c>'s own numbering
    /// as it stood when the enum was removed.
    /// </summary>
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
    public async Task All_nine_kinds_are_mapped_and_no_row_is_lost()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

        await ApplyThroughAsync(context, schemaName, HandlerKeyMigrationId - 1);

        // Rows written under the OLD shape: `kind` still exists, `handler_key`
        // does not. One schedule and one job per value, plus a second job for
        // one value so a count check cannot pass by coincidence.
        foreach (var (kind, _) in Expected)
        {
            await SeedScheduleAsync(context, schemaName, kind);
            await SeedJobAsync(context, schemaName, kind);
        }

        await SeedJobAsync(context, schemaName, 0);

        var jobsBefore = await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {schemaName}.jobs;");
        var schedulesBefore = await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {schemaName}.job_schedules;");

        jobsBefore.ShouldBe(10);
        schedulesBefore.ShouldBe(9);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {schemaName}.jobs;")).ShouldBe(jobsBefore);
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {schemaName}.job_schedules;"))
            .ShouldBe(schedulesBefore);

        foreach (var (kind, handlerKey) in Expected)
        {
            // `name`/`target_name` carry the ORIGINAL kind, so each row can be
            // matched back to what it was before the column disappeared.
            (await context.ScalarAsync<string>(
                $"SELECT handler_key FROM {schemaName}.job_schedules WHERE name = 'schedule-{kind}';"))
                .ShouldBe(handlerKey, $"schedule seeded as kind {kind}");

            (await context.ScalarAsync<long>(
                $"SELECT COUNT(*) FROM {schemaName}.jobs " +
                $"WHERE target_name = 'target-{kind}' AND handler_key = '{handlerKey}';"))
                .ShouldBe(kind == 0 ? 2 : 1, $"jobs seeded as kind {kind}");
        }

        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {schemaName}.jobs WHERE handler_key IS NULL;")).ShouldBe(0);

        // The old column is gone, and the new one is NOT NULL.
        (await ColumnExistsAsync(context, schemaName, "jobs", "kind")).ShouldBeFalse();
        (await ColumnExistsAsync(context, schemaName, "job_schedules", "kind")).ShouldBeFalse();
        (await IsNullableAsync(context, schemaName, "jobs", "handler_key")).ShouldBeFalse();
        (await IsNullableAsync(context, schemaName, "job_schedules", "handler_key")).ShouldBeFalse();
    }

    /// <summary>Runs every migration up to <paramref name="lastId"/> and records it as applied.</summary>
    private static async Task ApplyThroughAsync(PostgresTestContext context, string schemaName, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "AgentPrism.PostgreSql.Migrations.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

        await context.ExecuteAsync($"CREATE SCHEMA {schemaName};");
        await context.ExecuteAsync($"""
            CREATE TABLE {schemaName}.__migrations (
                id         integer     NOT NULL,
                set_name   text        NOT NULL,
                name       text        NOT NULL,
                checksum   text        NOT NULL,
                applied_at timestamptz NOT NULL,
                PRIMARY KEY (set_name, id)
            );
            """);

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync($"""
                INSERT INTO {schemaName}.__migrations (id, set_name, name, checksum, applied_at)
                VALUES ({migration.Id}, 'core', '{migration.Name}', '{migration.Checksum}', now());
                """);
        }
    }

    private static ValueTask<int> SeedScheduleAsync(PostgresTestContext context, string schemaName, short kind)
    {
        var value = kind.ToString(CultureInfo.InvariantCulture);

        return context.ExecuteAsync(
            $"INSERT INTO {schemaName}.job_schedules " +
            "(id, tenant_id, name, kind, target_name, payload, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), 'test', 'schedule-{value}', {value}, 'target', " +
            "'{}'::jsonb, now(), now());");
    }

    private static ValueTask<int> SeedJobAsync(PostgresTestContext context, string schemaName, short kind)
    {
        var value = kind.ToString(CultureInfo.InvariantCulture);

        return context.ExecuteAsync(
            $"INSERT INTO {schemaName}.jobs " +
            "(id, tenant_id, kind, target_name, status, payload, scheduled_for, created_at) " +
            $"VALUES (gen_random_uuid(), 'test', {value}, 'target-{value}', 3, " +
            "'{}'::jsonb, now(), now());");
    }

    private static async Task<bool> ColumnExistsAsync(
        PostgresTestContext context,
        string schemaName,
        string table,
        string column)
        => await context.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.columns " +
            $"WHERE table_schema = '{schemaName}' AND table_name = '{table}' AND column_name = '{column}');");

    private static async Task<bool> IsNullableAsync(
        PostgresTestContext context,
        string schemaName,
        string table,
        string column)
        => string.Equals(
            await context.ScalarAsync<string>(
                $"SELECT is_nullable FROM information_schema.columns " +
                $"WHERE table_schema = '{schemaName}' AND table_name = '{table}' AND column_name = '{column}';"),
            "YES",
            StringComparison.Ordinal);
}
