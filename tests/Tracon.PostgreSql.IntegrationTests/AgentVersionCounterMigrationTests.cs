using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The agent version counter table, applied over agents that already exist.
/// </summary>
/// <remarks>
/// 🚨 An agent stored before the upgrade has no counter row. The migration starts each counter at the
/// version the agent holds today. Without that row the first save after the upgrade would take
/// version 1 and meet the unique key of the history, or, worse, reuse a number that a run, a workflow
/// node or an approval recorded. No compiler and no SQL text snapshot sees migration SQL, so the
/// upgrade runs against rows written under the old schema.
/// </remarks>
public sealed class AgentVersionCounterMigrationTests(PostgresFixture fixture)
{
    private const string Tenant = "default";

    private const string OtherTenant = "other";

    private const string Payload = """{"description":"Seeded before the upgrade.","model":{"provider":"echo","model":"echo-1"}}""";

    [Fact]
    public async Task Existing_agents_keep_their_numbering_after_the_counter_table_arrives()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var schema = context.SchemaName;

        // One counter per stored agent, at the version the agent holds, in the tenant that owns it.
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {schema}.agent_definition_version_counters;")).ShouldBe(2);
        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {schema}.agent_definition_version_counters AS c JOIN {schema}.agent_definitions AS d " +
            "ON c.tenant_id = d.tenant_id AND c.name = d.name AND c.last_version = d.version;"))
            .ShouldBe(2, "each counter starts at the version of its agent");

        var store = context.AgentDefinitions;

        (await store.ListVersionsAsync("alpha")).Select(static definition => definition.Version).ShouldBe([5]);
        (await store.GetAsync("beta")).ShouldBeNull("another tenant's agent is not visible");

        // The next save is version 6, not 1, and the history grows by one row.
        var saved = await store.SaveAsync(Saved());

        saved.Version.ShouldBe(6);
        (await store.ListVersionsAsync("alpha")).Select(static definition => definition.Version).ShouldBe([6, 5]);

        // A delete removes the content, and the numbering goes on from the number the agent had.
        (await store.DeleteAsync("alpha")).ShouldBeTrue();
        (await store.SaveAsync(Saved())).Version.ShouldBe(7);
        (await store.GetVersionAsync("alpha", 5)).ShouldBeNull("the history went with the delete");
    }

    [Fact]
    public async Task The_backfill_can_run_again_without_adding_or_lowering_a_counter()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var store = context.AgentDefinitions;

        await store.SaveAsync(Saved());
        await store.DeleteAsync("alpha");

        // The agent row is gone and its counter stands at 6. The same SQL again must add no row
        // (there is no agent to copy) and must not move the counter back to the old row's number.
        var migration = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.PostgreSql.Migrations.")
            .Single(static migration => migration.Name.Contains("agent_definition_version_counters", StringComparison.Ordinal));

        await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {context.SchemaName}.agent_definition_version_counters;")).ShouldBe(2);
        (await store.SaveAsync(Saved())).Version.ShouldBe(7);
    }

    private static AgentDefinition Saved() => new()
    {
        Name = "alpha",
        Description = "Saved after the upgrade.",
        Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
    };

    /// <summary>
    /// Builds the schema as it was before the counter table, and stores two agents in it the way
    /// the store wrote them then: the current row and one history row at the same version.
    /// </summary>
    private async Task<PostgresTestContext> OpenOldSchemaAsync()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);
        var all = MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly, "Tracon.PostgreSql.Migrations.");
        var target = all.Single(static migration => migration.Name.Contains("agent_definition_version_counters", StringComparison.Ordinal)).Id;
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

        await SeedAgentAsync(context, Tenant, "alpha", 5, "2026-02-02T00:00:00Z");
        await SeedAgentAsync(context, OtherTenant, "beta", 1, "2026-03-03T00:00:00Z");

        return context;
    }

    private static async ValueTask SeedAgentAsync(PostgresTestContext context, string tenant, string name, int version, string updatedAt)
    {
        await context.ExecuteAsync(
            $"INSERT INTO {context.SchemaName}.agent_definitions (id, tenant_id, name, version, definition, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{tenant}', '{name}', {version}, '{Payload}'::jsonb, '2026-01-01T00:00:00Z', '{updatedAt}');");
        await context.ExecuteAsync(
            $"INSERT INTO {context.SchemaName}.agent_definition_versions (id, agent_id, version, definition, created_by, created_at) " +
            $"SELECT gen_random_uuid(), id, version, definition, NULL, updated_at FROM {context.SchemaName}.agent_definitions " +
            $"WHERE tenant_id = '{tenant}' AND name = '{name}';");
    }
}
