using AgentPrism.Sqlite.IntegrationTests.Infrastructure;

namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>
/// Tests of the SQLite implementation of
/// <see cref="IRetentionStore.FindRowLimitCutoffAsync"/> against a real
/// database (Phase 36, <c>MaxRows</c>). Covers the SAME three scenarios as
/// <c>RetentionDataPlaneTests</c> (AgentPrism.PostgreSql.IntegrationTests) for
/// PostgreSQL; proves behavioral equality.
/// </summary>
public sealed class RetentionMaxRowsDataPlaneTests(SqliteFixture fixture) : IAsyncLifetime
{
    private SqliteTestContext _context = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await SqliteTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task MaxRows_cutoff_is_computed_correctly_and_leaves_the_target_at_N_rows()
    {
        var runId = await SeedRunAsync();

        for (var i = 0; i < 10; i++)
        {
            await SeedRunEventAsync(runId, seq: i, createdAt: DateTimeOffset.UtcNow.AddMinutes(-10 + i));
        }

        var cutoff = await _context.RetentionData.FindRowLimitCutoffAsync(RetentionTargets.RunEvents, tenantId: null, maxRows: 4);

        cutoff.ShouldNotBeNull();

        var deleted = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, cutoff!.Value, batchSize: 100);

        deleted.ShouldBe(6);

        var remaining = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.TablePrefix}run_events WHERE run_id = '{runId}';");

        remaining.ShouldBe(4);
    }

    [Fact]
    public async Task MaxRows_cutoff_returns_null_when_the_table_is_below_the_limit()
    {
        var runId = await SeedRunAsync();
        await SeedRunEventAsync(runId, seq: 1, createdAt: DateTimeOffset.UtcNow);

        var cutoff = await _context.RetentionData.FindRowLimitCutoffAsync(RetentionTargets.RunEvents, tenantId: null, maxRows: 1000);

        cutoff.ShouldBeNull();
    }

    /// <summary>
    /// For <c>workflow_checkpoints</c>, the cutoff is computed not from the
    /// target's OWN column but from the LINKED RUN's <c>completed_at</c> (a
    /// correlated subquery) — see
    /// <c>RetentionTargetRegistry.RowLimitOrderExpression</c>.
    /// </summary>
    [Fact]
    public async Task MaxRows_cutoff_is_computed_correctly_through_a_related_table()
    {
        var run1 = await SeedRunAsync(completedAt: DateTimeOffset.UtcNow.AddDays(-10));
        var run2 = await SeedRunAsync(completedAt: DateTimeOffset.UtcNow.AddDays(-5));
        var run3 = await SeedRunAsync(completedAt: DateTimeOffset.UtcNow);

        await SeedWorkflowCheckpointAsync(run1);
        await SeedWorkflowCheckpointAsync(run2);
        await SeedWorkflowCheckpointAsync(run3);

        var cutoff = await _context.RetentionData.FindRowLimitCutoffAsync(RetentionTargets.WorkflowCheckpoints, tenantId: null, maxRows: 1);

        cutoff.ShouldNotBeNull();

        var deleted = await _context.RetentionData.DeleteBatchAsync(
            RetentionTargets.WorkflowCheckpoints, tenantId: null,
            cutoff!.Value,
            batchSize: 100);

        deleted.ShouldBe(2);
    }

    /// <summary>
    /// 🚨 A side finding from Phase 36: the <c>WherePredicate</c> for
    /// <c>attachments</c> (Phase 25) used the BARE target name ("attachments")
    /// as the correlation; in SQLite the actual FROM'd object is prefixed
    /// ("t_...attachments") and the bare name never matched — <c>NOT EXISTS</c>
    /// always evaluated to TRUE, and owned attachments were also considered
    /// eligible for deletion. This test carries the evidence for the fix in
    /// the registry (fully qualified correlation).
    /// </summary>
    [Fact]
    public async Task Attachments_owned_attachment_is_kept_orphaned_attachment_is_deleted()
    {
        var sessionId = Guid.NewGuid().ToString();
        const string emptyJsonState = "'{}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at)
            VALUES ('{sessionId}', 'test', 'support', {emptyJsonState}, 1, '{Iso(DateTimeOffset.UtcNow)}', '{Iso(DateTimeOffset.UtcNow)}');
            """);

        await SeedAttachmentAsync(sessionId: sessionId);
        await SeedAttachmentAsync(sessionId: null);

        var cutoff = DateTimeOffset.UtcNow.AddDays(1);
        var deleted = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.Attachments, tenantId: null, cutoff, batchSize: 100);

        deleted.ShouldBe(1);

        var remaining = await _context.ScalarAsync<long>($"SELECT COUNT(*) FROM {_context.TablePrefix}attachments;");

        remaining.ShouldBe(1);
    }

    private async Task SeedAttachmentAsync(string? sessionId)
    {
        var sessionSql = sessionId is null ? "NULL" : $"'{sessionId}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}attachments
                (id, tenant_id, session_id, file_name, media_type, byte_size, sha256, created_at)
            VALUES
                ('{Guid.NewGuid()}', 'test', {sessionSql}, 'a.txt', 'text/plain', 1, 'x', '{Iso(DateTimeOffset.UtcNow.AddDays(-1))}');
            """);
    }

    private async Task<Guid> SeedRunAsync(DateTimeOffset? completedAt = null)
    {
        var id = Guid.NewGuid();
        var completedSql = completedAt is { } value ? $"'{Iso(value)}'" : "NULL";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}runs
                (id, tenant_id, agent_name, status, started_at, completed_at, is_streaming, event_count)
            VALUES
                ('{id}', 'test', 'support', 1, '{Iso(DateTimeOffset.UtcNow.AddHours(-1))}', {completedSql}, 0, 0);
            """);

        return id;
    }

    private async Task SeedRunEventAsync(Guid runId, int seq, DateTimeOffset createdAt)
        => await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}run_events (run_id, seq, type, text, created_at)
            VALUES ('{runId}', {seq}, 0, 'event', '{Iso(createdAt)}');
            """);

    private async Task SeedWorkflowCheckpointAsync(Guid runId)
    {
        const string emptyJsonState = "'{}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}workflow_checkpoints
                (id, tenant_id, session_id, checkpoint_id, run_id, state, created_at)
            VALUES
                ('{Guid.NewGuid()}', 'test', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{runId}', {emptyJsonState}, '{Iso(DateTimeOffset.UtcNow)}');
            """);
    }

    private static string Iso(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
