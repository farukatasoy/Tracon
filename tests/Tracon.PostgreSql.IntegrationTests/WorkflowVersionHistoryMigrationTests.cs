using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The workflow version history table, applied over workflows that already exist.
/// </summary>
/// <remarks>
/// 🚨 A workflow stored before the upgrade has no history rows. The migration starts each
/// one at the version it has today, and nothing else. Without that row the first rollback,
/// or the first resume of a run that recorded that version, would find no content under
/// its number. No compiler and no SQL text snapshot sees migration SQL, so the upgrade runs
/// against rows written under the old schema.
/// </remarks>
public sealed class WorkflowVersionHistoryMigrationTests(PostgresFixture fixture)
{
    private const string Tenant = "default";

    private const string OtherTenant = "other";

    private const string Payload = """{"description":"Seeded before the upgrade.","kind":"Sequential","agentNames":["support"]}""";

    [Fact]
    public async Task Existing_workflows_start_their_history_at_the_version_they_have_today()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var schema = context.SchemaName;

        // One history row per stored workflow, with the content and the save time it has today.
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {schema}.workflow_definition_versions;")).ShouldBe(2);
        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {schema}.workflow_definition_versions AS v JOIN {schema}.workflows AS w " +
            "ON v.tenant_id = w.tenant_id AND v.name = w.name AND v.version = w.version " +
            "WHERE v.definition = w.definition AND v.created_at = w.updated_at;"))
            .ShouldBe(2, "each history row copies the content and the save time of its workflow");

        var store = context.Workflows;

        (await store.ListVersionsAsync(Tenant, "alpha")).Select(static definition => definition.Version).ShouldBe([3]);
        (await store.ListVersionsAsync(Tenant, "alpha"))[0].Description.ShouldBe("Seeded before the upgrade.");
        (await store.ListVersionsAsync(OtherTenant, "beta")).Select(static definition => definition.Version).ShouldBe([1]);
        (await store.GetVersionAsync(Tenant, "alpha", 2)).ShouldBeNull("the versions before the upgrade were never kept");
        (await store.GetVersionAsync(Tenant, "beta", 1)).ShouldBeNull("another tenant's workflow is not visible");

        // The next save continues from the number the workflow holds, and the history grows by one row.
        var saved = await store.SaveAsync(Tenant, Saved());

        saved.Version.ShouldBe(4);
        (await store.ListVersionsAsync(Tenant, "alpha")).Select(static definition => definition.Version).ShouldBe([4, 3]);

        // A rollback to the backfilled row restores its content as a new version.
        var rolledBack = await store.RollbackAsync(Tenant, "alpha", 3);

        rolledBack.Version.ShouldBe(5);
        rolledBack.Description.ShouldBe("Seeded before the upgrade.");
        (await store.GetAsync(Tenant, "alpha"))!.Version.ShouldBe(5);
    }

    [Fact]
    public async Task The_backfill_can_run_again_without_adding_or_changing_a_row()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var store = context.Workflows;

        await store.SaveAsync(Tenant, Saved());

        var before = await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {context.SchemaName}.workflow_definition_versions;");

        before.ShouldBe(3);

        // The same SQL again: a version that is already there is skipped, and the saved row is not overwritten.
        var migration = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.PostgreSql.Migrations.")
            .Single(static migration => migration.Name.Contains("workflow_definition_versions", StringComparison.Ordinal));

        await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {context.SchemaName}.workflow_definition_versions;")).ShouldBe(before);
        (await store.GetVersionAsync(Tenant, "alpha", 4))!.Description.ShouldBe("Saved after the upgrade.");
        (await store.GetVersionAsync(Tenant, "alpha", 3))!.Description.ShouldBe("Seeded before the upgrade.");
    }

    private static WorkflowDefinition Saved() => new()
    {
        Name = "alpha",
        Description = "Saved after the upgrade.",
        Kind = WorkflowKind.Sequential,
        AgentNames = ["support"],
    };

    /// <summary>
    /// Builds the schema as it was before the history table, and stores two workflows in it
    /// the way the store wrote them then.
    /// </summary>
    private async Task<PostgresTestContext> OpenOldSchemaAsync()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);
        var all = MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly, "Tracon.PostgreSql.Migrations.");
        var target = all.Single(static migration => migration.Name.Contains("workflow_definition_versions", StringComparison.Ordinal)).Id;
        var before = all.Where(migration => migration.Id < target).OrderBy(migration => migration.Id).ToArray();

        before.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

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

        foreach (var migration in before)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync($"""
                INSERT INTO {schemaName}.__migrations (id, set_name, name, checksum, applied_at)
                VALUES ({migration.Id}, 'core', '{migration.Name}', '{migration.Checksum}', now());
                """);
        }

        await SeedWorkflowAsync(context, Tenant, "alpha", 3, "2026-02-02T00:00:00Z");
        await SeedWorkflowAsync(context, OtherTenant, "beta", 1, "2026-03-03T00:00:00Z");

        return context;
    }

    private static ValueTask<int> SeedWorkflowAsync(PostgresTestContext context, string tenant, string name, int version, string updatedAt)
        => context.ExecuteAsync(
            $"INSERT INTO {context.SchemaName}.workflows (id, tenant_id, name, version, definition, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{tenant}', '{name}', {version}, '{Payload}'::jsonb, '2026-01-01T00:00:00Z', '{updatedAt}');");
}
