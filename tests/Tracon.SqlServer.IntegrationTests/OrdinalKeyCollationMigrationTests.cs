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
/// The stores seed the old schema: none of them reads the collation, so the same calls that a deployed
/// release makes write the rows here.
/// </para>
/// </remarks>
public sealed class OrdinalKeyCollationMigrationTests(SqlServerFixture fixture)
{
    private const string Tenant = "default";

    private const string Ordinal = "Latin1_General_BIN2";

    private static readonly DateTimeOffset Window = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rows_written_before_the_upgrade_keep_their_count_and_their_values()
    {
        await using var context = await OpenOldSchemaAsync();

        var before = await RowCountsAsync(context);

        before.Values.Sum().ShouldBeGreaterThan(0, "the seed has to put rows in the tables the migration rebuilds");

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
        (await context.ScalarAsync<int>(
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

    private static readonly Guid RunId = new("11111111-2222-3333-4444-555555555555");

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
        => context.ScalarAsync<string>(
            $"SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'{context.SchemaName}.{table}') AND name = N'{column}';");

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

        return context;
    }
}
