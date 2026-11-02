using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The names of definitions compare ordinally: applied over rows written under the case-insensitive
/// comparison, and stopped halfway.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 A migration that drops a key, changes the column under it and builds the key again can lose a row
/// or a value only on rows that already exist. No compiler and no SQL text snapshot sees migration SQL, so
/// the upgrade runs against rows the older release wrote.
/// </para>
/// <para>
/// The stores write the old schema where the context has one. The tables without a store here (files,
/// approvals, voice sessions, workflow requests) get a row of plain SQL, so that no table the migration
/// rebuilds is empty when it runs.
/// </para>
/// </remarks>
public sealed class DefinitionNameUpgradeTests(SqlServerFixture fixture)
{
    private const string Tenant = "default";

    private const string Ordinal = "Latin1_General_BIN2";

    private static readonly Guid SeedRunId = new("bbbbbbbb-2222-3333-4444-555555555555");

    /// <summary>Every key and index the migration drops and builds again.</summary>
    private static readonly string[] RebuiltObjects =
    [
        "agent_definition_version_counters_pk", "quota_usage_pk", "workflow_definition_versions_pk",
        "agent_definitions_tenant_name_uq", "agent_files_tenant_agent_path_uq", "agent_skill_resources_skill_name_uq",
        "agent_skill_scripts_skill_name_uq", "agent_skills_tenant_name_uq", "eval_suites_tenant_name_uq",
        "experiments_tenant_name_uq", "inbound_triggers_tenant_name_uq", "job_schedules_tenant_name_uq",
        "mcp_servers_tenant_name_uq", "quotas_scope_uq", "skill_script_grants_uq", "tool_approval_rules_scope_uq",
        "webhook_subscriptions_tenant_name_uq", "workflows_tenant_name_uq",
        "experiments_running_agent_uq", "experiments_tenant_agent_idx", "runs_agent_version_idx",
        "runs_tenant_agent_started_idx", "runs_workflow_idx", "sessions_tenant_agent_updated_idx",
        "skill_script_grants_lookup_idx", "tool_invocations_tool_created_idx",
    ];

    /// <summary>The columns that carry a name, as <c>table.column</c>, that the seed fills and the checks follow.</summary>
    private static readonly (string Table, string Column)[] NameColumns =
    [
        ("agent_definitions", "name"), ("agent_definition_version_counters", "name"), ("workflows", "name"),
        ("workflow_definition_versions", "name"), ("agent_skills", "name"), ("eval_suites", "name"), ("eval_suites", "agent_name"),
        ("experiments", "name"), ("experiments", "agent_name"), ("inbound_triggers", "name"), ("inbound_triggers", "target_name"),
        ("job_schedules", "name"), ("job_schedules", "target_name"), ("quotas", "agent_name"), ("quota_usage", "agent_name"),
        ("skill_script_grants", "skill_name"), ("tool_approval_rules", "agent_name"), ("tool_approval_rules", "tool_name"),
        ("agent_files", "agent_name"), ("runs", "agent_name"), ("runs", "workflow_name"), ("sessions", "agent_name"),
        ("voice_sessions", "agent_name"), ("workflow_requests", "workflow_name"), ("pending_approvals", "tool_name"),
        ("tool_invocations", "tool_name"), ("run_events", "tool_name"),
    ];

    [Fact]
    public async Task Rows_written_before_the_upgrade_keep_their_count_and_their_values()
    {
        await using var context = await OpenOldSchemaAsync();

        var before = await RowCountsAsync(context);

        before.Where(static pair => pair.Value == 0).Select(static pair => pair.Key)
            .ShouldBeEmpty("the seed has to put a row in every table the migration rebuilds, or its count proves nothing");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await RowCountsAsync(context)).ShouldBe(before, "the migration neither adds, removes nor merges a row");

        // The values come back as they were written, in their own letter case.
        (await context.AgentDefinitions.GetAsync("Writer")).ShouldNotBeNull().Name.ShouldBe("Writer");
        (await context.Sessions.GetAsync("Session-1")).ShouldNotBeNull().AgentName.ShouldBe("Writer");
        (await context.Runs.GetRunAsync(SeedRunId)).ShouldNotBeNull().AgentName.ShouldBe("Writer");
        (await context.Evals.GetSuiteAsync(Tenant, "Suite-A")).ShouldNotBeNull().Name.ShouldBe("Suite-A");
        (await context.JobSchedules.GetAsync(Tenant, "Night-Report")).ShouldNotBeNull().Name.ShouldBe("Night-Report");
        (await context.ApprovalRules.ListAsync(Tenant)).ShouldHaveSingleItem().ToolName.ShouldBe("Get_Order");

        (await context.ScalarAsync<string>(
            $"SELECT agent_name FROM {context.SchemaName}.agent_files;")).ShouldBe("Writer");
    }

    [Fact]
    public async Task Every_column_that_carries_a_name_compares_ordinally_once_the_migration_ran()
    {
        await using var context = await OpenOldSchemaAsync();

        // Before: the old comparison folds the case, so a second spelling is the first definition.
        foreach (var (table, column) in NameColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: false, $"{table}.{column} folds the case before the migration");
        }

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var (table, column) in NameColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: true, $"{table}.{column} carries a name");
        }
    }

    [Fact]
    public async Task Two_names_that_differ_only_in_case_are_two_definitions_once_the_migration_ran()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        await context.AgentDefinitions.SaveAsync(TestData.Definition("writer"));

        (await context.AgentDefinitions.GetAsync("Writer")).ShouldNotBeNull().Name.ShouldBe("Writer");
        (await context.AgentDefinitions.GetAsync("writer")).ShouldNotBeNull().Name.ShouldBe("writer");
        (await context.AgentDefinitions.ListAsync()).Count.ShouldBe(2);

        // The runs of the two agents are listed apart.
        await context.Runs.StartRunAsync(TestData.Run(TraconId.NewId(), "writer"));

        (await context.Runs.QueryRunsAsync(new RunQuery { AgentName = "Writer" })).ShouldHaveSingleItem().Id.ShouldBe(SeedRunId);
        (await context.Runs.QueryRunsAsync(new RunQuery { AgentName = "writer" })).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_migration_that_fails_on_its_last_table_leaves_the_old_shape_everywhere()
    {
        await using var context = await OpenOldSchemaAsync();

        // A schema-bound view makes the change of the last column impossible, after every earlier table was
        // already rebuilt inside the same transaction.
        await context.ExecuteAsync(
            $"CREATE VIEW {context.SchemaName}.event_tools WITH SCHEMABINDING AS SELECT run_id, seq, tool_name FROM {context.SchemaName}.run_events;");

        await Should.ThrowAsync<TraconException>(async () => await context.Migrations.ApplyAsync());

        AssertCollation(await CollationOfAsync(context, "agent_definitions", "name"), ordinal: false, "the first table rolled back with the last");
        AssertCollation(await CollationOfAsync(context, "tool_invocations", "tool_name"), ordinal: false, "the last table before the failure rolled back too");
        (await context.CatalogScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.key_constraints WHERE name = N'agent_definitions_tenant_name_uq' AND parent_object_id = OBJECT_ID(N'{context.SchemaName}.agent_definitions');"))
            .ShouldBe(1, "the key the migration dropped is back");
        (await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.__migrations WHERE name LIKE N'%ordinal_definition_names%';"))
            .ShouldBe(0, "a failed migration is not in the ledger");

        // Nothing is half done: the next run, without the view, goes through.
        await context.ExecuteAsync($"DROP VIEW {context.SchemaName}.event_tools;");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        AssertCollation(await CollationOfAsync(context, "agent_definitions", "name"), ordinal: true, "the retry went through");
        AssertCollation(await CollationOfAsync(context, "run_events", "tool_name"), ordinal: true, "the retry went through");
    }

    [Fact]
    public async Task The_migration_can_run_again_over_its_own_result()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var migration = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.")
            .Single(static migration => migration.Name.Contains("ordinal_definition_names", StringComparison.Ordinal));

        var before = await RowCountsAsync(context);

        // A run that wrote its work but not its ledger line starts over from the top.
        await context.ExecuteRetryingDeadlockAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));

        (await RowCountsAsync(context)).ShouldBe(before);
        AssertCollation(await CollationOfAsync(context, "runs", "agent_name"), ordinal: true, "the column stays ordinal");
        (await context.AgentDefinitions.GetAsync("Writer")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Every_key_and_index_comes_back_with_the_shape_it_had()
    {
        await using var context = await OpenOldSchemaAsync();

        var before = await ShapesAsync(context);

        before.Count.ShouldBe(RebuiltObjects.Length, "every object the migration rebuilds exists before it runs");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        // Name, kind, uniqueness, key columns in order (and their direction) and the filter: a key that came
        // back non-unique, a filtered index that came back unfiltered or a column that moved shows here.
        (await ShapesAsync(context)).ShouldBe(before);
    }

    [Fact]
    public async Task A_view_over_the_runs_reports_the_new_collation_of_the_agent_name()
    {
        await using var context = await OpenOldSchemaAsync();

        // A view that is not schema-bound copies the collation of its columns when it is created. The read
        // contract view is named like this one.
        await context.ExecuteAsync($"CREATE VIEW {context.SchemaName}.runs_v1 AS SELECT id, agent_name FROM {context.SchemaName}.runs;");

        AssertCollation(await CollationOfAsync(context, "runs_v1", "agent_name"), ordinal: false, "the view was made over the old column");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        AssertCollation(await CollationOfAsync(context, "runs_v1", "agent_name"), ordinal: true, "the migration refreshed the view");
    }

    private static void AssertCollation(string? actual, bool ordinal, string because)
        => string.Equals(actual, Ordinal, StringComparison.Ordinal).ShouldBe(ordinal, $"{because} (collation: {actual})");

    private static ValueTask<string?> CollationOfAsync(SqlServerTestContext context, string table, string column)
        => context.CatalogScalarAsync<string>(
            $"SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'{context.SchemaName}.{table}') AND name = N'{column}';");

    private static async ValueTask<IReadOnlyList<string>> ShapesAsync(SqlServerTestContext context)
    {
        var names = string.Join(", ", RebuiltObjects.Select(static name => $"N'{name}'"));

        var shapes = await context.CatalogScalarAsync<string>(
            "SELECT STRING_AGG(CAST(x.shape AS nvarchar(max)), N';') WITHIN GROUP (ORDER BY x.shape) FROM (" +
            "SELECT CAST(i.name AS nvarchar(200)) COLLATE DATABASE_DEFAULT + N'|' + CAST(i.type_desc AS nvarchar(60)) COLLATE DATABASE_DEFAULT + N'|' + " +
            "CAST(i.is_primary_key AS nvarchar(1)) + CAST(i.is_unique_constraint AS nvarchar(1)) + CAST(i.is_unique AS nvarchar(1)) + N'|' + " +
            "ISNULL((SELECT STRING_AGG(CAST(c.name AS nvarchar(200)) COLLATE DATABASE_DEFAULT + CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N'' END, N',') WITHIN GROUP (ORDER BY ic.key_ordinal) " +
            "FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0), N'') + N'|' + " +
            "ISNULL(CAST(i.filter_definition AS nvarchar(400)) COLLATE DATABASE_DEFAULT, N'') AS shape " +
            "FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id " +
            $"WHERE SCHEMA_NAME(t.schema_id) = N'{context.SchemaName}' AND i.name IN ({names})) AS x;");

        return (shapes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    private static async ValueTask<long> RowCountAsync(SqlServerTestContext context, string table)
        => await context.ScalarAsync<long>($"SELECT CAST(COUNT(*) AS bigint) FROM {context.SchemaName}.{table};");

    /// <summary>The row count of every table the seed fills.</summary>
    private static async ValueTask<SortedDictionary<string, long>> RowCountsAsync(SqlServerTestContext context)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);

        foreach (var table in NameColumns.Select(static pair => pair.Table).Distinct(StringComparer.Ordinal))
        {
            counts[table] = await RowCountAsync(context, table);
        }

        return counts;
    }

    /// <summary>
    /// Builds the schema as it was before the migration and writes into it the way the older release did:
    /// a definition of every kind, and the records of a run, whose names carry capitals.
    /// </summary>
    private async Task<SqlServerTestContext> OpenOldSchemaAsync()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        var context = SqlServerTestContext.Create(fixture, schemaName);
        var all = MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.");
        var target = all.Single(static migration => migration.Name.Contains("ordinal_definition_names", StringComparison.Ordinal)).Id;
        var before = all.Where(migration => migration.Id < target).OrderBy(migration => migration.Id).ToArray();

        before.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");
        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in before)
        {
            await context.ExecuteRetryingDeadlockAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteRetryingDeadlockAsync(
                $"INSERT INTO {schemaName}.__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', SYSDATETIMEOFFSET());");
        }

        await SeedAsync(context);

        return context;
    }

    private static async ValueTask SeedAsync(SqlServerTestContext context)
    {
        var now = DateTimeOffset.UtcNow;

        await context.AgentDefinitions.SaveAsync(TestData.Definition("Writer"));
        await context.Workflows.SaveAsync(Tenant, new WorkflowDefinition
        {
            Name = "Review-Flow",
            Description = "Three-step review.",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["Researcher", "Writer"],
        });

        await context.AgentSkills.SaveAsync(new AgentSkillDefinition
        {
            TenantId = Tenant,
            Name = "Invoice-Review",
            Description = "Invoice review.",
            Instructions = "Check every invoice.",
        });

        await context.Evals.SaveSuiteAsync(TestData.EvalSuite(Tenant, "Suite-A"));

        await context.Experiments.SaveAsync(new Experiment
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Name = "Experiment-A",
            AgentName = "Writer",
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 },
            ],
        });

        await context.InboundTriggers.UpsertAsync(new InboundTrigger
        {
            TenantId = Tenant,
            Name = "Slack-Hook",
            TargetKind = InboundTriggerTargetKind.Agent,
            TargetName = "Writer",
            SigningSecretConfigurationName = "Tracon:TriggerSecrets:Test:Slack",
            PayloadMode = InboundTriggerPayloadMode.WholeBody,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await context.JobSchedules.SaveAsync(TestData.Schedule(Tenant, "Night-Report") with { TargetName = "Writer" });

        await context.Quotas.SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = "Writer",
            Period = QuotaPeriod.Daily,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await context.Quotas.AddUsageAsync(
            new QuotaConsumption { TenantId = Tenant, AgentName = "Writer", Runs = 1, Tokens = 10, OccurredAt = now },
            new Dictionary<QuotaPeriod, DateOnly> { [QuotaPeriod.Daily] = DateOnly.FromDateTime(now.UtcDateTime) });

        await context.SkillScriptGrants.GrantAsync(new SkillScriptGrant
        {
            TenantId = Tenant,
            SkillName = "Invoice-Review",
            ScriptName = null,
            GrantedBy = "admin@example.com",
            GrantedAt = now,
        });

        await context.ApprovalRules.AddAsync(new ToolApprovalRule
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = "Writer",
            ToolName = "Get_Order",
            CreatedBy = "operator@example",
            CreatedAt = now,
        });

        await context.Sessions.SaveAsync(TestData.Session("Session-1") with { AgentName = "Writer" });

        await context.Runs.StartRunAsync(TestData.Run(SeedRunId, "Writer") with { WorkflowName = "Review-Flow" });
        await context.Runs.AppendEventAsync(TestData.Event(SeedRunId, 0) with { Type = RunEventType.ToolInvoking, ToolName = "Get_Order" });
        await context.Runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = SeedRunId,
            ToolName = "Get_Order",
            ToolCallId = "call-1",
            CreatedAt = now,
        });

        var schema = context.SchemaName;

        string[] statements =
        [
            $"INSERT INTO {schema}.agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at) " +
            $"VALUES (NEWID(), N'{Tenant}', N'Writer', N'/Notes/Plan.md', N'text', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());",
            $"INSERT INTO {schema}.pending_approvals (id, tenant_id, run_id, session_id, request_id, tool_name, status, expires_at, created_at) " +
            $"VALUES (NEWID(), N'{Tenant}', '{SeedRunId}', N'Session-1', N'Req-1', N'Get_Order', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());",
            $"INSERT INTO {schema}.voice_sessions (id, tenant_id, session_id, agent_name, started_at) " +
            $"VALUES (NEWID(), N'{Tenant}', N'Session-1', N'Writer', SYSDATETIMEOFFSET());",
            $"INSERT INTO {schema}.workflow_requests (run_id, request_id, tenant_id, session_id, workflow_name, port_id, event_seq, requested_at, status) " +
            $"VALUES ('{SeedRunId}', N'Req-1', N'{Tenant}', N'Session-1', N'Review-Flow', N'port', 1, SYSDATETIMEOFFSET(), 0);",
        ];

        foreach (var statement in statements)
        {
            await context.ExecuteAsync(statement);
        }
    }
}
