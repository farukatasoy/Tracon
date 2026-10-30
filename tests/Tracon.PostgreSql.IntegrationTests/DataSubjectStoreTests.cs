using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The tenant isolation, cascade, and commit/rollback behavior of the
/// PostgreSQL implementation of <see cref="IDataSubjectStore"/>.
/// </summary>
/// <remarks>
/// Registered directly (not through <c>TenantIsolationContract&lt;T&gt;</c>,
/// see <c>TenantCoverageTests.Covered["SqlDataSubjectStore"]</c>): that base
/// assumes a store keyed by one named record, while <see cref="IDataSubjectStore"/>
/// operates over a <see cref="DataSubjectScope"/> spanning several tables at
/// once, so its isolation guarantee is tested directly against seeded rows instead.
/// </remarks>
public sealed class DataSubjectStoreTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext _context = null!;

    /// <summary>A checkpoint state that carries a subject's own words.</summary>
    private const string CheckpointState = """{"$type":0,"messages":["my own words"]}""";

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Erase_removes_only_the_named_tenants_session_even_when_ids_collide()
    {
        // 🚨 sessions' key is (tenant_id, id): the SAME id string is
        // legitimately reused across two tenants, so erasure must be scoped by
        // tenant, not by id alone.
        const string SessionId = "shared-session-id";
        await SeedSessionAsync(SessionId, "tenant-a");
        await SeedSessionAsync(SessionId, "tenant-b");

        await _context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(sessionIds: [SessionId]),
            static (_, _) => ValueTask.CompletedTask);

        (await SessionExistsAsync(SessionId, "tenant-a")).ShouldBeFalse();
        (await SessionExistsAsync(SessionId, "tenant-b")).ShouldBeTrue();
    }

    [Fact]
    public async Task Erase_cascades_from_run_to_run_inputs()
    {
        var runId = await SeedRunAsync("tenant-a");
        await SeedRunInputAsync(runId, "tenant-a");

        var deleted = await _context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(runIds: [runId]),
            static (_, _) => ValueTask.CompletedTask);

        deleted[DataSubjectTargetRegistry.Runs].ShouldBe(1);
        (await RunExistsAsync(runId)).ShouldBeFalse();
        (await RunInputExistsAsync(runId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Erase_cascades_from_run_to_the_non_token_usage_items_its_nodes_reported()
    {
        // The items are not an export target (they hold units and amounts, no content), so the
        // run's foreign key is what removes them: DataSubjectCoverageTests names that rule.
        var runId = await SeedRunAsync("tenant-a");
        await SeedNonTokenUsageAsync(runId, "tenant-a");
        (await NonTokenUsageCountAsync(runId)).ShouldBe(1);

        await _context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(runIds: [runId]),
            static (_, _) => ValueTask.CompletedTask);

        (await NonTokenUsageCountAsync(runId)).ShouldBe(0);
    }

    [Fact]
    public async Task Erase_removes_a_conversation_and_its_items_together()
    {
        // the exception: summarized/compacted messages are
        // normally kept forever, but a data subject erasure removes them too.
        var conversationId = await SeedConversationAsync("tenant-a");
        await SeedConversationItemAsync(conversationId, seq: 1);
        await SeedConversationItemAsync(conversationId, seq: 2);

        await _context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(conversationIds: [conversationId]),
            static (_, _) => ValueTask.CompletedTask);

        (await ConversationExistsAsync(conversationId)).ShouldBeFalse();
        (await ConversationItemCountAsync(conversationId)).ShouldBe(0);
    }

    [Fact]
    public async Task Preview_reports_counts_without_deleting_anything()
    {
        const string SessionId = "preview-session";
        await SeedSessionAsync(SessionId, "tenant-a");

        var preview = await _context.DataSubjects.PreviewAsync("tenant-a", Scope(sessionIds: [SessionId]));

        preview[DataSubjectTargetRegistry.Sessions].ShouldBe(1);
        (await SessionExistsAsync(SessionId, "tenant-a")).ShouldBeTrue();
    }

    [Fact]
    public async Task Erase_rolls_back_every_delete_when_the_commit_callback_throws()
    {
        // An erasure that cannot be written to the audit trail is not
        // applied. EraseAsync's beforeCommitAsync callback is where the caller
        // writes that audit entry; if it throws, nothing should be deleted.
        const string SessionId = "rollback-session";
        await SeedSessionAsync(SessionId, "tenant-a");

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _context.DataSubjects.EraseAsync(
                "tenant-a",
                Scope(sessionIds: [SessionId]),
                static (_, _) => throw new InvalidOperationException("audit write failed")));

        (await SessionExistsAsync(SessionId, "tenant-a")).ShouldBeTrue();
    }

    [Fact]
    public async Task Export_does_not_include_another_tenants_data()
    {
        const string SessionId = "export-session";
        await SeedSessionAsync(SessionId, "tenant-a");
        await SeedSessionAsync(SessionId, "tenant-b");

        var export = await _context.DataSubjects.ExportAsync("tenant-a", Scope(sessionIds: [SessionId]));

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
        // Workflow_checkpoints carries tenant_id, session_id and run_id but
        // no foreign key to runs, so no cascade reaches it — and its state is the
        // serialized workflow, which can hold the subject's own messages. The run
        // waits for input: erasure is a right, not retention, so the "keep the
        // checkpoints of a waiting run" does not apply — the run row goes too.
        var waitingRunId = await SeedRunAsync("tenant-a", status: (int)RunStatus.AwaitingInput);
        await SeedCheckpointAsync("tenant-a", "wf-session-a", runId: null);
        await SeedCheckpointAsync("tenant-a", "wf-session-b", waitingRunId);
        await SeedCheckpointAsync("tenant-a", "wf-session-c", Guid.NewGuid());
        await SeedCheckpointAsync("tenant-b", "wf-session-a", runId: null);

        var deleted = await _context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(sessionIds: ["wf-session-a"], runIds: [waitingRunId]),
            static (_, _) => ValueTask.CompletedTask);

        (await CheckpointCountAsync("tenant-a", "wf-session-a")).ShouldBe(0);
        (await CheckpointCountAsync("tenant-a", "wf-session-b")).ShouldBe(0);
        (await CheckpointCountAsync("tenant-a", "wf-session-c")).ShouldBe(1);
        (await CheckpointCountAsync("tenant-b", "wf-session-a")).ShouldBe(1);
        (await RunExistsAsync(waitingRunId)).ShouldBeFalse();
        deleted["workflow_checkpoints"].ShouldBe(2);
    }

    [Fact]
    public async Task Export_includes_the_workflow_checkpoints_with_their_state_as_stored()
    {
        await SeedCheckpointAsync("tenant-a", "wf-session-a", runId: null, state: CheckpointState);
        await SeedCheckpointAsync("tenant-b", "wf-session-a", runId: null);

        var export = await _context.DataSubjects.ExportAsync("tenant-a", Scope(sessionIds: ["wf-session-a"]));

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
        // Run_scores has no foreign key to runs, so no cascade
        // reaches it. A resolver that named only a session reached the session's
        // runs but not their scores — and a score's comment can hold the
        // subject's own words.
        var runId = await SeedRunAsync("tenant-a", sessionId: "scored-session");
        await _context.RunScores.UpsertAsync(Score(runId, "tenant-a"));

        var export = await _context.DataSubjects.ExportAsync("tenant-a", Scope(sessionIds: ["scored-session"]));

        using (var document = System.Text.Json.JsonDocument.Parse(export.Json))
        {
            document.RootElement.GetProperty("run_scores").GetArrayLength().ShouldBe(1);
        }

        var deleted = await _context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(sessionIds: ["scored-session"]),
            static (_, _) => ValueTask.CompletedTask);

        (await _context.RunScores.ListAsync("tenant-a", runId)).ShouldBeEmpty();
        deleted["run_scores"].ShouldBe(1);
    }

    /// <summary>
    /// A promoted eval case, an idempotent run's stored response, a queued
    /// run's job and a batch item each keep a copy of the subject's text, and none
    /// had a foreign key that cascaded from runs. They are reached through a run of
    /// the named session, never in another tenant, and a batch job keeps its other items.
    /// </summary>
    [Fact]
    public async Task Erase_and_export_reach_the_promoted_case_the_stored_response_the_queued_job_and_the_batch_item()
    {
        var runId = await SeedRunAsync("tenant-a", sessionId: "subject-session");
        var otherRunId = await SeedRunAsync("tenant-a", sessionId: "other-session");

        var suiteA = await SeedSuiteAsync("tenant-a");
        var suiteB = await SeedSuiteAsync("tenant-b");
        await _context.Evals.AddCaseAsync(suiteA, new EvalCaseDraft { Query = "promoted-words", SourceRunId = runId, SourceKind = EvalCaseSource.ReferenceRun });
        await _context.Evals.AddCaseAsync(suiteA, new EvalCaseDraft { Query = "hand-written case" });
        await _context.Evals.AddCaseAsync(suiteB, new EvalCaseDraft { Query = "other tenant", SourceRunId = runId, SourceKind = EvalCaseSource.ReferenceRun });

        await SeedIdempotentResponseAsync("tenant-a", "key-a", runId);
        await SeedIdempotentResponseAsync("tenant-b", "key-b", runId);

        await SeedJobAsync("tenant-a", runId, items: []);
        var batchId = await SeedJobAsync("tenant-a", Guid.NewGuid(), items: ["item-words", "other item"]);
        await LinkItemAsync(batchId, seq: 0, runId);
        await LinkItemAsync(batchId, seq: 1, otherRunId);

        var export = await _context.DataSubjects.ExportAsync("tenant-a", Scope(sessionIds: ["subject-session"]));

        using (var document = System.Text.Json.JsonDocument.Parse(export.Json))
        {
            var root = document.RootElement;
            root.GetProperty("eval_cases").EnumerateArray().ShouldHaveSingleItem().GetProperty("query").GetString().ShouldBe("promoted-words");
            root.GetProperty("idempotency_keys").EnumerateArray().ShouldHaveSingleItem().GetProperty("key").GetString().ShouldBe("key-a");
            root.GetProperty("jobs").EnumerateArray().ShouldHaveSingleItem();
            root.GetProperty("job_items").EnumerateArray().ShouldHaveSingleItem().GetProperty("input").GetString().ShouldBe("item-words");
        }

        var deleted = await _context.DataSubjects.EraseAsync(
            "tenant-a",
            Scope(sessionIds: ["subject-session"]),
            static (_, _) => ValueTask.CompletedTask);

        deleted["eval_cases"].ShouldBe(1);
        deleted["idempotency_keys"].ShouldBe(1);
        deleted["job_items"].ShouldBe(1);
        deleted["jobs"].ShouldBe(1);

        (await _context.Evals.ListCasesAsync(suiteA)).ShouldHaveSingleItem().Query.ShouldBe("hand-written case");
        (await _context.Evals.ListCasesAsync(suiteB)).ShouldHaveSingleItem();
        (await IdempotencyKeyCountAsync("tenant-a")).ShouldBe(0);
        (await IdempotencyKeyCountAsync("tenant-b")).ShouldBe(1);
        (await _context.Jobs.GetAsync("tenant-a", runId)).ShouldBeNull();
        (await _context.Jobs.ListItemsAsync(batchId)).ShouldHaveSingleItem().Input.ShouldBe("other item");
    }

    private async Task<Guid> SeedSuiteAsync(string tenantId)
        => (await _context.Evals.SaveSuiteAsync(new EvalSuite
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "suite",
            AgentName = "support",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        })).Id;

    private async Task SeedIdempotentResponseAsync(string tenantId, string key, Guid runId)
    {
        await _context.IdempotencyKeys.ReserveAsync(new IdempotencyRequest
        {
            TenantId = tenantId,
            Key = key,
            Fingerprint = "fingerprint",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _context.IdempotencyKeys.CompleteAsync(tenantId, key, new IdempotencyResponse
        {
            StatusCode = 200,
            ContentType = "application/json",
            Body = """{"text":"response-words"}""",
            RunId = runId,
        });
    }

    private async Task<long> IdempotencyKeyCountAsync(string tenantId)
        => await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.idempotency_keys WHERE tenant_id = '{tenantId}';");

    private async Task<Guid> SeedJobAsync(string tenantId, Guid id, IReadOnlyList<string> items)
    {
        var now = DateTimeOffset.UtcNow;

        await _context.Jobs.EnqueueAsync(
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

    private async Task LinkItemAsync(Guid jobId, int seq, Guid runId)
        => await _context.ExecuteAsync($"""
            UPDATE {_context.SchemaName}.job_items SET run_id = '{runId}' WHERE job_id = '{jobId}' AND seq = {seq};
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

    private async Task SeedSessionAsync(string id, string tenantId)
    {
        const string emptyJsonState = "'{}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', {emptyJsonState}, 1, now(), now());
            """);
    }

    private async Task<bool> SessionExistsAsync(string id, string tenantId)
        => await _context.ScalarAsync<long>($"""
            SELECT COUNT(*) FROM {_context.SchemaName}.sessions WHERE id = '{id}' AND tenant_id = '{tenantId}';
            """) > 0;

    private async Task<Guid> SeedRunAsync(string tenantId, int status = (int)RunStatus.Completed, string? sessionId = null)
    {
        var id = Guid.NewGuid();
        var session = sessionId is null ? "NULL" : $"'{sessionId}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.runs (id, tenant_id, agent_name, status, started_at, is_streaming, event_count, session_id)
            VALUES ('{id}', '{tenantId}', 'support', {status}, now(), false, 0, {session});
            """);

        return id;
    }

    private async Task<bool> RunExistsAsync(Guid id)
        => await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.runs WHERE id = '{id}';") > 0;

    private async Task SeedRunInputAsync(Guid runId, string tenantId)
        => await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.run_inputs (run_id, tenant_id, messages, created_at)
            VALUES ('{runId}', '{tenantId}', '[]', now());
            """);

    private async Task SeedNonTokenUsageAsync(Guid runId, string tenantId)
        => await _context.Runs.RecordNonTokenUsageAsync(new NonTokenUsageRecord
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            NodeName = "narrate",
            CreatedAt = DateTimeOffset.UtcNow,
            Usage = new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 10m, Cost = 0.01m, Currency = "USD" },
            TenantId = tenantId,
        });

    private async Task<long> NonTokenUsageCountAsync(Guid runId)
        => await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.run_non_token_usage WHERE run_id = '{runId}';");

    private async Task<bool> RunInputExistsAsync(Guid runId)
        => await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.run_inputs WHERE run_id = '{runId}';") > 0;

    private async Task<Guid> SeedConversationAsync(string tenantId)
    {
        var id = Guid.NewGuid();

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.conversations (id, tenant_id, agent_name, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', now(), now());
            """);

        return id;
    }

    private async Task<bool> ConversationExistsAsync(Guid id)
        => await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.conversations WHERE id = '{id}';") > 0;

    private async Task SeedConversationItemAsync(Guid conversationId, long seq)
    {
        const string emptyJsonItem = "'{}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.conversation_items (id, conversation_id, seq, item, created_at)
            VALUES ('{Guid.NewGuid()}', '{conversationId}', {seq}, {emptyJsonItem}, now());
            """);
    }

    private async Task<long> ConversationItemCountAsync(Guid conversationId)
        => await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.conversation_items WHERE conversation_id = '{conversationId}';");

    private async Task SeedCheckpointAsync(string tenantId, string sessionId, Guid? runId, string state = """{"$type":0}""")
    {
        using var parsed = System.Text.Json.JsonDocument.Parse(state);

        await _context.WorkflowCheckpoints.CreateAsync(new WorkflowCheckpointRecord
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

    private async Task<int> CheckpointCountAsync(string tenantId, string sessionId)
        => (await _context.WorkflowCheckpoints.ListAsync(tenantId, sessionId)).Count;

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
