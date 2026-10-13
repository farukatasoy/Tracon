using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The tenant isolation, cascade, and commit/rollback behavior of the SQL
/// Server implementation of <see cref="IDataSubjectStore"/> (phase 64).
/// </summary>
/// <remarks>
/// SQL Server port of <c>Tracon.PostgreSql.IntegrationTests.DataSubjectStoreTests</c>
/// — the same scenarios, run against <c>SqlDataSubjectStore</c>'s
/// <c>OPENJSON</c>-based <see cref="SqlDialect.ArrayContains"/> implementation
/// (K-198: shared code, but the dialect text is genuinely provider-specific and
/// untested on this provider until this file existed — see the phase 64 audit
/// finding that asked for this).
/// </remarks>
public sealed class DataSubjectStoreTests(SqlServerFixture fixture)
{
    private static readonly string EmptyJsonState = "N'{}'";

    /// <summary>A checkpoint state that carries a subject's own words.</summary>
    private const string CheckpointState = """{"$type":0,"messages":["my own words"]}""";

    [Fact]
    public async Task Erase_removes_only_the_named_tenants_session_even_when_ids_collide()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        const string SessionId = "shared-session-id";
        await SeedSessionAsync(context, SessionId, "tenant-a");
        await SeedSessionAsync(context, SessionId, "tenant-b");

        await context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(sessionIds: [SessionId]),
            static (_, _) => ValueTask.CompletedTask);

        (await SessionExistsAsync(context, SessionId, "tenant-a")).ShouldBeFalse();
        (await SessionExistsAsync(context, SessionId, "tenant-b")).ShouldBeTrue();
    }

    [Fact]
    public async Task Erase_cascades_from_run_to_run_inputs()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var runId = await SeedRunAsync(context, "tenant-a");
        await SeedRunInputAsync(context, runId, "tenant-a");

        var deleted = await context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(runIds: [runId]),
            static (_, _) => ValueTask.CompletedTask);

        deleted[DataSubjectTargetRegistry.Runs].ShouldBe(1);
        (await RunExistsAsync(context, runId)).ShouldBeFalse();
        (await RunInputExistsAsync(context, runId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Erase_removes_a_conversation_and_its_items_together()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var conversationId = await SeedConversationAsync(context, "tenant-a");
        await SeedConversationItemAsync(context, conversationId, seq: 1);
        await SeedConversationItemAsync(context, conversationId, seq: 2);

        await context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(conversationIds: [conversationId]),
            static (_, _) => ValueTask.CompletedTask);

        (await ConversationExistsAsync(context, conversationId)).ShouldBeFalse();
        (await ConversationItemCountAsync(context, conversationId)).ShouldBe(0);
    }

    [Fact]
    public async Task Preview_reports_counts_without_deleting_anything()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        const string SessionId = "preview-session";
        await SeedSessionAsync(context, SessionId, "tenant-a");

        var preview = await context.DataSubjects.PreviewAsync("tenant-a", Scope(sessionIds: [SessionId]));

        preview[DataSubjectTargetRegistry.Sessions].ShouldBe(1);
        (await SessionExistsAsync(context, SessionId, "tenant-a")).ShouldBeTrue();
    }

    [Fact]
    public async Task Erase_rolls_back_every_delete_when_the_commit_callback_throws()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        const string SessionId = "rollback-session";
        await SeedSessionAsync(context, SessionId, "tenant-a");

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await context.DataSubjects.EraseAsync(
                "tenant-a",
                Scope(sessionIds: [SessionId]),
                static (_, _) => throw new InvalidOperationException("audit write failed")));

        (await SessionExistsAsync(context, SessionId, "tenant-a")).ShouldBeTrue();
    }

    [Fact]
    public async Task Export_does_not_include_another_tenants_data()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        const string SessionId = "export-session";
        await SeedSessionAsync(context, SessionId, "tenant-a");
        await SeedSessionAsync(context, SessionId, "tenant-b");

        var export = await context.DataSubjects.ExportAsync("tenant-a", Scope(sessionIds: [SessionId]));

        // 🚨 The document ALWAYS carries a top-level "tenantId" field (the
        // caller's own tenant), so asserting on the raw text alone would pass
        // even if the "sessions" array came back empty — parse it and check
        // the array itself.
        using var document = System.Text.Json.JsonDocument.Parse(export.Json);
        var sessions = document.RootElement.GetProperty("sessions");

        sessions.GetArrayLength().ShouldBe(1);
        sessions[0].GetProperty("tenant_id").GetString().ShouldBe("tenant-a");
    }

    [Fact]
    public async Task Erase_removes_the_workflow_checkpoints_of_the_subjects_sessions_and_runs()
    {
        // 🚨 F-371: workflow_checkpoints carries tenant_id, session_id and run_id but
        // no foreign key to runs, so no cascade reaches it — and its state is the
        // serialized workflow, which can hold the subject's own messages. The run
        // waits for input: erasure is a right, not retention, so K-888's "keep the
        // checkpoints of a waiting run" does not apply — the run row goes too.
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var waitingRunId = await SeedRunAsync(context, "tenant-a", status: (int)RunStatus.AwaitingInput);
        await SeedCheckpointAsync(context, "tenant-a", "wf-session-a", runId: null);
        await SeedCheckpointAsync(context, "tenant-a", "wf-session-b", waitingRunId);
        await SeedCheckpointAsync(context, "tenant-a", "wf-session-c", Guid.NewGuid());
        await SeedCheckpointAsync(context, "tenant-b", "wf-session-a", runId: null);

        var deleted = await context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(sessionIds: ["wf-session-a"], runIds: [waitingRunId]),
            static (_, _) => ValueTask.CompletedTask);

        (await CheckpointCountAsync(context, "tenant-a", "wf-session-a")).ShouldBe(0);
        (await CheckpointCountAsync(context, "tenant-a", "wf-session-b")).ShouldBe(0);
        (await CheckpointCountAsync(context, "tenant-a", "wf-session-c")).ShouldBe(1);
        (await CheckpointCountAsync(context, "tenant-b", "wf-session-a")).ShouldBe(1);
        (await RunExistsAsync(context, waitingRunId)).ShouldBeFalse();
        deleted["workflow_checkpoints"].ShouldBe(2);
    }

    [Fact]
    public async Task Export_includes_the_workflow_checkpoints_with_their_state_as_stored()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        await SeedCheckpointAsync(context, "tenant-a", "wf-session-a", runId: null, state: CheckpointState);
        await SeedCheckpointAsync(context, "tenant-b", "wf-session-a", runId: null);

        var export = await context.DataSubjects.ExportAsync("tenant-a", Scope(sessionIds: ["wf-session-a"]));

        using var document = System.Text.Json.JsonDocument.Parse(export.Json);
        var checkpoints = document.RootElement.GetProperty("workflow_checkpoints");

        checkpoints.GetArrayLength().ShouldBe(1);
        checkpoints[0].GetProperty("tenant_id").GetString().ShouldBe("tenant-a");

        // The state is opaque Microsoft Agent Framework data: it is exported as the
        // stored JSON text (a string), the same as sessions.state, never parsed.
        checkpoints[0].GetProperty("state").GetString().ShouldBe(CheckpointState);
    }

    [Fact]
    public async Task Erase_and_export_reach_the_scores_of_runs_found_through_a_session()
    {
        // F-371 class sweep: run_scores has no foreign key to runs, so no cascade
        // reaches it. A resolver that named only a session reached the session's
        // runs but not their scores — and a score's comment can hold the
        // subject's own words.
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var runId = await SeedRunAsync(context, "tenant-a", sessionId: "scored-session");
        await context.RunScores.UpsertAsync(Score(runId, "tenant-a"));

        var export = await context.DataSubjects.ExportAsync("tenant-a", Scope(sessionIds: ["scored-session"]));

        using (var document = System.Text.Json.JsonDocument.Parse(export.Json))
        {
            document.RootElement.GetProperty("run_scores").GetArrayLength().ShouldBe(1);
        }

        var deleted = await context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(sessionIds: ["scored-session"]),
            static (_, _) => ValueTask.CompletedTask);

        (await context.RunScores.ListAsync("tenant-a", runId)).ShouldBeEmpty();
        deleted["run_scores"].ShouldBe(1);
    }

    /// <summary>
    /// F-400: a promoted eval case, an idempotent run's stored response, a queued
    /// run's job and a batch item each keep a copy of the subject's text, and none
    /// had a foreign key that cascaded from runs. They are reached through a run of
    /// the named session, never in another tenant, and a batch job keeps its other items.
    /// </summary>
    [Fact]
    public async Task Erase_and_export_reach_the_promoted_case_the_stored_response_the_queued_job_and_the_batch_item()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var runId = await SeedRunAsync(context, "tenant-a", sessionId: "subject-session");
        var otherRunId = await SeedRunAsync(context, "tenant-a", sessionId: "other-session");

        var suiteA = await SeedSuiteAsync(context, "tenant-a");
        var suiteB = await SeedSuiteAsync(context, "tenant-b");
        await context.Evals.AddCaseAsync(suiteA, new EvalCaseDraft { Query = "promoted-words", SourceRunId = runId, SourceKind = EvalCaseSource.ReferenceRun });
        await context.Evals.AddCaseAsync(suiteA, new EvalCaseDraft { Query = "hand-written case" });
        await context.Evals.AddCaseAsync(suiteB, new EvalCaseDraft { Query = "other tenant", SourceRunId = runId, SourceKind = EvalCaseSource.ReferenceRun });

        await SeedIdempotentResponseAsync(context, "tenant-a", "key-a", runId);
        await SeedIdempotentResponseAsync(context, "tenant-b", "key-b", runId);

        await SeedJobAsync(context, "tenant-a", runId, items: []);
        var batchId = await SeedJobAsync(context, "tenant-a", Guid.NewGuid(), items: ["item-words", "other item"]);
        await LinkItemAsync(context, batchId, seq: 0, runId);
        await LinkItemAsync(context, batchId, seq: 1, otherRunId);

        var export = await context.DataSubjects.ExportAsync("tenant-a", Scope(sessionIds: ["subject-session"]));

        using (var document = System.Text.Json.JsonDocument.Parse(export.Json))
        {
            var root = document.RootElement;
            root.GetProperty("eval_cases").EnumerateArray().ShouldHaveSingleItem().GetProperty("query").GetString().ShouldBe("promoted-words");
            root.GetProperty("idempotency_keys").EnumerateArray().ShouldHaveSingleItem().GetProperty("key").GetString().ShouldBe("key-a");
            root.GetProperty("jobs").EnumerateArray().ShouldHaveSingleItem();
            root.GetProperty("job_items").EnumerateArray().ShouldHaveSingleItem().GetProperty("input").GetString().ShouldBe("item-words");
        }

        var deleted = await context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(sessionIds: ["subject-session"]),
            static (_, _) => ValueTask.CompletedTask);

        deleted["eval_cases"].ShouldBe(1);
        deleted["idempotency_keys"].ShouldBe(1);
        deleted["job_items"].ShouldBe(1);
        deleted["jobs"].ShouldBe(1);

        (await context.Evals.ListCasesAsync(suiteA)).ShouldHaveSingleItem().Query.ShouldBe("hand-written case");
        (await context.Evals.ListCasesAsync(suiteB)).ShouldHaveSingleItem();
        (await IdempotencyKeyCountAsync(context, "tenant-a")).ShouldBe(0);
        (await IdempotencyKeyCountAsync(context, "tenant-b")).ShouldBe(1);
        (await context.Jobs.GetAsync("tenant-a", runId)).ShouldBeNull();
        (await context.Jobs.ListItemsAsync(batchId)).ShouldHaveSingleItem().Input.ShouldBe("other item");
    }

    private static async Task<Guid> SeedSuiteAsync(SqlServerTestContext context, string tenantId)
        => (await context.Evals.SaveSuiteAsync(new EvalSuite
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "suite",
            AgentName = "support",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        })).Id;

    private static async Task SeedIdempotentResponseAsync(SqlServerTestContext context, string tenantId, string key, Guid runId)
    {
        await context.IdempotencyKeys.ReserveAsync(new IdempotencyRequest
        {
            TenantId = tenantId,
            Key = key,
            Fingerprint = "fingerprint",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.IdempotencyKeys.CompleteAsync(tenantId, key, new IdempotencyResponse
        {
            StatusCode = 200,
            ContentType = "application/json",
            Body = """{"text":"response-words"}""",
            RunId = runId,
        });
    }

    private static async Task<int> IdempotencyKeyCountAsync(SqlServerTestContext context, string tenantId)
        => await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.idempotency_keys WHERE tenant_id = '{tenantId}';");

    private static async Task<Guid> SeedJobAsync(SqlServerTestContext context, string tenantId, Guid id, IReadOnlyList<string> items)
    {
        var now = DateTimeOffset.UtcNow;

        await context.Jobs.EnqueueAsync(
            new JobRecord
            {
                Id = id,
                TenantId = tenantId,
                HandlerKey = JobHandlerKeys.AgentRun,
                TargetName = "support",
                Status = JobStatus.Pending,
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { message = "queued-words" }),
                ScheduledFor = now,
                CreatedAt = now,
            },
            items);

        return id;
    }

    private static async Task LinkItemAsync(SqlServerTestContext context, Guid jobId, int seq, Guid runId)
        => await context.ExecuteAsync($"""
            UPDATE {context.SchemaName}.job_items SET run_id = '{runId}' WHERE job_id = '{jobId}' AND seq = {seq};
            """);

    private static DataSubjectScope Scope(
        IReadOnlyList<string>? sessionIds = null,
        IReadOnlyList<Guid>? runIds = null,
        IReadOnlyList<Guid>? conversationIds = null)
        => new()
        {
            SessionIds = sessionIds ?? [],
            RunIds = runIds ?? [],
            ConversationIds = conversationIds ?? [],
        };

    private static async Task SeedSessionAsync(SqlServerTestContext context, string id, string tenantId)
        => await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', {EmptyJsonState}, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

    private static async Task<bool> SessionExistsAsync(SqlServerTestContext context, string id, string tenantId)
        => await context.ScalarAsync<int>($"""
            SELECT COUNT(*) FROM {context.SchemaName}.sessions WHERE id = '{id}' AND tenant_id = '{tenantId}';
            """) > 0;

    private static async Task<Guid> SeedRunAsync(
        SqlServerTestContext context,
        string tenantId,
        int status = (int)RunStatus.Completed,
        string? sessionId = null)
    {
        var id = Guid.NewGuid();
        var session = sessionId is null ? "NULL" : $"N'{sessionId}'";

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.runs (id, tenant_id, agent_name, status, started_at, is_streaming, event_count, session_id)
            VALUES ('{id}', '{tenantId}', 'support', {status}, SYSDATETIMEOFFSET(), 0, 0, {session});
            """);

        return id;
    }

    private static async Task<bool> RunExistsAsync(SqlServerTestContext context, Guid id)
        => await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.runs WHERE id = '{id}';") > 0;

    private static async Task SeedRunInputAsync(SqlServerTestContext context, Guid runId, string tenantId)
        => await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.run_inputs (run_id, tenant_id, messages, created_at)
            VALUES ('{runId}', '{tenantId}', N'[]', SYSDATETIMEOFFSET());
            """);

    private static async Task<bool> RunInputExistsAsync(SqlServerTestContext context, Guid runId)
        => await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.run_inputs WHERE run_id = '{runId}';") > 0;

    private static async Task<Guid> SeedConversationAsync(SqlServerTestContext context, string tenantId)
    {
        var id = Guid.NewGuid();

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.conversations (id, tenant_id, agent_name, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

        return id;
    }

    private static async Task<bool> ConversationExistsAsync(SqlServerTestContext context, Guid id)
        => await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.conversations WHERE id = '{id}';") > 0;

    private static async Task SeedConversationItemAsync(SqlServerTestContext context, Guid conversationId, long seq)
        => await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.conversation_items (id, conversation_id, seq, item, created_at)
            VALUES ('{Guid.NewGuid()}', '{conversationId}', {seq}, {EmptyJsonState}, SYSDATETIMEOFFSET());
            """);

    private static async Task<int> ConversationItemCountAsync(SqlServerTestContext context, Guid conversationId)
        => await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.conversation_items WHERE conversation_id = '{conversationId}';");

    private static async Task SeedCheckpointAsync(
        SqlServerTestContext context,
        string tenantId,
        string sessionId,
        Guid? runId,
        string state = """{"$type":0}""")
    {
        using var parsed = System.Text.Json.JsonDocument.Parse(state);

        await context.WorkflowCheckpoints.CreateAsync(new WorkflowCheckpointRecord
        {
            Id = TraconId.NewId(),
            TenantId = tenantId,
            SessionId = sessionId,
            CheckpointId = Guid.NewGuid().ToString("n"),
            RunId = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            State = parsed.RootElement.Clone(),
        });
    }

    private static async Task<int> CheckpointCountAsync(SqlServerTestContext context, string tenantId, string sessionId)
        => (await context.WorkflowCheckpoints.ListAsync(tenantId, sessionId)).Count;

    private static RunScore Score(Guid runId, string tenantId)
        => new()
        {
            TenantId = tenantId,
            RunId = runId,
            Name = RunScoreRules.DefaultName,
            Kind = RunScoreKind.Binary,
            Value = 0,
            Comment = "my own words",
            Source = "human",
            Author = "end-user",
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
