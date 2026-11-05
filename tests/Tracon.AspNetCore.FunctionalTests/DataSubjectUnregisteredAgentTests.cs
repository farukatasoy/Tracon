using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A data subject export and erasure that meets a session whose agent is no longer registered still answers
/// <c>200</c> and says how many conversations it could not look up.
/// </summary>
/// <remarks>
/// <para>
/// The internal chat-history conversation of a session lives in the session's own state, so finding it restores the
/// session through its agent. An agent that was deleted or renamed leaves a session whose conversation cannot be found.
/// Failing the whole request would let one stale session block the erasure of everything else the subject has, so the
/// session is skipped; skipping without a word would let the caller read <c>200</c> as "everything is gone". The count
/// is that word.
/// </para>
/// <para>
/// Every test seeds a REAL conversation: a first host that has the agent runs it over SQLite, then a second host on the
/// same file has no such agent. A hand-written session row would carry no conversation and could not show that one stays
/// behind.
/// </para>
/// </remarks>
public sealed class DataSubjectUnregisteredAgentTests
{
    private const string Ghost = "ghost-agent";
    private const string UnresolvedHeader = "X-Tracon-Unresolved-Conversations";
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task The_preview_counts_the_conversation_it_could_not_look_up_and_changes_nothing()
    {
        using var database = new TempSqliteDatabase("subject-preview");
        await SeedAsync(database, "ghost-session");

        await using var host = await StartAsync(database, static _ => ["ghost-session"]);

        var reply = await SendAsync(host, HttpMethod.Delete, "data-subjects/subject");

        reply.Status.ShouldBe(HttpStatusCode.OK, reply.Body);
        reply.Json.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        reply.Json.GetProperty("unresolvedConversations").GetInt32().ShouldBe(1);
        reply.Json.GetProperty("rowsByTarget").GetProperty("sessions").GetInt32().ShouldBe(1);

        Count(database, Table.Sessions).ShouldBe(1);
        Count(database, Table.Conversations).ShouldBe(1);
        (await AuditEntriesAsync(host, "subject")).ShouldBeEmpty("a preview writes no audit entry");
    }

    [Fact]
    public async Task The_erasure_deletes_the_session_row_leaves_the_conversation_and_says_so_in_the_answer_and_the_audit_trail()
    {
        using var database = new TempSqliteDatabase("subject-erase");
        await SeedAsync(database, "ghost-session");
        Count(database, Table.ConversationItems).ShouldBeGreaterThan(0, "the seed has to have produced chat history");

        await using var host = await StartAsync(database, static _ => ["ghost-session"]);

        var reply = await SendAsync(host, HttpMethod.Delete, "data-subjects/subject?dryRun=false");

        reply.Status.ShouldBe(HttpStatusCode.OK, reply.Body);
        reply.Json.GetProperty("dryRun").GetBoolean().ShouldBeFalse();
        reply.Json.GetProperty("unresolvedConversations").GetInt32().ShouldBe(1);

        Count(database, Table.Sessions).ShouldBe(0);
        Count(database, Table.Conversations).ShouldBe(1, "the agent is gone, so the conversation cannot be found");
        Count(database, Table.ConversationItems).ShouldBeGreaterThan(0);

        var entry = (await AuditEntriesAsync(host, "subject")).ShouldHaveSingleItem();
        entry.GetProperty("action").GetString().ShouldBe("data_subject.erase");

        using var after = JsonDocument.Parse(entry.GetProperty("after").GetString()!);
        after.RootElement.GetProperty("unresolvedConversations").GetInt32().ShouldBe(1);
        after.RootElement.GetProperty("rowsByTarget").GetProperty("sessions").GetInt32().ShouldBe(1);

        var verify = await SendAsync(host, HttpMethod.Get, "audit/verify");
        verify.Json.GetProperty("status").GetString().ShouldBe("Valid", verify.Body);
    }

    [Fact]
    public async Task A_second_call_finds_no_session_and_counts_nothing()
    {
        using var database = new TempSqliteDatabase("subject-twice");
        await SeedAsync(database, "ghost-session");

        await using var host = await StartAsync(database, static _ => ["ghost-session"]);

        (await SendAsync(host, HttpMethod.Delete, "data-subjects/subject?dryRun=false"))
            .Json.GetProperty("unresolvedConversations").GetInt32().ShouldBe(1);

        var again = await SendAsync(host, HttpMethod.Delete, "data-subjects/subject?dryRun=false");

        again.Status.ShouldBe(HttpStatusCode.OK, again.Body);
        again.Json.GetProperty("unresolvedConversations").GetInt32().ShouldBe(0);
        Count(database, Table.Conversations).ShouldBe(1);
    }

    [Fact]
    public async Task The_export_carries_the_count_in_a_header_and_the_document_is_untouched()
    {
        using var database = new TempSqliteDatabase("subject-export");
        await SeedAsync(database, "ghost-session");

        await using var host = await StartAsync(database, static _ => ["ghost-session"]);

        var reply = await SendAsync(host, HttpMethod.Get, "data-subjects/subject/export");

        reply.Status.ShouldBe(HttpStatusCode.OK, reply.Body);
        reply.UnresolvedHeader.ShouldBe("1");
        reply.Json.ValueKind.ShouldBe(JsonValueKind.Object);
        reply.Json.TryGetProperty("unresolvedConversations", out _).ShouldBeFalse("the document is the store's, the count travels in the header");
        reply.Json.GetProperty("sessions").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task The_export_header_is_zero_when_every_conversation_was_looked_up()
    {
        using var database = new TempSqliteDatabase("subject-export-zero");
        await SeedAsync(database, "ghost-session");

        // The agent is registered on this host: the session is restored through it and its conversation is found.
        await using var host = await StartAsync(database, static _ => ["ghost-session"], registerGhost: true);

        var found = await SendAsync(host, HttpMethod.Get, "data-subjects/subject/export");
        var empty = await SendAsync(host, HttpMethod.Get, "data-subjects/nobody/export");

        found.Status.ShouldBe(HttpStatusCode.OK, found.Body);
        found.UnresolvedHeader.ShouldBe("0");
        found.Json.GetProperty("conversations").GetArrayLength().ShouldBe(1, "the conversation was found through the registered agent");
        empty.UnresolvedHeader.ShouldBe("0", "the header is written on every answer, 0 included");
    }

    [Fact]
    public async Task With_the_agent_registered_again_the_conversation_is_erased_with_the_session()
    {
        using var database = new TempSqliteDatabase("subject-recovered");
        await SeedAsync(database, "ghost-session");

        await using var host = await StartAsync(database, static _ => ["ghost-session"], registerGhost: true);

        var reply = await SendAsync(host, HttpMethod.Delete, "data-subjects/subject?dryRun=false");

        reply.Status.ShouldBe(HttpStatusCode.OK, reply.Body);
        reply.Json.GetProperty("unresolvedConversations").GetInt32().ShouldBe(0);
        Count(database, Table.Sessions).ShouldBe(0);
        Count(database, Table.Conversations).ShouldBe(0);
        Count(database, Table.ConversationItems).ShouldBe(0);
    }

    [Fact]
    public async Task A_session_of_another_tenant_is_not_counted_and_is_answered_like_an_id_that_does_not_exist()
    {
        // A count that included it would tell tenant A that this session id exists in tenant B.
        using var database = new TempSqliteDatabase("subject-foreign");
        await SeedAsync(database, "tenant-b-session", tenant: "tenant-b");

        await using var host = await StartAsync(
            database,
            static subject => string.Equals(subject, "foreign", StringComparison.Ordinal) ? ["tenant-b-session"] : ["no-such-session"],
            tenancy: true);

        var foreign = await SendAsync(host, HttpMethod.Delete, "data-subjects/foreign", tenant: "tenant-a");
        var absent = await SendAsync(host, HttpMethod.Delete, "data-subjects/absent", tenant: "tenant-a");

        foreign.Status.ShouldBe(HttpStatusCode.OK, foreign.Body);
        foreign.Json.GetProperty("unresolvedConversations").GetInt32().ShouldBe(0);
        foreign.Body.ShouldBe(absent.Body, "the two answers cannot differ by a byte");

        var real = await SendAsync(host, HttpMethod.Delete, "data-subjects/foreign?dryRun=false", tenant: "tenant-a");

        real.Status.ShouldBe(HttpStatusCode.OK, real.Body);
        real.Json.GetProperty("unresolvedConversations").GetInt32().ShouldBe(0);
        Count(database, Table.Sessions).ShouldBe(1, "tenant B's session is not tenant A's to erase");
        Count(database, Table.Conversations).ShouldBe(1);
    }

    [Fact]
    public async Task A_session_whose_state_cannot_be_restored_is_counted_and_does_not_block_the_request()
    {
        var store = new CountingStore();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddAgent(TestData.Definition(Ghost)),
            configureServices: services =>
            {
                services.AddSingleton<IDataSubjectResolver>(new NamedSessionsResolver(static _ => ["broken-session"]));
                services.AddSingleton<IDataSubjectStore>(store);
            });

        // A session state that is not a JSON object cannot be restored by any agent.
        await host.Services.GetRequiredService<ISessionStore>().SaveAsync(new SessionRecord
        {
            Id = "broken-session",
            AgentName = Ghost,
            State = JsonDocument.Parse("5").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = host.Services.GetRequiredService<ITenantContext>().TenantId,
        });

        var reply = await SendAsync(host, HttpMethod.Delete, "data-subjects/subject");

        reply.Status.ShouldBe(HttpStatusCode.OK, reply.Body);
        reply.Json.GetProperty("unresolvedConversations").GetInt32().ShouldBe(1);
        store.PreviewCalls.ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_of_the_session_store_is_not_a_skip_and_nothing_is_applied()
    {
        var store = new CountingStore();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<IDataSubjectResolver>(new NamedSessionsResolver(static _ => ["any-session"]));
                services.AddSingleton<IDataSubjectStore>(store);
                services.AddSingleton<ISessionStore>(new UnreachableSessionStore());
            });

        // The in-process host has no exception-handler middleware, so the failure surfaces from SendAsync.
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.Client.DeleteAsync(new Uri("/tracon/api/data-subjects/subject?dryRun=false", UriKind.Relative)));

        store.EraseCalls.ShouldBe(0);
        (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { TenantId = "default", Action = "data_subject.erase" }))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancelled_request_is_not_a_skip_and_nothing_is_applied()
    {
        var store = new CountingStore();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<IDataSubjectResolver>(new NamedSessionsResolver(static _ => ["any-session"]));
                services.AddSingleton<IDataSubjectStore>(store);
                services.AddSingleton<ISessionStore>(new CancellingSessionStore());
            });

        // The caller leaving mid-lookup must neither answer 400 or 200 with a partial count, nor reach the store or the audit trail.
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await host.Client.DeleteAsync(new Uri("/tracon/api/data-subjects/subject?dryRun=false", UriKind.Relative)));

        store.EraseCalls.ShouldBe(0);
        store.PreviewCalls.ShouldBe(0);
        (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { TenantId = "default", Action = "data_subject.erase" }))
            .ShouldBeEmpty();
    }

    /// <summary>Runs the agent over SQLite so that a session and its conversation exist, then stops the host that had the agent.</summary>
    private static async Task SeedAsync(TempSqliteDatabase database, string sessionId, string? tenant = null)
    {
        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.UseSqlite(database.ConnectionString);
            builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            });
            builder.AddAgent(TestData.Definition(Ghost));
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{Ghost}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "where is my order", SessionId = sessionId }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A host on the seeded database; the agent is registered only when asked.</summary>
    private static Task<TraconTestHost> StartAsync(
        TempSqliteDatabase database,
        Func<string, string[]> sessionsOfSubject,
        bool registerGhost = false,
        bool tenancy = false)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);

                if (tenancy || registerGhost)
                {
                    builder.UseTenancy(options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }

                if (registerGhost)
                {
                    builder.AddAgent(TestData.Definition(Ghost));
                }
            },
            configureServices: services => services.AddSingleton<IDataSubjectResolver>(new NamedSessionsResolver(sessionsOfSubject)));

    private static async Task<Reply> SendAsync(TraconTestHost host, HttpMethod method, string path, string? tenant = null)
    {
        using var request = new HttpRequestMessage(method, new Uri($"/tracon/api/{path}", UriKind.Relative));

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var header = response.Headers.TryGetValues(UnresolvedHeader, out var values) ? values.Single() : null;

        return new Reply(
            response.StatusCode,
            body,
            header,
            body.Length > 0 && body[0] is '{' or '[' ? JsonDocument.Parse(body).RootElement.Clone() : default);
    }

    private static async Task<IReadOnlyList<JsonElement>> AuditEntriesAsync(TraconTestHost host, string subject)
    {
        var reply = await SendAsync(host, HttpMethod.Get, $"audit?entity=data-subject:{subject}");
        reply.Status.ShouldBe(HttpStatusCode.OK, reply.Body);

        return [.. reply.Json.EnumerateArray()];
    }

    private static long Count(TempSqliteDatabase database, Table table)
    {
        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = table switch
        {
            Table.Sessions => "SELECT COUNT(*) FROM tracon_sessions",
            Table.Conversations => "SELECT COUNT(*) FROM tracon_conversations",
            Table.ConversationItems => "SELECT COUNT(*) FROM tracon_conversation_items",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private enum Table
    {
        Sessions,
        Conversations,
        ConversationItems,
    }

    private sealed record Reply(HttpStatusCode Status, string Body, string? UnresolvedHeader, JsonElement Json);

    private sealed class NamedSessionsResolver(Func<string, string[]> sessionsOfSubject) : IDataSubjectResolver
    {
        public ValueTask<DataSubjectScope> ResolveAsync(string subjectId, string tenantId, CancellationToken cancellationToken = default)
            => new(new DataSubjectScope { SessionIds = sessionsOfSubject(subjectId), RunIds = [], ConversationIds = [] });
    }

    private sealed class CountingStore : IDataSubjectStore
    {
        public int PreviewCalls { get; private set; }

        public int EraseCalls { get; private set; }

        public ValueTask<DataSubjectExport> ExportAsync(string tenantId, DataSubjectScope scope, CancellationToken cancellationToken = default)
            => new(new DataSubjectExport { Json = "{}" });

        public ValueTask<IReadOnlyDictionary<string, int>> PreviewAsync(string tenantId, DataSubjectScope scope, CancellationToken cancellationToken = default)
        {
            PreviewCalls++;

            return new(new Dictionary<string, int>(StringComparer.Ordinal));
        }

        public ValueTask<IReadOnlyDictionary<string, int>> EraseAsync(
            string tenantId,
            DataSubjectScope scope,
            Func<IReadOnlyDictionary<string, int>, CancellationToken, ValueTask> beforeCommitAsync,
            CancellationToken cancellationToken = default)
        {
            EraseCalls++;

            return new(new Dictionary<string, int>(StringComparer.Ordinal));
        }
    }

    private sealed class UnreachableSessionStore : ISessionStore
    {
        public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("session store is unreachable");

        public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("session store is unreachable");

        public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("session store is unreachable");

        public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("session store is unreachable");
    }

    private sealed class CancellingSessionStore : ISessionStore
    {
        public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException();

        public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException();

        public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException();

        public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException();
    }
}
