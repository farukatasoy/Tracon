using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
// Same rationale as AgentExtensionRunTests.cs: the package's Tracon.Testing.TraconTestHost and
// this project's own TraconTestHost share a name.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Conversation memory over a SQL host. The SQL stores take the content erasers from the
/// container (<c>ActivatorUtilities</c>); a unit test builds them by hand and so cannot see a constructor that
/// would leave them out.
/// </summary>
/// <remarks>
/// Data subject erasure and the sessions retention target run through the stores a SQLite host registers, with the
/// real session rows, and the memory records must go with them. Both are driven through the store interfaces
/// (<see cref="IDataSubjectStore"/>, <see cref="IRetentionStore"/>): the retention policy endpoints queue a job, and
/// the erasure endpoint hangs over SQLite for a reason of its own.
/// </remarks>
public sealed class ConversationMemorySqlTests
{
    private const string Collection = "sql_memory";
    private const string UserHeader = "X-Test-User";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string Colour = "My favourite colour is teal.";

    [Fact]
    public async Task A_data_subject_erasure_over_SQL_deletes_the_memory_of_the_subjects_sessions_only()
    {
        // Driven through the IDataSubjectStore the SQLite host registers, not through the HTTP endpoint: over SQLite
        // the endpoint's audit write, made inside the erasure transaction on another connection, never returns
        // (measured). The store is what takes the content erasers from the container.
        using var database = new TempSqliteDatabase("memory-erasure");
        var memory = new InMemoryVectorStore();

        await using var host = await StartAsync(database, memory);

        await RunAsync(host, "ada", "ada-session", Colour);
        await RunAsync(host, "grace", "grace-session", "My favourite colour is plum.");
        memory.Records(Collection).Select(static record => (string?)record["SessionId"]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .ShouldBe(["ada-session", "grace-session"]);

        var store = host.Services.GetRequiredService<IDataSubjectStore>();
        var scope = new DataSubjectScope { SessionIds = ["ada-session"], RunIds = [], ConversationIds = [] };

        var preview = await store.PreviewAsync("default", scope);
        preview["conversation_memory"].ShouldBe(2);
        preview["sessions"].ShouldBe(1);
        memory.Records(Collection).Count.ShouldBe(4, "a preview deletes nothing");

        var exported = (await store.ExportAsync("default", scope)).Json;
        exported.ShouldContain("conversation_memory");
        exported.ShouldContain("teal");
        exported.ShouldNotContain("plum");

        var order = new List<string>();
        var erased = await store.EraseAsync(
            "default",
            scope,
            (counts, _) =>
            {
                order.Add($"audit sees {counts["conversation_memory"]} memory records, {memory.Records(Collection).Count} left");

                return ValueTask.CompletedTask;
            });

        erased["conversation_memory"].ShouldBe(2);
        erased["sessions"].ShouldBe(1);
        order.ShouldBe(["audit sees 2 memory records, 2 left"], "the copy is erased before the audit callback, which runs before the commit");
        memory.Records(Collection).Select(static record => (string?)record["SessionId"]).Distinct(StringComparer.Ordinal)
            .ShouldBe(["grace-session"]);
        (await host.Services.GetRequiredService<ISessionStore>().GetAsync("ada-session")).ShouldBeNull();
        (await host.Services.GetRequiredService<ISessionStore>().GetAsync("grace-session")).ShouldNotBeNull();

        memory.Dispose();
    }

    [Fact]
    public async Task A_store_that_cannot_delete_fails_the_SQL_erasure_before_any_row_is_deleted_and_writes_no_audit_entry()
    {
        using var database = new TempSqliteDatabase("memory-rollback");
        var memory = new InMemoryVectorStore();

        await using var host = await StartAsync(database, memory);

        await RunAsync(host, "ada", "ada-session", Colour);
        memory.FailOn = static operation => string.Equals(operation, "delete", StringComparison.Ordinal) ? new InvalidOperationException("store is down") : null;

        var store = host.Services.GetRequiredService<IDataSubjectStore>();
        var scope = new DataSubjectScope { SessionIds = ["ada-session"], RunIds = [], ConversationIds = [] };
        var audited = false;

        await Should.ThrowAsync<InvalidOperationException>(async () => await store.EraseAsync(
            "default",
            scope,
            (_, _) =>
            {
                audited = true;

                return ValueTask.CompletedTask;
            }));

        audited.ShouldBeFalse();
        (await host.Services.GetRequiredService<ISessionStore>().GetAsync("ada-session")).ShouldNotBeNull("the SQL rows rolled back");
        memory.Records(Collection).ShouldNotBeEmpty();

        memory.FailOn = null;

        (await store.EraseAsync("default", scope, static (_, _) => ValueTask.CompletedTask))["conversation_memory"].ShouldBe(2);
        memory.Records(Collection).ShouldBeEmpty();
        (await host.Services.GetRequiredService<ISessionStore>().GetAsync("ada-session")).ShouldBeNull();
    }

    [Fact]
    public async Task The_sessions_retention_target_over_SQL_deletes_the_memory_of_each_expired_session_in_its_own_tenant()
    {
        using var database = new TempSqliteDatabase("memory-retention");
        var store = new InMemoryVectorStore();

        await using var host = await StartAsync(database, store);

        // The same user and the same session id in two tenants: two sessions.
        await RunAsync(host, "ada", "shared-session", Colour, tenant: "tenant-a");
        await RunAsync(host, "ada", "shared-session", "My favourite colour is plum.", tenant: "tenant-b");
        await RunAsync(host, "ada", "tenant-a-only", "I live in Lisbon.", tenant: "tenant-a");
        store.Records(Collection).Count.ShouldBe(6);

        var retention = host.Services.GetRequiredService<IRetentionStore>();

        var deleted = await retention.DeleteBatchAsync(
            RetentionTargets.Sessions, tenantId: "tenant-a", DateTimeOffset.UtcNow.AddDays(1), batchSize: 50);

        deleted.ShouldBe(2);
        store.Records(Collection).Select(static record => (string?)record["ApplicationId"]).Distinct(StringComparer.Ordinal)
            .ShouldBe(["tenant-b"], "only tenant-a's sessions expired");

        deleted = await retention.DeleteBatchAsync(
            RetentionTargets.Sessions, tenantId: null, DateTimeOffset.UtcNow.AddDays(1), batchSize: 50);

        deleted.ShouldBe(1);
        store.Records(Collection).ShouldBeEmpty();

        store.Dispose();
    }

    [Fact]
    public async Task A_retention_run_that_archives_hands_the_sink_the_rows_then_erases_the_memory()
    {
        using var database = new TempSqliteDatabase("memory-archive");
        var store = new InMemoryVectorStore();

        await using var host = await StartAsync(database, store);

        await RunAsync(host, "ada", "ada-session", Colour);

        var seenByTheSink = -1;

        var deleted = await host.Services.GetRequiredService<IRetentionStore>().ArchiveBatchAsync(
            RetentionTargets.Sessions,
            tenantId: null,
            DateTimeOffset.UtcNow.AddDays(1),
            batchSize: 50,
            (rows, _) =>
            {
                seenByTheSink = store.Records(Collection).Count;

                return ValueTask.CompletedTask;
            });

        deleted.ShouldBe(1);
        seenByTheSink.ShouldBe(2, "the memory is still there while the sink holds the rows");
        store.Records(Collection).ShouldBeEmpty();

        store.Dispose();
    }

    [Fact]
    public async Task A_tenant_context_that_does_not_normalize_still_finds_its_records_to_delete()
    {
        // AmbientTenantScope.Normalize is the written rule; a tenant context of a host may break it. The record
        // is written under the normalized form, so the eraser must look under it.
        var store = new InMemoryVectorStore();
        var eraser = new ConversationMemoryEraser(new ServiceCollection()
            .AddSingleton<VectorStore>(store)
            .AddOptions<TraconConversationMemoryOptions>().Configure(static options =>
            {
                options.CollectionName = Collection;
                options.VectorDimensions = 8;
            }).Services
            .BuildServiceProvider());

        using (var collection = store.GetDynamicCollection(Collection, ConversationMemorySchema.CreateDefinition(8)))
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Key"] = Guid.NewGuid(),
                ["Role"] = "user",
                ["MessageId"] = null,
                ["AuthorName"] = null,
                ["ApplicationId"] = "acme",
                ["AgentId"] = "memory-agent",
                ["UserId"] = "ada",
                ["SessionId"] = "s1",
                ["Content"] = Colour,
                ["CreatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["ContentEmbedding"] = Colour,
            });
        }

        (await eraser.EraseAsync("Acme", ["s1"], CancellationToken.None)).ShouldBe(1);
        store.Records(Collection).ShouldBeEmpty();

        store.Dispose();
    }

    private static Task<TraconTestHost> StartAsync(TempSqliteDatabase database, InMemoryVectorStore store)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                builder.AddModelProvider(new FakeModelProvider("memory-model").ForModel("memory-1", static cfg => cfg.EchoesUserMessage()));
                builder.UseConversationMemory(static options =>
                {
                    options.CollectionName = Collection;
                    options.VectorDimensions = 8;
                });
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.AddAgent(new AgentDefinition
                {
                    Name = "memory-agent",
                    Instructions = "Reply briefly.",
                    Model = new ModelBinding { Provider = "memory-model", Model = "memory-1" },
                    Memory = new MemorySettings { EnableConversationMemory = true },
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services =>
            {
                services.AddHttpContextAccessor();
                services.AddSingleton<VectorStore>(store);
                services.AddSingleton<IRunAttributionContext>(static provider =>
                    new HeaderAttribution(provider.GetRequiredService<IHttpContextAccessor>()));
            });

    private static async Task RunAsync(TraconTestHost host, string user, string sessionId, string message, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/memory-agent/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = message, SessionId = sessionId }),
        };
        request.Headers.Add(UserHeader, user);

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
    }

    private sealed class HeaderAttribution(IHttpContextAccessor accessor) : IRunAttributionContext
    {
        public string? UserId => accessor.HttpContext is { } context
            && context.Request.Headers.TryGetValue(UserHeader, out var user)
            && user.ToString() is { Length: > 0 } value
                ? value
                : null;

        public IReadOnlyDictionary<string, string>? Labels => null;
    }
}
