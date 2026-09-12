using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The tenant isolation, cascade, and commit/rollback behavior of the
/// PostgreSQL implementation of <see cref="IDataSubjectStore"/> (phase 64).
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

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Erase_removes_only_the_named_tenants_session_even_when_ids_collide()
    {
        // 🚨 sessions' key is (tenant_id, id) (K-278): the SAME id string is
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
    public async Task Erase_removes_a_conversation_and_its_items_together()
    {
        // K-107's exception (phase 64, 64.5): summarized/compacted messages are
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
        // K-370: an erasure that cannot be written to the audit trail is not
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

    private async Task<Guid> SeedRunAsync(string tenantId)
    {
        var id = Guid.NewGuid();

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.runs (id, tenant_id, agent_name, status, started_at, is_streaming, event_count)
            VALUES ('{id}', '{tenantId}', 'support', 1, now(), false, 0);
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
}
