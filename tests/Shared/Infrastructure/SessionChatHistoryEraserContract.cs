using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// MAAI001: ChatHistoryProvider.InvokedContext is marked "evaluation purposes only". It is the one public way to write
// the history of a session without running an agent, and the product writes through it the same way.
#pragma warning disable MAAI001

namespace Tracon.Tests.Common;

/// <summary>
/// What deleting a session, the retention period of the sessions and the erasure of a data subject do with the chat history of
/// the sessions, written once for every SQL provider.
/// </summary>
/// <remarks>
/// <para>
/// The history of a session is a conversation the <c>conversations</c> table cannot tie to the session: the id is in the state
/// of the session. Every scenario writes a REAL history (a session of a real agent, through the SQL history provider), so
/// the state the eraser reads is what the agent framework writes and not a JSON this test made up.
/// </para>
/// <para>
/// The tenant context of the stores answers the ambient tenant, as the ones the product registers do: a retention run
/// names the tenant of each session and has no request to take it from.
/// </para>
/// </remarks>
public abstract class SessionChatHistoryEraserContract
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private static readonly DateTimeOffset Past = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Future = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Opens the store context of one migrated, empty schema.</summary>
    /// <returns>The context.</returns>
    internal abstract ValueTask<SqlStoreContext> OpenAsync();

    /// <summary>
    /// Gets whether the context writes the state of a session and the items of a history as protected values. A class that
    /// opens a protecting context says so, and every scenario runs the same: the state the eraser reads is then a value the
    /// session store unprotects, not the stored text.
    /// </summary>
    internal virtual bool ContentIsProtected => false;

    /// <summary>The state and the items are stored in the form the context writes them, so the scenarios of a protecting context read protected values.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task The_stored_state_and_items_are_in_the_form_the_context_writes_them()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "session-1", "support");

        var state = await rig.TextAsync("sessions", "state");
        var item = await rig.TextAsync("conversation_items", "item");

        if (ContentIsProtected)
        {
            state.ShouldContain("$apEnc", Case.Sensitive);
            state.ShouldNotContain("stateBag", Case.Sensitive);
            item.ShouldContain("$apEnc", Case.Sensitive);
            item.ShouldNotContain("where is my order", Case.Sensitive);
        }
        else
        {
            state.ShouldContain("stateBag", Case.Sensitive);
            item.ShouldContain("where is my order", Case.Sensitive);
        }
    }

    /// <summary>Deleting a session deletes its conversation with the items, and no other history.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task Deleting_a_session_deletes_its_conversation_and_its_items_and_no_other_history()
    {
        var rig = await OpenRigAsync();
        var gone = await rig.SeedTurnAsync(TenantA, "session-1", "support");
        var kept = await rig.SeedTurnAsync(TenantA, "session-2", "support");

        (await rig.Delete(TenantA, "session-1")).ShouldBeTrue();

        (await rig.CountAsync("sessions")).ShouldBe(1);
        (await rig.ConversationsAsync()).ShouldBe([kept]);
        (await rig.CountAsync("conversation_items")).ShouldBe(2, "the two items of the other session; the items of the deleted one went with their conversation");
        (await rig.ConversationExistsAsync(gone)).ShouldBeFalse();
    }

    /// <summary>A session whose agent is no longer registered is read from its stored state.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_session_whose_agent_is_not_registered_has_its_history_deleted_from_the_stored_state()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "orphan", "vanished-agent");

        (await rig.Delete(TenantA, "orphan")).ShouldBeTrue();

        (await rig.CountAsync("conversations")).ShouldBe(0);
        (await rig.CountAsync("conversation_items")).ShouldBe(0);
        rig.Logger.Warnings.ShouldBeEmpty("the state was readable; nothing was left unresolved");
    }

    /// <summary>A session whose agent the catalog cannot compile can still be deleted, with its history.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_session_whose_agent_the_catalog_cannot_compile_is_deleted_with_its_history_and_the_delete_does_not_fail()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "doomed", "doomed-agent");
        rig.Catalog.Refuse("doomed-agent", new TraconException("The provider key has no value."));

        (await rig.Delete(TenantA, "doomed")).ShouldBeTrue();

        (await rig.CountAsync("sessions")).ShouldBe(0);
        (await rig.CountAsync("conversations")).ShouldBe(0);
        (await rig.CountAsync("conversation_items")).ShouldBe(0);
    }

    /// <summary>The same session id in two tenants is two sessions: the delete of one leaves the history of the other.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task Deleting_a_session_leaves_the_history_of_the_same_session_id_in_another_tenant()
    {
        var rig = await OpenRigAsync();
        var mine = await rig.SeedTurnAsync(TenantA, "shared-id", "support");
        var theirs = await rig.SeedTurnAsync(TenantB, "shared-id", "support");

        (await rig.Delete(TenantA, "shared-id")).ShouldBeTrue();

        (await rig.ConversationExistsAsync(mine)).ShouldBeFalse();
        (await rig.ConversationExistsAsync(theirs)).ShouldBeTrue("the conversation of tenant-b's session is not tenant-a's to delete");
        (await rig.CountAsync("sessions", TenantB)).ShouldBe(1);
        (await rig.ItemsOfAsync(theirs)).ShouldBe(2);
    }

    /// <summary>A state that names the conversation of another tenant deletes nothing of that tenant: the delete is by tenant and id.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_state_that_names_the_conversation_of_another_tenant_does_not_delete_it()
    {
        var rig = await OpenRigAsync();
        var theirs = await rig.SeedTurnAsync(TenantB, "their-session", "support");
        await rig.SaveStateAsync(TenantA, "my-session", "vanished-agent", Rig.StateNaming(theirs));

        var erased = await rig.Eraser.EraseAsync(TenantA, ["my-session"], TestContext.Current.CancellationToken);

        erased.ShouldBe(0);
        (await rig.ConversationExistsAsync(theirs)).ShouldBeTrue();
        (await rig.ItemsOfAsync(theirs)).ShouldBe(2);
    }

    /// <summary>A session without a history, and a session that does not exist, have nothing to delete and nothing fails.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_session_without_a_history_or_that_does_not_exist_has_nothing_to_delete()
    {
        var rig = await OpenRigAsync();
        var kept = await rig.SeedTurnAsync(TenantA, "with-history", "support");
        await rig.SaveStateAsync(TenantA, "fresh", "support", JsonDocument.Parse("""{"stateBag":{}}""").RootElement.Clone());

        var erased = await rig.Eraser.EraseAsync(TenantA, ["fresh", "no-such-session"], TestContext.Current.CancellationToken);

        erased.ShouldBe(0);
        (await rig.ConversationExistsAsync(kept)).ShouldBeTrue();
        rig.Logger.Warnings.ShouldBeEmpty();
    }

    /// <summary>The count names the conversations of the sessions and deletes nothing.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task The_count_names_the_conversations_of_the_sessions_and_deletes_nothing()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "session-1", "support");
        await rig.SeedTurnAsync(TenantA, "session-2", "support");
        await rig.SeedTurnAsync(TenantA, "session-3", "support");

        (await rig.Eraser.CountAsync(TenantA, ["session-1", "session-2", "no-such-session"], TestContext.Current.CancellationToken)).ShouldBe(2);
        (await rig.Eraser.CountAsync(TenantB, ["session-1"], TestContext.Current.CancellationToken)).ShouldBe(0, "another tenant's session of that id is not there");
        (await rig.CountAsync("conversations")).ShouldBe(3);
    }

    /// <summary>A state the conversation cannot be read from is logged and its history stays: nothing says which conversation is its.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_state_that_has_another_shape_is_reported_and_its_history_stays()
    {
        var rig = await OpenRigAsync();
        var kept = await rig.SeedTurnAsync(TenantA, "session-1", "vanished-agent");
        await rig.SaveStateAsync(TenantA, "session-1", "vanished-agent", JsonDocument.Parse("{}").RootElement.Clone());

        var erased = await rig.Eraser.EraseAsync(TenantA, ["session-1"], TestContext.Current.CancellationToken);

        erased.ShouldBe(0);
        (await rig.ConversationExistsAsync(kept)).ShouldBeTrue();
        rig.Logger.Warnings.ShouldContain(static line => line.Contains("could not be found", StringComparison.Ordinal));
    }

    /// <summary>A lookup that fails leaves the session record in place, so a repeat call finds it and finishes.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_failure_of_the_lookup_leaves_the_session_and_a_repeat_call_finishes()
    {
        var rig = await OpenRigAsync();
        var conversation = await rig.SeedTurnAsync(TenantA, "session-1", "flaky-agent");
        rig.Catalog.Refuse("flaky-agent", new InvalidOperationException("The catalog is down."));

        await Should.ThrowAsync<InvalidOperationException>(async () => await rig.Delete(TenantA, "session-1"));

        (await rig.CountAsync("sessions")).ShouldBe(1);
        (await rig.ConversationExistsAsync(conversation)).ShouldBeTrue();

        rig.Catalog.Refuse("flaky-agent", null);

        (await rig.Delete(TenantA, "session-1")).ShouldBeTrue();
        (await rig.CountAsync("sessions")).ShouldBe(0);
        (await rig.ConversationExistsAsync(conversation)).ShouldBeFalse();
    }

    /// <summary>A repeat erasure of a session whose history is gone deletes nothing and does not fail.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_second_erasure_of_the_same_session_deletes_nothing_and_does_not_fail()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "session-1", "support");

        (await rig.Eraser.EraseAsync(TenantA, ["session-1"], TestContext.Current.CancellationToken)).ShouldBe(1);
        (await rig.Eraser.EraseAsync(TenantA, ["session-1"], TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    /// <summary>A conversation no session names is left alone: a run that has no session writes one, and nothing ties it to a record.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_conversation_that_no_session_names_is_left_alone()
    {
        var rig = await OpenRigAsync();
        var sessionless = await rig.WriteSessionlessTurnAsync(TenantA);
        await rig.SeedTurnAsync(TenantA, "session-1", "support");

        (await rig.Delete(TenantA, "session-1")).ShouldBeTrue();

        (await rig.ConversationsAsync()).ShouldBe([sessionless]);
        (await rig.Eraser.EraseAsync(TenantA, ["session-1"], TestContext.Current.CancellationToken)).ShouldBe(0);
        (await rig.ConversationExistsAsync(sessionless)).ShouldBeTrue();
    }

    /// <summary>The retention period of the sessions deletes the history of every expired session, each in its own tenant.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task Retention_deletes_the_history_of_every_expired_session_in_its_own_tenant()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "shared-id", "support");
        await rig.SeedTurnAsync(TenantB, "shared-id", "support");
        await rig.SeedTurnAsync(TenantB, "other-id", "vanished-agent");
        var retention = new SqlRetentionStore(rig.Context, contentErasers: [rig.Eraser]);

        var deleted = await retention.DeleteBatchAsync(RetentionTargets.Sessions, tenantId: null, Future, batchSize: 10, TestContext.Current.CancellationToken);

        deleted.ShouldBe(3);
        (await rig.CountAsync("sessions")).ShouldBe(0);
        (await rig.CountAsync("conversations")).ShouldBe(0);
        (await rig.CountAsync("conversation_items")).ShouldBe(0);
    }

    /// <summary>A session that has not expired keeps its history.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task Retention_keeps_the_history_of_a_session_that_has_not_expired()
    {
        var rig = await OpenRigAsync();
        var conversation = await rig.SeedTurnAsync(TenantA, "session-1", "support");
        var retention = new SqlRetentionStore(rig.Context, contentErasers: [rig.Eraser]);

        (await retention.DeleteBatchAsync(RetentionTargets.Sessions, tenantId: null, Past, batchSize: 10, TestContext.Current.CancellationToken)).ShouldBe(0);

        (await rig.CountAsync("sessions")).ShouldBe(1);
        (await rig.ConversationExistsAsync(conversation)).ShouldBeTrue();
    }

    /// <summary>A retention run for one tenant leaves the history of another tenant's sessions.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_retention_run_for_one_tenant_leaves_the_history_of_the_other_tenants_sessions()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "shared-id", "support");
        var theirs = await rig.SeedTurnAsync(TenantB, "shared-id", "support");
        var retention = new SqlRetentionStore(rig.Context, contentErasers: [rig.Eraser]);

        (await retention.DeleteBatchAsync(RetentionTargets.Sessions, TenantA, Future, batchSize: 10, TestContext.Current.CancellationToken)).ShouldBe(1);

        (await rig.ConversationsAsync()).ShouldBe([theirs]);
        (await rig.CountAsync("sessions", TenantB)).ShouldBe(1);
    }

    /// <summary>The archive path of the retention period deletes the history too, after the rows are handed to the sink.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task The_archive_path_of_retention_deletes_the_history_too()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "session-1", "support");
        await rig.SeedTurnAsync(TenantB, "session-1", "support");
        var retention = new SqlRetentionStore(rig.Context, contentErasers: [rig.Eraser]);
        var archived = 0;

        var deleted = await retention.ArchiveBatchAsync(
            RetentionTargets.Sessions,
            tenantId: null,
            Future,
            batchSize: 10,
            (rows, _) =>
            {
                archived += rows.Count;

                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        deleted.ShouldBe(2);
        archived.ShouldBe(2);
        (await rig.CountAsync("conversations")).ShouldBe(0);
        (await rig.CountAsync("conversation_items")).ShouldBe(0);
    }

    /// <summary>
    /// A data subject erasure that names a session and its conversation deletes the conversation once and reports it once:
    /// the eraser is not part of that erasure, because the scope already holds the conversation.
    /// </summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_data_subject_erasure_that_names_the_session_and_its_conversation_counts_the_conversation_once()
    {
        var rig = await OpenRigAsync();
        var conversation = await rig.SeedTurnAsync(TenantA, "session-1", "support");
        var other = await rig.SeedTurnAsync(TenantA, "session-2", "support");
        var store = new SqlDataSubjectStore(rig.Context, contentErasers: [rig.Eraser]);
        var scope = new DataSubjectScope { SessionIds = ["session-1"], RunIds = [], ConversationIds = [conversation] };

        var preview = await store.PreviewAsync(TenantA, scope, TestContext.Current.CancellationToken);

        preview["conversations"].ShouldBe(1);
        preview.ContainsKey(rig.Eraser.Target).ShouldBeFalse("the history is counted under the conversations target, not twice");

        var erased = await store.EraseAsync(TenantA, scope, static (_, _) => ValueTask.CompletedTask, TestContext.Current.CancellationToken);

        erased["conversations"].ShouldBe(1);
        erased["sessions"].ShouldBe(1);
        erased.ContainsKey(rig.Eraser.Target).ShouldBeFalse();
        (await rig.ConversationsAsync()).ShouldBe([other]);
        (await rig.CountAsync("conversation_items")).ShouldBe(2);
    }

    /// <summary>The export of a data subject lists the history once, from the conversation tables.</summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task A_data_subject_export_lists_the_history_from_the_conversation_tables_only()
    {
        var rig = await OpenRigAsync();
        var conversation = await rig.SeedTurnAsync(TenantA, "session-1", "support");
        var store = new SqlDataSubjectStore(rig.Context, contentErasers: [rig.Eraser]);
        var scope = new DataSubjectScope { SessionIds = ["session-1"], RunIds = [], ConversationIds = [conversation] };

        var export = await store.ExportAsync(TenantA, scope, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(export.Json);
        document.RootElement.GetProperty("conversations").GetArrayLength().ShouldBe(1);
        document.RootElement.GetProperty("conversation_items").GetArrayLength().ShouldBe(2);
        document.RootElement.TryGetProperty(rig.Eraser.Target, out _).ShouldBeFalse();
    }

    /// <summary>
    /// The export of the eraser itself is an empty array: the history of a subject is exported from the conversation tables.
    /// </summary>
    /// <returns>A task that completes when the checks are done.</returns>
    [Fact]
    public async Task The_export_of_the_eraser_is_an_empty_array()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "session-1", "support");
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            await rig.Eraser.WriteExportAsync(TenantA, ["session-1"], writer, TestContext.Current.CancellationToken);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        document.RootElement.GetProperty(rig.Eraser.Target).GetArrayLength().ShouldBe(0);
    }

    /// <summary>
    /// MEASURED LIMIT, not a promise: a run that is still writing the history of a session when the session is deleted
    /// writes its conversation again, and nothing names it any more.
    /// </summary>
    /// <returns>A task that completes when the checks are done.</returns>
    /// <remarks>
    /// The session of the run still holds the conversation id in its own state, and the history provider creates the
    /// conversation when it does not find it. The write cannot be refused for want of a session record: the first turn of a
    /// new session writes its history before the record exists. The caller that deletes a session while a run of it is
    /// still going has to cancel or wait for the run first.
    /// </remarks>
    [Fact]
    public async Task A_turn_that_stores_its_history_after_the_session_was_deleted_writes_the_conversation_again()
    {
        var rig = await OpenRigAsync();
        await rig.SeedTurnAsync(TenantA, "session-1", "support");
        var inFlight = await rig.OpenSessionAsync(TenantA, "session-1", "support");

        (await rig.Delete(TenantA, "session-1")).ShouldBeTrue();
        (await rig.CountAsync("conversations")).ShouldBe(0);

        await rig.StoreTurnAsync(TenantA, "support", inFlight);

        (await rig.CountAsync("sessions")).ShouldBe(0);
        (await rig.CountAsync("conversations")).ShouldBe(1, "the late write created the conversation again");
        (await rig.CountAsync("conversation_items")).ShouldBe(2, "and only the items of the late turn");
    }

    private async ValueTask<Rig> OpenRigAsync() => new(await OpenAsync());

    private sealed class Rig
    {
        public Rig(SqlStoreContext context)
        {
            Context = context;

            var tenants = new AmbientTenantContext();

            Sessions = new SqlSessionStore(context, tenants);
            History = new SqlChatHistoryProvider(context, tenants);
            Seeding = new AgentSessionManager(Sessions, tenants);
            Catalog = new ScriptedCatalog();
            Logger = new RecordingLogger<SessionChatHistoryEraser>();

            var resolver = new SessionConversationResolver(Sessions, Catalog);

            Eraser = new SessionChatHistoryEraser(context, () => resolver, Logger);
            Deleting = new AgentSessionManager(Sessions, tenants, contentErasers: [Eraser]);

            Catalog.Register("support");
        }

        public SqlStoreContext Context { get; }

        public SqlSessionStore Sessions { get; }

        public SqlChatHistoryProvider History { get; }

        public AgentSessionManager Seeding { get; }

        public AgentSessionManager Deleting { get; }

        public ScriptedCatalog Catalog { get; }

        public RecordingLogger<SessionChatHistoryEraser> Logger { get; }

        public SessionChatHistoryEraser Eraser { get; }

        /// <summary>Deletes a session the way a caller does, in the tenant of the call.</summary>
        public async Task<bool> Delete(string tenant, string sessionId)
        {
            using var scope = AmbientTenantScope.Begin(tenant);

            return await Deleting.DeleteSessionAsync(sessionId, TestContext.Current.CancellationToken);
        }

        /// <summary>Runs one turn of a session of the named agent: the history is written, and the session is saved.</summary>
        public async Task<Guid> SeedTurnAsync(string tenant, string sessionId, string agentName)
        {
            using var scope = AmbientTenantScope.Begin(tenant);
            var session = await OpenSessionAsync(tenant, sessionId, agentName);

            await StoreTurnAsync(tenant, agentName, session);
            await Seeding.SaveSessionAsync(Agent(agentName), session, TestContext.Current.CancellationToken);

            return ConversationOf(session);
        }

        public async Task<AgentSession> OpenSessionAsync(string tenant, string sessionId, string agentName)
        {
            using var scope = AmbientTenantScope.Begin(tenant);

            return await Seeding.GetOrCreateSessionAsync(Agent(agentName), sessionId, TestContext.Current.CancellationToken);
        }

        /// <summary>Writes one turn (a question and its answer) into the history of the session.</summary>
        public async Task StoreTurnAsync(string tenant, string agentName, AgentSession session)
        {
            using var scope = AmbientTenantScope.Begin(tenant);

            await History.InvokedAsync(
                new ChatHistoryProvider.InvokedContext(
                    Agent(agentName),
                    session,
                    [new ChatMessage(ChatRole.User, "where is my order")],
                    [new ChatMessage(ChatRole.Assistant, "it ships today")]),
                TestContext.Current.CancellationToken);
        }

        /// <summary>Writes the history of a run that has no session: the conversation exists and no record names it.</summary>
        public async Task<Guid> WriteSessionlessTurnAsync(string tenant)
        {
            using var scope = AmbientTenantScope.Begin(tenant);
            var agent = Agent("support");
            var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

            await StoreTurnAsync(tenant, "support", session);

            return ConversationOf(session);
        }

        /// <summary>Saves a session record whose state is the given JSON, for the agent of that name.</summary>
        public async Task SaveStateAsync(string tenant, string sessionId, string agentName, JsonElement state)
        {
            using var scope = AmbientTenantScope.Begin(tenant);

            await Sessions.SaveAsync(
                new SessionRecord
                {
                    Id = sessionId,
                    AgentName = agentName,
                    TenantId = tenant,
                    State = state,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                TestContext.Current.CancellationToken);
        }

        /// <summary>A stored state in the shape an agent writes, naming the given conversation.</summary>
        public static JsonElement StateNaming(Guid conversationId)
            => JsonSerializer.SerializeToElement(new
            {
                stateBag = new Dictionary<string, object>(StringComparer.Ordinal) { [TraconSessionStateKeys.ChatHistory] = new { conversationId } },
            });

        public async Task<long> CountAsync(string table, string? tenant = null)
        {
            var sql = $"SELECT COUNT(*) FROM {Context.Dialect.QualifyTable(table)}";

            if (tenant is not null)
            {
                sql += $" WHERE tenant_id = '{tenant}'";
            }

            return await ScalarAsync(sql);
        }

        /// <summary>Counts the items of one conversation; the id is bound as the provider binds it, not written as a literal.</summary>
        public async Task<long> ItemsOfAsync(Guid conversationId)
        {
            var table = Context.Dialect.QualifyTable("conversation_items");

            return await Context.ExecuteStatementAsync(
                $"SELECT COUNT(*) FROM {table} WHERE {Context.Dialect.ArrayContains($"{table}.conversation_id", "conversation_ids")}",
                command => Context.Dialect.AddUuidArray(command, "conversation_ids", [conversationId]),
                static async (command, token) => Convert.ToInt64(await DbHelpers.ExecuteScalarAsync(command, token), System.Globalization.CultureInfo.InvariantCulture),
                TestContext.Current.CancellationToken);
        }

        /// <summary>Reads one column of the first row of a table, as it is stored.</summary>
        public async Task<string> TextAsync(string table, string column)
            => await Context.ExecuteStatementAsync(
                $"SELECT {column} FROM {Context.Dialect.QualifyTable(table)}",
                static _ => { },
                static async (command, token) => Convert.ToString(await DbHelpers.ExecuteScalarAsync(command, token), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                TestContext.Current.CancellationToken);

        public async Task<IReadOnlyList<Guid>> ConversationsAsync()
            => await Context.ExecuteStatementAsync(
                $"SELECT id FROM {Context.Dialect.QualifyTable("conversations")}",
                static _ => { },
                static (command, token) => DbHelpers.ReadListAsync(command, static reader => DbHelpers.ToGuid(reader.GetValue(0)), token),
                TestContext.Current.CancellationToken);

        public async Task<bool> ConversationExistsAsync(Guid conversationId) => (await ConversationsAsync()).Contains(conversationId);

        private async Task<long> ScalarAsync(string sql)
            => await Context.ExecuteStatementAsync(
                sql,
                static _ => { },
                static async (command, token) => Convert.ToInt64(await DbHelpers.ExecuteScalarAsync(command, token), System.Globalization.CultureInfo.InvariantCulture),
                TestContext.Current.CancellationToken);

        private static ChatClientAgent Agent(string name) => new(new NeverCalledChatClient(), name: name);

        private static Guid ConversationOf(AgentSession session)
        {
            session.StateBag
                .TryGetValue<ChatHistoryState>(TraconSessionStateKeys.ChatHistory, out var state, TraconCoreJsonContext.Default.Options)
                .ShouldBeTrue("the history provider writes the conversation id into the state of the session");

            return state!.ConversationId;
        }
    }

    /// <summary>The tenant of the ambient scope, as the tenant contexts the product registers answer it.</summary>
    private sealed class AmbientTenantContext : ITenantContext
    {
        public string TenantId => AmbientTenantScope.Current ?? TenantA;
    }

    private sealed class ScriptedCatalog : IAgentCatalog
    {
        private readonly Dictionary<string, ChatClientAgent> _agents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Exception> _refusals = new(StringComparer.Ordinal);

        public void Register(string name) => _agents[name] = new ChatClientAgent(new NeverCalledChatClient(), name: name);

        public void Refuse(string name, Exception? failure)
        {
            if (failure is null)
            {
                _refusals.Remove(name);
            }
            else
            {
                _refusals[name] = failure;
            }
        }

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new(Array.Empty<AgentDescriptor>());

        public ValueTask<AIAgent?> ResolveAsync(string agentName, AgentResolveOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (_refusals.TryGetValue(agentName, out var failure))
            {
                throw failure;
            }

            return new(_agents.GetValueOrDefault(agentName));
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    /// <summary>A chat client of an agent that is only a name to write a session for: no scenario runs the agent.</summary>
    private sealed class NeverCalledChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("No scenario of this contract runs the agent.");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("No scenario of this contract runs the agent.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
