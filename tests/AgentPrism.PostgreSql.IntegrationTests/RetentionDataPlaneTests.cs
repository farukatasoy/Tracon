using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// The counting/deletion/archive-read behavior of the PostgreSQL
/// implementation of <see cref="IRetentionStore"/>. Meets the real evidence
/// requirement in Phase 25's DoD: old rows are dropped, while the <c>runs</c>
/// summary and <c>audit_log</c> are PRESERVED.
/// </summary>
public sealed class RetentionDataPlaneTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext _context = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Old_run_events_are_deleted_new_ones_remain()
    {
        var runId = await SeedRunAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        await SeedRunEventAsync(runId, seq: 1, createdAt: cutoff.AddDays(-5));
        await SeedRunEventAsync(runId, seq: 2, createdAt: cutoff.AddDays(-1));
        await SeedRunEventAsync(runId, seq: 3, createdAt: cutoff.AddDays(5));

        (await _context.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, tenantId: null, cutoff)).ShouldBe(2);

        var deleted = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, cutoff, batchSize: 100);

        deleted.ShouldBe(2);

        var remaining = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.run_events WHERE run_id = '{runId}';");

        remaining.ShouldBe(1);
    }

    [Fact]
    public async Task Delete_works_batch_by_batch()
    {
        var runId = await SeedRunAsync();
        var cutoff = DateTimeOffset.UtcNow;

        for (var i = 0; i < 25; i++)
        {
            await SeedRunEventAsync(runId, seq: i, createdAt: cutoff.AddDays(-1));
        }

        var firstBatch = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, cutoff, batchSize: 10);
        var secondBatch = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, cutoff, batchSize: 10);
        var thirdBatch = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, cutoff, batchSize: 10);
        var fourthBatch = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, cutoff, batchSize: 10);

        firstBatch.ShouldBe(10);
        secondBatch.ShouldBe(10);
        thirdBatch.ShouldBe(5);
        fourthBatch.ShouldBe(0);
    }

    [Fact]
    public async Task Runs_summary_is_preserved_while_run_events_are_deleted()
    {
        var runId = await SeedRunAsync();
        await SeedRunEventAsync(runId, seq: 1, createdAt: DateTimeOffset.UtcNow.AddDays(-60));

        await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, DateTimeOffset.UtcNow, batchSize: 100);

        var runStillExists = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.runs WHERE id = '{runId}';");

        runStillExists.ShouldBe(1);
    }

    [Fact]
    public async Task Retention_audit_log_target_is_not_whitelisted_and_is_never_deleted()
    {
        var before = await _context.ScalarAsync<long>($"SELECT COUNT(*) FROM {_context.SchemaName}.audit_log;");

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.audit_log (id, tenant_id, actor, action, entity, created_at)
            VALUES (gen_random_uuid(), 'test', 'test-actor', 'agent.create', 'agent:demo', now() - interval '400 days');
            """);

        // 'audit_log' is NOT in the RetentionTargets whitelist; IRetentionStore
        // throws ArgumentException for an unknown target — this proves that even
        // attempting it is impossible.
        await Should.ThrowAsync<ArgumentException>(async ()
            => await _context.RetentionData.CountOlderThanAsync("audit_log", tenantId: null, DateTimeOffset.UtcNow));

        var after = await _context.ScalarAsync<long>($"SELECT COUNT(*) FROM {_context.SchemaName}.audit_log;");

        after.ShouldBe(before + 1);
    }

    [Fact]
    public async Task Archive_read_returns_rows_as_JSON_and_does_not_delete()
    {
        var runId = await SeedRunAsync();
        await SeedRunEventAsync(runId, seq: 1, createdAt: DateTimeOffset.UtcNow.AddDays(-60), text: "hello");

        var cutoff = DateTimeOffset.UtcNow;
        var rows = await _context.RetentionData.ReadForArchiveAsync(RetentionTargets.RunEvents, tenantId: null, cutoff, batchSize: 10);

        rows.Count.ShouldBe(1);
        rows[0].Json.ShouldContain("hello");
        rows[0].Json.ShouldContain(runId.ToString());

        // Reading does NOT DELETE.
        (await _context.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, tenantId: null, cutoff)).ShouldBe(1);
    }

    [Fact]
    public async Task Completed_job_is_deleted_pending_job_remains()
    {
        var pendingId = await SeedJobAsync(status: 0, completedAt: null);
        var completedId = await SeedJobAsync(status: 3, completedAt: DateTimeOffset.UtcNow.AddDays(-60));

        var deleted = await _context.RetentionData.DeleteBatchAsync(
            RetentionTargets.Jobs, tenantId: null,
            DateTimeOffset.UtcNow.AddDays(-30),
            batchSize: 100);

        deleted.ShouldBe(1);

        var pendingExists = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.jobs WHERE id = '{pendingId}';");
        var completedExists = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.jobs WHERE id = '{completedId}';");

        pendingExists.ShouldBe(1);
        completedExists.ShouldBe(0);
    }

    /// <summary>
    /// 🚨 Phase 36's core evidence requirement: the <c>MaxRows</c> threshold is
    /// computed correctly from the OWN column of the Nth row, counting from the
    /// newest, and feeding that threshold into the EXISTING batch deletion
    /// mechanism leaves the table at exactly N rows.
    /// </summary>
    [Fact]
    public async Task MaxRows_threshold_is_computed_correctly_and_leaves_the_target_at_N_rows()
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
            $"SELECT COUNT(*) FROM {_context.SchemaName}.run_events WHERE run_id = '{runId}';");

        remaining.ShouldBe(4);
    }

    [Fact]
    public async Task MaxRows_threshold_returns_null_when_the_table_is_below_the_limit()
    {
        var runId = await SeedRunAsync();
        await SeedRunEventAsync(runId, seq: 1, createdAt: DateTimeOffset.UtcNow);

        var cutoff = await _context.RetentionData.FindRowLimitCutoffAsync(RetentionTargets.RunEvents, tenantId: null, maxRows: 1000);

        cutoff.ShouldBeNull();
    }

    /// <summary>
    /// For <c>workflow_checkpoints</c>, the MaxRows threshold is computed not
    /// from the target's OWN column but from the LINKED RUN's
    /// <c>completed_at</c> (a correlated subquery) —
    /// <see cref="RetentionTargetRegistry"/>'s <c>RowLimitOrderExpression</c>.
    /// This must be the same column the WherePredicate actually compares
    /// against; otherwise the threshold would delete no rows.
    /// </summary>
    [Fact]
    public async Task MaxRows_threshold_is_computed_correctly_via_a_related_table()
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

        // Only the checkpoints belonging to run1 and run2 are OLDER than run3
        // (the newest) and satisfy the wherePredicate's EXISTS condition.
        deleted.ShouldBe(2);
    }

    private async Task<Guid> SeedRunAsync(DateTimeOffset? completedAt = null)
    {
        var id = Guid.NewGuid();
        var completedSql = completedAt is { } value ? $"'{value.UtcDateTime:O}'" : "NULL";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.runs
                (id, tenant_id, agent_name, status, started_at, completed_at, is_streaming, event_count)
            VALUES
                ('{id}', 'test', 'support', 1, now() - interval '1 hour', {completedSql}, false, 0);
            """);

        return id;
    }

    private async Task SeedWorkflowCheckpointAsync(Guid runId)
    {
        const string emptyJsonState = "'{}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.workflow_checkpoints
                (id, tenant_id, session_id, checkpoint_id, run_id, state, created_at)
            VALUES
                (gen_random_uuid(), 'test', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{runId}', {emptyJsonState}, now());
            """);
    }

    private async Task SeedRunEventAsync(Guid runId, int seq, DateTimeOffset createdAt, string text = "event")
        => await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.run_events (run_id, seq, type, text, created_at)
            VALUES ('{runId}', {seq}, 0, '{text}', '{createdAt.UtcDateTime:O}');
            """);

    private async Task<Guid> SeedJobAsync(short status, DateTimeOffset? completedAt)
    {
        var id = Guid.NewGuid();
        var completedSql = completedAt is { } value ? $"'{value.UtcDateTime:O}'" : "NULL";
        const string emptyJsonPayload = "'{}'::jsonb";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.jobs
                (id, tenant_id, handler_key, target_name, status, payload, scheduled_for, completed_at, created_at)
            VALUES
                ('{id}', 'test', '{JobHandlerKeys.AgentBatch}', 'support', {status}, {emptyJsonPayload}, now(), {completedSql}, now());
            """);

        return id;
    }
}
