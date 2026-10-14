using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// 🚨 F-372: the archive mode of retention deletes by each target's key
/// (<c>RetentionTargetDefinition.KeyColumns</c>), not by a second batch.
/// </summary>
/// <remarks>
/// <c>RetentionStoreContract</c> proves the rule on <c>voice_sessions</c>, whose
/// key is one <c>id</c> column. This class covers what the contract cannot see:
/// every target's delete against the real schema (a key column that does not
/// exist, or the reserved word <c>key</c> left unquoted, fails only at run time),
/// and a composite key (<c>run_events</c>: <c>run_id, seq</c>) bound with the
/// values the read returned.
/// </remarks>
public sealed class RetentionArchiveKeyTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Tenant = "tenant-a";

    private const string EmptyJsonState = "'{}'";

    private static readonly DateTimeOffset Cutoff = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private PostgresTestContext _context = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Every_targets_archive_delete_runs_against_the_schema(string target)
    {
        var dialect = _context.StoreContext.Dialect;
        var definition = RetentionTargetRegistry.Resolve(dialect, target);
        var where = SqlDialect.Combine(definition.WherePredicate, definition.TenantPredicate);

        var command = _context.StoreContext.CreateCommand(
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
        var newest = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var written = 0;

        foreach (var runId in new[] { await SeedRunAsync(), await SeedRunAsync() })
        {
            for (var seq = 0; seq < 3; seq++)
            {
                await SeedRunEventAsync(runId, seq, newest.AddMinutes(-written++));
            }
        }

        var archived = new List<string>();
        int deleted;

        do
        {
            var batch = new List<ArchiveRow>();

            deleted = await _context.RetentionData.ArchiveBatchAsync(
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
        (await _context.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, Tenant, Cutoff)).ShouldBe(0);
    }

    [Fact]
    public async Task A_key_that_holds_the_tenant_is_deleted_tenant_by_tenant()
    {
        // The key of sessions is (tenant_id, id): two tenants can use the same id.
        // An installation-wide archive batch of one row must not delete the other
        // tenant's row, which it never archived.
        await SeedSessionAsync("shared-id", "tenant-a");
        await SeedSessionAsync("shared-id", "tenant-b");

        var archived = new List<ArchiveRow>();

        var deleted = await _context.RetentionData.ArchiveBatchAsync(
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
        (await _context.RetentionData.CountOlderThanAsync(RetentionTargets.Sessions, tenantId: null, Cutoff)).ShouldBe(1);
    }

    /// <summary>Every target; the knowledge set (<c>document_embeddings</c>) is applied in this context.</summary>
    /// <returns>The targets.</returns>
    public static TheoryData<string> AllTargets()
    {
        var data = new TheoryData<string>();

        foreach (var target in RetentionTargets.All)
        {
            data.Add(target);
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

    private async Task<Guid> SeedRunAsync()
    {
        var id = Guid.NewGuid();

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.runs (id, tenant_id, agent_name, status, started_at, is_streaming, event_count)
            VALUES ('{id}', '{Tenant}', 'support', {(int)RunStatus.Completed}, now(), false, 0);
            """);

        return id;
    }

    private async Task SeedRunEventAsync(Guid runId, int seq, DateTimeOffset createdAt)
        => await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.run_events (run_id, seq, type, text, created_at)
            VALUES ('{runId}', {seq}, {(int)RunEventType.RunStarted}, 'event', '{createdAt.UtcDateTime:O}');
            """);

    private async Task SeedSessionAsync(string id, string tenantId)
    {
        var old = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', {EmptyJsonState}, 1, '{old.UtcDateTime:O}', '{old.UtcDateTime:O}');
            """);
    }
}
