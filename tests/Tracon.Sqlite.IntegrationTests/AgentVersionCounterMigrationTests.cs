using System.Globalization;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The agent version counter table: applied over agents that already exist, and written beside a process of
/// the older release.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 An agent stored before the upgrade has no counter row. The migration starts each counter at the
/// version the agent holds today. Without that row the first save after the upgrade would take
/// version 1 and meet the unique key of the history, or, worse, reuse a number that a run, a workflow
/// node or an approval recorded. No compiler and no SQL text snapshot sees migration SQL, so the
/// upgrade runs against rows written under the old schema.
/// </para>
/// <para>
/// 🚨 A rolling upgrade keeps a process of the older release up beside the new one. It writes the agent
/// row and its history entry and never touches the counter. A number taken from the counter alone would
/// then collide with that history entry for good, and every save of the name would fail.
/// </para>
/// </remarks>
public sealed class AgentVersionCounterMigrationTests(SqliteFixture fixture)
{
    private const string Tenant = "default";

    private const string OtherTenant = "other";

    /// <summary>The text shape <c>SqliteDialect.AddTimestamp</c> writes.</summary>
    private const string Seeded = "2026-01-01T00:00:00.0000000Z";

    private const string Payload = """{"description":"Seeded before the upgrade.","model":{"provider":"echo","model":"echo-1"}}""";

    [Fact]
    public async Task Existing_agents_keep_their_numbering_after_the_counter_table_arrives()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        // One counter per stored agent, at the version the agent holds, in the tenant that owns it. Two
        // tenants hold an agent of the same name, so the key has to carry the tenant.
        (await CountersAsync(context)).ShouldBe(2);
        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {context.TablePrefix}agent_definition_version_counters AS c JOIN {context.TablePrefix}agent_definitions AS d " +
            "ON c.tenant_id = d.tenant_id AND c.name = d.name AND c.last_version = d.version;"))
            .ShouldBe(2, "each counter starts at the version of its agent");

        var store = context.AgentDefinitions;

        (await store.ListVersionsAsync("alpha")).Select(static definition => definition.Version).ShouldBe([5]);

        // The next save is version 6, not 1, and the history grows by one row.
        var saved = await store.SaveAsync(Saved());

        saved.Version.ShouldBe(6);
        (await store.ListVersionsAsync("alpha")).Select(static definition => definition.Version).ShouldBe([6, 5]);

        // A delete removes the content, and the numbering goes on from the number the agent had.
        (await store.DeleteAsync("alpha")).ShouldBeTrue();
        (await store.SaveAsync(Saved())).Version.ShouldBe(7);
        (await store.GetVersionAsync("alpha", 5)).ShouldBeNull("the history went with the delete");

        // The other tenant's agent of the same name did not move.
        (await CounterAsync(context, OtherTenant, "alpha")).ShouldBe(2);
    }

    [Fact]
    public async Task The_backfill_can_run_again_without_adding_or_lowering_a_counter()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        // The counter stands above the agent row, as it does once a name has been deleted and created again.
        await context.ExecuteAsync($"UPDATE {context.TablePrefix}agent_definition_version_counters SET last_version = 9 WHERE tenant_id = '{Tenant}' AND name = 'alpha';");

        var migration = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.")
            .Single(static migration => migration.Name.Contains("agent_definition_version_counters", StringComparison.Ordinal));

        await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));

        // The same SQL again adds no row and does not move the counter back to the agent row's number.
        (await CountersAsync(context)).ShouldBe(2);
        (await CounterAsync(context, Tenant, "alpha")).ShouldBe(9);
        (await context.AgentDefinitions.SaveAsync(Saved())).Version.ShouldBe(10);
    }

    [Fact]
    public async Task A_save_continues_from_the_agent_row_when_an_older_process_wrote_it_after_the_upgrade()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var store = context.AgentDefinitions;

        // The older process saves an agent that has a counter: the row moves to 6, the counter stays at 5.
        await OlderProcessSavesAsync(context, Tenant, "alpha", 6);
        (await CounterAsync(context, Tenant, "alpha")).ShouldBe(5);

        (await store.SaveAsync(Saved())).Version.ShouldBe(7);
        (await store.SaveAsync(Saved())).Version.ShouldBe(8);
        (await store.ListVersionsAsync("alpha")).Select(static definition => definition.Version).ShouldBe([8, 7, 6, 5]);

        // The older process creates an agent that has no counter yet.
        await SeedAgentAsync(context, Tenant, "gamma", 1, "2026-04-04T00:00:00Z");

        (await store.SaveAsync(Saved() with { Name = "gamma" })).Version.ShouldBe(2);
        (await CounterAsync(context, Tenant, "gamma")).ShouldBe(2);
    }

    [Fact]
    public async Task A_save_whose_history_write_fails_leaves_the_counter_where_it_was()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var store = context.AgentDefinitions;

        // Occupy the number the next save will take, so the history insert collides.
        await InsertHistoryAsync(context, Tenant, "alpha", 6);

        await Should.ThrowAsync<Exception>(async () => await store.SaveAsync(Saved()));

        // One transaction: the counter and the agent row did not move on without a history entry to name them.
        (await CounterAsync(context, Tenant, "alpha")).ShouldBe(5);
        (await store.GetAsync("alpha"))!.Version.ShouldBe(5);

        await context.ExecuteAsync($"DELETE FROM {context.TablePrefix}agent_definition_versions WHERE version = 6 AND agent_id = " +
            $"(SELECT id FROM {context.TablePrefix}agent_definitions WHERE tenant_id = '{Tenant}' AND name = 'alpha');");

        (await store.SaveAsync(Saved())).Version.ShouldBe(6);
    }

    private static AgentDefinition Saved() => new()
    {
        Name = "alpha",
        Description = "Saved after the upgrade.",
        Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
    };

    private static async ValueTask<long> CountersAsync(SqliteTestContext context)
        => await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {context.TablePrefix}agent_definition_version_counters;");

    private static async ValueTask<long> CounterAsync(SqliteTestContext context, string tenant, string name)
        => await context.ScalarAsync<long>(
            $"SELECT last_version FROM {context.TablePrefix}agent_definition_version_counters WHERE tenant_id = '{tenant}' AND name = '{name}';");

    /// <summary>
    /// Builds the schema as it was before the counter table, and stores three agents in it the way
    /// the store wrote them then: the current row and one history row at the same version. Two tenants
    /// hold an agent called <c>alpha</c>.
    /// </summary>
    private async Task<SqliteTestContext> OpenOldSchemaAsync()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        var context = SqliteTestContext.Create(fixture, prefix);
        var all = MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.");
        var target = all.Single(static migration => migration.Name.Contains("agent_definition_version_counters", StringComparison.Ordinal)).Id;
        var before = all.Where(migration => migration.Id < target).OrderBy(migration => migration.Id).ToArray();

        before.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in before)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', '{Seeded}');");
        }

        await SeedAgentAsync(context, Tenant, "alpha", 5, "2026-02-02T00:00:00.0000000Z");
        await SeedAgentAsync(context, OtherTenant, "alpha", 2, "2026-03-03T00:00:00.0000000Z");

        return context;
    }

    private static async ValueTask SeedAgentAsync(SqliteTestContext context, string tenant, string name, int version, string updatedAt)
    {
        var id = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

        await context.ExecuteAsync(
            $"INSERT INTO {context.TablePrefix}agent_definitions (id, tenant_id, name, version, definition, created_at, updated_at) " +
            $"VALUES ('{id}', '{tenant}', '{name}', {version}, '{Payload}', '{Seeded}', '{updatedAt}');");
        await InsertHistoryAsync(context, tenant, name, version);
    }

    /// <summary>What the older release does on a save: the row moves to the new number, and so does the history.</summary>
    private static async ValueTask OlderProcessSavesAsync(SqliteTestContext context, string tenant, string name, int version)
    {
        await context.ExecuteAsync(
            $"UPDATE {context.TablePrefix}agent_definitions SET version = {version} WHERE tenant_id = '{tenant}' AND name = '{name}';");
        await InsertHistoryAsync(context, tenant, name, version);
    }

    private static ValueTask<int> InsertHistoryAsync(SqliteTestContext context, string tenant, string name, int version)
        => context.ExecuteAsync(
            $"INSERT INTO {context.TablePrefix}agent_definition_versions (id, agent_id, version, definition, created_by, created_at) " +
            $"SELECT '{Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant()}', id, {version}, definition, NULL, '{Seeded}' " +
            $"FROM {context.TablePrefix}agent_definitions WHERE tenant_id = '{tenant}' AND name = '{name}';");
}
