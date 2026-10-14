using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// 🚨 F-372: the archive mode of retention deletes by each target's key
/// (<c>RetentionTargetDefinition.KeyColumns</c>), not by a second batch.
/// </summary>
/// <remarks>
/// SQL Server port of <c>Tracon.PostgreSql.IntegrationTests.RetentionArchiveKeyTests</c>.
/// The key column <c>key</c> is a reserved word here: it is quoted with square
/// brackets (<c>SqlServerDialect.QuoteIdentifier</c>), and only a run against the
/// real schema proves that. <c>run_events</c> is clustered by <c>(run_id, seq)</c>,
/// whose <c>uniqueidentifier</c> order is not time order (K-180).
/// </remarks>
public sealed class RetentionArchiveKeyTests(SqlServerFixture fixture)
{
    private const string Tenant = "tenant-a";

    private const string EmptyJsonState = "N'{}'";

    private static readonly DateTimeOffset Cutoff = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Every_targets_archive_delete_runs_against_the_schema(string target)
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var dialect = context.StoreContext.Dialect;
        var definition = RetentionTargetRegistry.Resolve(dialect, target);
        var where = SqlDialect.Combine(definition.WherePredicate, definition.TenantPredicate);

        var command = context.StoreContext.CreateCommand(
            dialect.BuildRetentionDeleteKeysSql(definition.Table, where, definition.KeyColumns, rowCount: 2));
        dialect.AddTimestamp(command, "cutoff", Cutoff);
        DbHelpers.AddTenant(command, Tenant);

        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < definition.KeyColumns.Count; column++)
            {
                DbHelpers.Add(
                    command,
                    SqlDialect.RetentionKeyParameterName(row, column),
                    UnmatchedKey(target, definition.KeyColumns[column]));
            }
        }

        (await DbHelpers.ExecuteAsync(command, CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task Run_events_are_archived_and_deleted_by_their_composite_key()
    {
        // Two runs, events written newest first: their time order is the reverse
        // of their write order, and the key has two columns.
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var newest = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var written = 0;

        foreach (var runId in new[] { await SeedRunAsync(context), await SeedRunAsync(context) })
        {
            for (var seq = 0; seq < 3; seq++)
            {
                await SeedRunEventAsync(context, runId, seq, newest.AddMinutes(-written++));
            }
        }

        var archived = new List<string>();
        int deleted;

        do
        {
            var batch = new List<ArchiveRow>();

            deleted = await context.RetentionData.ArchiveBatchAsync(
                RetentionTargets.RunEvents,
                Tenant,
                Cutoff,
                batchSize: 2,
                (rows, _) =>
                {
                    batch.AddRange(rows);

                    return ValueTask.CompletedTask;
                });

            deleted.ShouldBe(batch.Count);
            archived.AddRange(batch.Select(static row => row.Json));
        }
        while (deleted == 2);

        archived.Distinct(StringComparer.Ordinal).Count().ShouldBe(written);
        (await context.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, Tenant, Cutoff)).ShouldBe(0);
    }

    [Fact]
    public async Task A_key_that_holds_the_tenant_is_deleted_tenant_by_tenant()
    {
        // The key of sessions is (tenant_id, id): two tenants can use the same id.
        // An installation-wide archive batch of one row must not delete the other
        // tenant's row, which it never archived.
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "shared-id", "tenant-a");
        await SeedSessionAsync(context, "shared-id", "tenant-b");

        var archived = new List<ArchiveRow>();

        var deleted = await context.RetentionData.ArchiveBatchAsync(
            RetentionTargets.Sessions,
            tenantId: null,
            Cutoff,
            batchSize: 1,
            (rows, _) =>
            {
                archived.AddRange(rows);

                return ValueTask.CompletedTask;
            });

        archived.Count.ShouldBe(1);
        deleted.ShouldBe(1);
        (await context.RetentionData.CountOlderThanAsync(RetentionTargets.Sessions, tenantId: null, Cutoff)).ShouldBe(1);
    }

    /// <summary>
    /// Every target except <c>document_embeddings</c>: that table exists only in
    /// the PostgreSQL migration set, by design (phase 51).
    /// </summary>
    /// <returns>The targets.</returns>
    public static TheoryData<string> AllTargets()
    {
        var data = new TheoryData<string>();

        foreach (var target in RetentionTargets.All)
        {
            if (!string.Equals(target, RetentionTargets.DocumentEmbeddings, StringComparison.Ordinal))
            {
                data.Add(target);
            }
        }

        return data;
    }

    /// <summary>A value of the key column's type that matches no row.</summary>
    private static object UnmatchedKey(string target, string column) => column switch
    {
        "seq" => 0L,
        "key" => "no-such-row",
        "request_id" => "no-such-row",
        "id" when string.Equals(target, RetentionTargets.Sessions, StringComparison.Ordinal) => "no-such-row",
        _ => Guid.NewGuid(),
    };

    private static async Task<Guid> SeedRunAsync(SqlServerTestContext context)
    {
        var id = Guid.NewGuid();

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.runs (id, tenant_id, agent_name, status, started_at, is_streaming, event_count)
            VALUES ('{id}', '{Tenant}', 'support', {(int)RunStatus.Completed}, SYSDATETIMEOFFSET(), 0, 0);
            """);

        return id;
    }

    private static async Task SeedRunEventAsync(SqlServerTestContext context, Guid runId, int seq, DateTimeOffset createdAt)
        => await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.run_events (run_id, seq, type, text, created_at)
            VALUES ('{runId}', {seq}, {(int)RunEventType.RunStarted}, N'event', '{createdAt.UtcDateTime:O}');
            """);

    private static async Task SeedSessionAsync(SqlServerTestContext context, string id, string tenantId)
    {
        var old = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', {EmptyJsonState}, 1, '{old.UtcDateTime:O}', '{old.UtcDateTime:O}');
            """);
    }
}
