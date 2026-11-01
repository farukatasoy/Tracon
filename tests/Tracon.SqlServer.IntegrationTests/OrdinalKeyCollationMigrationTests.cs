using System.Text.Json;
using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The keys a client chooses compare ordinally: applied over rows written under the case-insensitive
/// comparison, and stopped halfway.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 A migration that drops a key, changes the column under it and builds the key again can lose a row
/// or a value only on rows that already exist. No compiler and no SQL text snapshot sees migration SQL, so
/// the upgrade runs against rows the older release wrote.
/// </para>
/// <para>
/// The stores seed the old schema where the context has one: none of them reads the collation, so the same
/// calls that a deployed release makes write the rows. The tables without a store here (attachments,
/// approvals, voice sessions, workflow requests and responses, runs) get a row of plain SQL, so that no table
/// the migration rebuilds is empty when it runs.
/// </para>
/// </remarks>
public sealed class OrdinalKeyCollationMigrationTests(SqlServerFixture fixture)
{
    private const string Tenant = "default";

    private const string Ordinal = "Latin1_General_BIN2";

    private static readonly DateTimeOffset Window = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Every key and index the migration drops and builds again.</summary>
    private static readonly string[] RebuiltObjects =
    [
        "sessions_pk", "runs_session_idx", "attachments_session_idx", "workflow_checkpoints_session_idx",
        "workflow_checkpoints_uq", "workflow_requests_pk", "workflow_requests_logical_idx",
        "workflow_responses_claim_idx", "idempotency_keys_pk", "agent_files_tenant_agent_path_uq",
        "agent_files_tenant_path_idx", "run_scores_target_author_name_idx", "rate_limit_counters_pk",
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
        (await context.Sessions.GetAsync("Alpha")).ShouldNotBeNull().Id.ShouldBe("Alpha");
        (await context.Sessions.GetAsync("beta")).ShouldNotBeNull().Id.ShouldBe("beta");

        (await context.IdempotencyKeys.ReserveAsync(Reservation("Key-One"))).State.ShouldBe(IdempotencyState.InProgress);

        (await context.RateLimitCounters.IncrementAsync("endpoint:Tracon:tenant:Default", Window)).Count.ShouldBe(3);

        (await context.WorkflowCheckpoints.ReadAsync(Tenant, "Flow-Session", "Checkpoint-A")).ShouldNotBeNull();

        var score = (await context.RunScores.ListAsync(Tenant, RunId)).ShouldHaveSingleItem();

        score.Author.ShouldBe("Alice");
        score.MessageId.ShouldBe("Msg-1");
        score.Name.ShouldBe("Quality");

        (await context.ScalarAsync<string>(
            $"SELECT path FROM {context.SchemaName}.agent_files;")).ShouldBe("/Notes/Plan.md");
    }

    [Fact]
    public async Task The_keys_of_the_session_family_compare_ordinally_once_the_migration_ran()
    {
        await using var context = await OpenOldSchemaAsync();

        // Before: the old comparison folds the case, so a second spelling is the first session.
        AssertCollation(await CollationOfAsync(context, "sessions", "id"), ordinal: false, "the old comparison folds the case");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var (table, column) in new[]
        {
            ("sessions", "id"), ("runs", "session_id"), ("attachments", "session_id"), ("pending_approvals", "session_id"),
            ("responses", "session_id"), ("voice_sessions", "session_id"), ("workflow_checkpoints", "session_id"),
            ("workflow_requests", "session_id"), ("workflow_responses", "session_id"),
        })
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: true, $"{table}.{column} carries a session id");
        }

        // After: the spellings are two sessions.
        await context.Sessions.SaveAsync(TestData.Session("ALPHA"));

        (await context.Sessions.GetAsync("Alpha")).ShouldNotBeNull().Id.ShouldBe("Alpha");
        (await context.Sessions.GetAsync("ALPHA")).ShouldNotBeNull().Id.ShouldBe("ALPHA");
        (await RowCountAsync(context, "sessions")).ShouldBe(3);
    }

    [Fact]
    public async Task A_migration_that_fails_on_its_last_table_leaves_the_old_shape_everywhere()
    {
        await using var context = await OpenOldSchemaAsync();

        // A schema-bound view makes the change of the last column impossible, after every earlier table was
        // already rebuilt inside the same transaction.
        await context.ExecuteAsync(
            $"CREATE VIEW {context.SchemaName}.rate_limit_partitions WITH SCHEMABINDING AS SELECT partition_key FROM {context.SchemaName}.rate_limit_counters;");

        await Should.ThrowAsync<TraconException>(async () => await context.Migrations.ApplyAsync());

        AssertCollation(await CollationOfAsync(context, "sessions", "id"), ordinal: false, "the first table rolled back with the last");
        AssertCollation(await CollationOfAsync(context, "run_scores", "name"), ordinal: false, "the last table before the failure rolled back too");
        (await context.CatalogScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.key_constraints WHERE name = N'sessions_pk' AND parent_object_id = OBJECT_ID(N'{context.SchemaName}.sessions');"))
            .ShouldBe(1, "the key the migration dropped is back");
        (await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.__migrations WHERE name LIKE N'%ordinal_key_collation%';"))
            .ShouldBe(0, "a failed migration is not in the ledger");

        // Nothing is half done: the next run, without the view, goes through.
        await context.ExecuteAsync($"DROP VIEW {context.SchemaName}.rate_limit_partitions;");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        AssertCollation(await CollationOfAsync(context, "sessions", "id"), ordinal: true, "the retry went through");
        AssertCollation(await CollationOfAsync(context, "rate_limit_counters", "partition_key"), ordinal: true, "the retry went through");
    }

    [Fact]
    public async Task The_migration_can_run_again_over_its_own_result()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var migration = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.")
            .Single(static migration => migration.Name.Contains("ordinal_key_collation", StringComparison.Ordinal));

        var before = await RowCountsAsync(context);

        // A run that wrote its work but not its ledger line starts over from the top.
        await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));

        (await RowCountsAsync(context)).ShouldBe(before);
        AssertCollation(await CollationOfAsync(context, "run_scores", "message_key"), ordinal: true, "the computed column follows message_id");
        (await context.Sessions.GetAsync("Alpha")).ShouldNotBeNull();
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
    public async Task A_view_over_the_runs_reports_the_new_collation_of_the_session_id()
    {
        await using var context = await OpenOldSchemaAsync();

        // A view that is not schema-bound copies the collation of its columns when it is created. The read
        // contract view is named like this one.
        await context.ExecuteAsync($"CREATE VIEW {context.SchemaName}.runs_v1 AS SELECT id, session_id FROM {context.SchemaName}.runs;");

        AssertCollation(await CollationOfAsync(context, "runs_v1", "session_id"), ordinal: false, "the view was made over the old column");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        AssertCollation(await CollationOfAsync(context, "runs_v1", "session_id"), ordinal: true, "the migration refreshed the view");
    }

    private static readonly Guid RunId = new("11111111-2222-3333-4444-555555555555");

    private static readonly Guid SeedRunId = new("aaaaaaaa-2222-3333-4444-555555555555");

    private static IdempotencyRequest Reservation(string key)
        => new()
        {
            TenantId = Tenant,
            Key = key,
            Fingerprint = "fp-a",
            CreatedAt = DateTimeOffset.UtcNow,
        };

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

    /// <summary>The row count of every table the migration rebuilds.</summary>
    private static async ValueTask<SortedDictionary<string, long>> RowCountsAsync(SqlServerTestContext context)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);

        foreach (var table in new[]
        {
            "sessions", "runs", "attachments", "pending_approvals", "responses", "voice_sessions", "workflow_checkpoints",
            "workflow_requests", "workflow_responses", "idempotency_keys", "agent_files", "run_scores", "rate_limit_counters",
        })
        {
            counts[table] = await RowCountAsync(context, table);
        }

        return counts;
    }

    /// <summary>
    /// Builds the schema as it was before the migration and writes into it the way the older release did:
    /// two sessions, a reservation, a counter, a checkpoint, a score and a file whose names carry capitals.
    /// </summary>
    private async Task<SqlServerTestContext> OpenOldSchemaAsync()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        var context = SqlServerTestContext.Create(fixture, schemaName);
        var all = MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.");
        var target = all.Single(static migration => migration.Name.Contains("ordinal_key_collation", StringComparison.Ordinal)).Id;
        var before = all.Where(migration => migration.Id < target).OrderBy(migration => migration.Id).ToArray();

        before.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");
        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in before)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {schemaName}.__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', SYSDATETIMEOFFSET());");
        }

        await context.Sessions.SaveAsync(TestData.Session("Alpha"));
        await context.Sessions.SaveAsync(TestData.Session("beta"));

        await context.IdempotencyKeys.ReserveAsync(Reservation("Key-One"));

        await context.RateLimitCounters.IncrementAsync("endpoint:Tracon:tenant:Default", Window);
        await context.RateLimitCounters.IncrementAsync("endpoint:Tracon:tenant:Default", Window);

        await context.WorkflowCheckpoints.CreateAsync(new WorkflowCheckpointRecord
        {
            Id = TraconId.NewId(),
            TenantId = Tenant,
            SessionId = "Flow-Session",
            CheckpointId = "Checkpoint-A",
            CreatedAt = DateTimeOffset.UtcNow,
            State = JsonDocument.Parse("""{"stepNumber":1}""").RootElement.Clone(),
        });

        await context.RunScores.UpsertAsync(new RunScore
        {
            TenantId = Tenant,
            RunId = RunId,
            MessageId = "Msg-1",
            Name = "Quality",
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            Author = "Alice",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.ExecuteAsync(
            $"INSERT INTO {schemaName}.agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at) " +
            $"VALUES (NEWID(), N'{Tenant}', N'writer', N'/Notes/Plan.md', N'text', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());");

        await SeedSessionFamilyAsync(context);

        return context;
    }

    /// <summary>
    /// A row in every table that holds a session id and has no store in the test context: a run with a session
    /// and one without, an attachment, an approval, a response, a voice session, and a workflow request and
    /// response whose claim sits in the filtered unique index.
    /// </summary>
    private static async ValueTask SeedSessionFamilyAsync(SqlServerTestContext context)
    {
        var schema = context.SchemaName;
        var run = SeedRunId;

        string[] statements =
        [
            $"INSERT INTO {schema}.runs (id, tenant_id, agent_name, session_id, status, started_at, is_streaming) " +
            $"VALUES ('{run}', N'{Tenant}', N'writer', N'Flow-Session', 2, SYSDATETIMEOFFSET(), 0), (NEWID(), N'{Tenant}', N'writer', NULL, 2, SYSDATETIMEOFFSET(), 0);",
            $"INSERT INTO {schema}.attachments (id, tenant_id, session_id, file_name, media_type, byte_size, sha256, created_at) " +
            $"VALUES (NEWID(), N'{Tenant}', N'Flow-Session', N'a.txt', N'text/plain', 1, N'00', SYSDATETIMEOFFSET());",
            $"INSERT INTO {schema}.pending_approvals (id, tenant_id, run_id, session_id, request_id, tool_name, status, expires_at, created_at) " +
            $"VALUES (NEWID(), N'{Tenant}', '{run}', N'Flow-Session', N'Req-1', N'tool', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());",
            $"INSERT INTO {schema}.responses (id, session_id, payload, created_at) VALUES (NEWID(), N'Flow-Session', N'{{}}', SYSDATETIMEOFFSET());",
            $"INSERT INTO {schema}.voice_sessions (id, tenant_id, session_id, agent_name, started_at) " +
            $"VALUES (NEWID(), N'{Tenant}', N'Flow-Session', N'writer', SYSDATETIMEOFFSET());",
            $"INSERT INTO {schema}.workflow_requests (run_id, request_id, tenant_id, session_id, workflow_name, port_id, event_seq, requested_at, status) " +
            $"VALUES ('{run}', N'Req-1', N'{Tenant}', N'Flow-Session', N'flow', N'port', 1, SYSDATETIMEOFFSET(), 0);",
            $"INSERT INTO {schema}.workflow_responses (response_run_id, tenant_id, source_run_id, session_id, request_id, claim_scope, answer_hash, status, responded_at) " +
            $"VALUES (NEWID(), N'{Tenant}', '{run}', N'Flow-Session', N'Req-1', N'Claim-1', REPLICATE('0', 64), 0, SYSDATETIMEOFFSET());",
        ];

        foreach (var statement in statements)
        {
            await context.ExecuteAsync(statement);
        }
    }
}
