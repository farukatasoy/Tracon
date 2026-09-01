using System.Globalization;
using AgentPrism.Sqlite.IntegrationTests.Infrastructure;

namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>
/// The tenant isolation, cascade, and commit/rollback behavior of the SQLite
/// implementation of <see cref="IDataSubjectStore"/> (phase 64).
/// </summary>
/// <remarks>
/// SQLite port of <c>AgentPrism.PostgreSql.IntegrationTests.DataSubjectStoreTests</c>
/// — the same scenarios, run against <c>SqlDataSubjectStore</c>'s
/// <c>json_each</c>-based <see cref="SqlDialect.ArrayContains"/> implementation
/// (K-198: shared code, but the dialect text is genuinely provider-specific and
/// untested on this provider until this file existed — see the phase 64 audit
/// finding that asked for this).
/// </remarks>
public sealed class DataSubjectStoreTests(SqliteFixture fixture)
{
    private const string EmptyJsonState = "'{}'";

    [Fact]
    public async Task Erase_removes_only_the_named_tenants_session_even_when_ids_collide()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
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
        await using var context = await SqliteTestContext.CreateAsync(fixture);
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
        await using var context = await SqliteTestContext.CreateAsync(fixture);
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
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        const string SessionId = "preview-session";
        await SeedSessionAsync(context, SessionId, "tenant-a");

        var preview = await context.DataSubjects.PreviewAsync("tenant-a", Scope(sessionIds: [SessionId]));

        preview[DataSubjectTargetRegistry.Sessions].ShouldBe(1);
        (await SessionExistsAsync(context, SessionId, "tenant-a")).ShouldBeTrue();
    }

    [Fact]
    public async Task Erase_rolls_back_every_delete_when_the_commit_callback_throws()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
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
        await using var context = await SqliteTestContext.CreateAsync(fixture);
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

    private static async Task SeedSessionAsync(SqliteTestContext context, string id, string tenantId)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', {EmptyJsonState}, 1, '{now}', '{now}');
            """);
    }

    private static async Task<bool> SessionExistsAsync(SqliteTestContext context, string id, string tenantId)
        => await context.ScalarAsync<long>($"""
            SELECT COUNT(*) FROM {context.TablePrefix}sessions WHERE id = '{id}' AND tenant_id = '{tenantId}';
            """) > 0;

    private static async Task<Guid> SeedRunAsync(SqliteTestContext context, string tenantId)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}runs (id, tenant_id, agent_name, status, started_at, is_streaming, event_count)
            VALUES ('{Sql(id)}', '{tenantId}', 'support', 1, '{now}', 0, 0);
            """);

        return id;
    }

    private static async Task<bool> RunExistsAsync(SqliteTestContext context, Guid id)
        => await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {context.TablePrefix}runs WHERE id = '{Sql(id)}';") > 0;

    private static async Task SeedRunInputAsync(SqliteTestContext context, Guid runId, string tenantId)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}run_inputs (run_id, tenant_id, messages, created_at)
            VALUES ('{Sql(runId)}', '{tenantId}', '[]', '{now}');
            """);
    }

    private static async Task<bool> RunInputExistsAsync(SqliteTestContext context, Guid runId)
        => await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {context.TablePrefix}run_inputs WHERE run_id = '{Sql(runId)}';") > 0;

    private static async Task<Guid> SeedConversationAsync(SqliteTestContext context, string tenantId)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}conversations (id, tenant_id, agent_name, created_at, updated_at)
            VALUES ('{Sql(id)}', '{tenantId}', 'support', '{now}', '{now}');
            """);

        return id;
    }

    private static async Task<bool> ConversationExistsAsync(SqliteTestContext context, Guid id)
        => await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {context.TablePrefix}conversations WHERE id = '{Sql(id)}';") > 0;

    private static async Task SeedConversationItemAsync(SqliteTestContext context, Guid conversationId, long seq)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}conversation_items (id, conversation_id, seq, item, created_at)
            VALUES ('{Sql(Guid.NewGuid())}', '{Sql(conversationId)}', {seq}, {EmptyJsonState}, '{now}');
            """);
    }

    private static async Task<long> ConversationItemCountAsync(SqliteTestContext context, Guid conversationId)
        => await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {context.TablePrefix}conversation_items WHERE conversation_id = '{Sql(conversationId)}';");

    /// <summary>
    /// Converts an id to the form SQLite STORES it in (K-191: <c>Microsoft.Data.Sqlite</c>
    /// writes <see cref="Guid"/> values as UPPERCASE hyphenated text, and SQLite text
    /// comparison is case-sensitive).
    /// </summary>
    private static string Sql(Guid id) => id.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();
}
