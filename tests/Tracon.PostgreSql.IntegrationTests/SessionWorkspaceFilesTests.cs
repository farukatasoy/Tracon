using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The PostgreSQL file store reads, measures and deletes the files of one session's directory, by tenant and
/// path and with no agent or run in scope, and a data subject erasure over PostgreSQL takes the workspace with the
/// session.
/// </summary>
/// <remarks>
/// The store answers an erasure of a session, which runs outside any run, and a quota check that must not
/// scan the whole table. The end-to-end tests drive the real eraser over the real store.
/// </remarks>
// MAAI001: AgentFileStore is marked "evaluation purposes only" - the same rationale as the product code.
#pragma warning disable MAAI001
public sealed class SessionWorkspaceFilesTests(PostgresFixture fixture) : IDisposable
{
    private const string Tenant = "tenant-a";
    private const string EmptyJsonState = "'{}'";

    public void Dispose() => TraconRunContext.SetCurrent(null);

    [Fact]
    public async Task The_SQL_store_is_the_file_tree_the_eraser_looks_for()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        // The eraser and the quota check take the SQL answer when the store is an IAgentFileTree and walk the store
        // file by file when it is not; walking a SQL store would need an agent, and an erasure has none.
        ((AgentFileStore)context.AgentFiles).ShouldBeAssignableTo<IAgentFileTree>();
    }

    [Fact]
    public async Task The_size_of_a_directory_counts_its_files_and_their_bytes_in_UTF8_whichever_agent_wrote_them()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var tree = context.AgentFiles;
        var root = SessionWorkspacePaths.SessionRoot(Tenant, "session-1");

        await WriteAsync(context, "drafter", root + "/drafter/notes.md", "alpha");
        await WriteAsync(context, "drafter", root + "/drafter/reports/q3.md", "é€");
        await WriteAsync(context, "reviewer", root + "/reviewer/notes.md", "beta");
        await WriteAsync(context, "drafter", SessionWorkspacePaths.SessionRoot(Tenant, "session-2") + "/drafter/notes.md", "another session");

        var usage = await tree.MeasureAsync(Tenant, root, CancellationToken.None);

        usage.Files.ShouldBe(3);
        usage.Bytes.ShouldBe(5 + 5 + 4, "'é€' is 2 + 3 bytes in UTF-8");
        (await tree.MeasureAsync(Tenant, SessionWorkspacePaths.TenantRoot(Tenant), CancellationToken.None)).Files.ShouldBe(4);
    }

    [Fact]
    public async Task A_directory_with_no_files_measures_zero()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        (await context.AgentFiles.MeasureAsync(Tenant, "tenant-a/~workspace/nothing", CancellationToken.None)).ShouldBe(default);
    }

    [Fact]
    public async Task The_files_of_a_directory_are_read_with_their_path_below_it()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var root = SessionWorkspacePaths.SessionRoot(Tenant, "session-1");
        await WriteAsync(context, "drafter", root + "/drafter/notes.md", "alpha");
        await WriteAsync(context, "reviewer", root + "/reviewer/reports/q3.md", "beta");

        var files = await context.AgentFiles.ReadAsync(Tenant, root, CancellationToken.None);

        files.Select(static file => (file.Path, file.Content)).ShouldBe([("drafter/notes.md", "alpha"), ("reviewer/reports/q3.md", "beta")]);
    }

    [Fact]
    public async Task A_delete_takes_the_directory_and_nothing_beside_it()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var tree = context.AgentFiles;
        var root = SessionWorkspacePaths.SessionRoot(Tenant, "session-1");
        await WriteAsync(context, "drafter", root + "/drafter/notes.md", "alpha");
        await WriteAsync(context, "reviewer", root + "/reviewer/notes.md", "beta");
        await WriteAsync(context, "drafter", SessionWorkspacePaths.SessionRoot(Tenant, "session-2") + "/drafter/notes.md", "another session");
        await WriteAsync(context, "drafter", "tenant-a/drafter/memory.md", "file memory of the agent");

        (await tree.DeleteAsync(Tenant, root, CancellationToken.None)).ShouldBe(2);

        (await tree.MeasureAsync(Tenant, root, CancellationToken.None)).Files.ShouldBe(0);
        (await tree.MeasureAsync(Tenant, SessionWorkspacePaths.SessionRoot(Tenant, "session-2"), CancellationToken.None)).Files.ShouldBe(1);
        (await Read(context, "drafter", "tenant-a/drafter/memory.md")).ShouldBe("file memory of the agent");
    }

    [Fact]
    public async Task A_delete_never_reaches_another_tenant_that_has_the_same_path()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var tree = context.AgentFiles;
        var path = "shared/~workspace/key/drafter/notes.md";
        await WriteAsync(context, "drafter", path, "alpha", tenant: "tenant-a");
        await WriteAsync(context, "drafter", path, "beta", tenant: "tenant-b");

        (await tree.DeleteAsync("tenant-a", "shared/~workspace/key", CancellationToken.None)).ShouldBe(1);

        (await tree.MeasureAsync("tenant-b", "shared/~workspace/key", CancellationToken.None)).Files.ShouldBe(1);
    }

    [Fact]
    public async Task A_character_that_LIKE_reads_as_a_wildcard_matches_only_itself()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var tree = context.AgentFiles;
        await WriteAsync(context, "drafter", "acme_1/~workspace/key/drafter/notes.md", "underscore");
        await WriteAsync(context, "drafter", "acmeX1/~workspace/key/drafter/notes.md", "other");
        await WriteAsync(context, "drafter", "100%/~workspace/key/drafter/notes.md", "percent");
        await WriteAsync(context, "drafter", "100x/~workspace/key/drafter/notes.md", "another");

        (await tree.DeleteAsync(Tenant, "acme_1/~workspace/key", CancellationToken.None)).ShouldBe(1);
        (await tree.DeleteAsync(Tenant, "100%/~workspace/key", CancellationToken.None)).ShouldBe(1);

        (await Read(context, "drafter", "acmeX1/~workspace/key/drafter/notes.md")).ShouldBe("other");
        (await Read(context, "drafter", "100x/~workspace/key/drafter/notes.md")).ShouldBe("another");
    }

    [Fact]
    public async Task A_directory_that_is_a_prefix_of_another_does_not_take_the_other()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var tree = context.AgentFiles;
        await WriteAsync(context, "drafter", "tenant-a/~workspace/abc/drafter/notes.md", "one");
        await WriteAsync(context, "drafter", "tenant-a/~workspace/abcdef/drafter/notes.md", "two");

        (await tree.DeleteAsync(Tenant, "tenant-a/~workspace/abc", CancellationToken.None)).ShouldBe(1);

        (await Read(context, "drafter", "tenant-a/~workspace/abcdef/drafter/notes.md")).ShouldBe("two");
    }

    [Fact]
    public async Task An_empty_directory_is_refused_because_it_would_match_every_file_of_the_tenant()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var tree = context.AgentFiles;
        await WriteAsync(context, "drafter", "tenant-a/drafter/memory.md", "kept");

        await Should.ThrowAsync<ArgumentException>(async () => await tree.DeleteAsync(Tenant, string.Empty, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(async () => await tree.DeleteAsync(Tenant, "  ", CancellationToken.None));

        (await Read(context, "drafter", "tenant-a/drafter/memory.md")).ShouldBe("kept");
    }

    [Fact]
    public async Task The_workspace_store_over_the_SQL_store_keeps_sessions_and_tenants_apart_and_never_shows_the_stored_path()
    {
        // The unit tests stand a double in for the SQL store. This is the combination a host runs: the SQL store answers
        // a search with the whole stored path (a leading slash, the tenant and the key of the session), and the model must
        // read names relative to the directory it asked about.
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var options = new FixedWorkspaceOptions(new TraconWorkspaceOptions { MaxFileBytes = 1000, MaxSessionBytes = 100_000, MaxTenantBytes = 1_000_000, FileOverheadBytes = 0 });
        var tenantA = new SessionWorkspaceFileStore(new SqlAgentFileStore(context.StoreContext, new FixedTenantContext("tenant-a")), context.AgentFiles, "tenant-a", "drafter", options);
        var tenantB = new SessionWorkspaceFileStore(new SqlAgentFileStore(context.StoreContext, new FixedTenantContext("tenant-b")), context.AgentFiles, "tenant-b", "drafter", options);

        using (new RunScope("drafter", "tenant-a", "s1"))
        {
            await tenantA.WriteAsync("notes.md", "alpha");
            await tenantA.WriteAsync("reports/q3.md", "alpha beta");
        }

        using (new RunScope("drafter", "tenant-a", "s2"))
        {
            (await tenantA.ListChildrenAsync(string.Empty)).ShouldBeEmpty("another session starts empty");
            (await tenantA.SearchAsync(string.Empty, "alpha", globPattern: null, recursive: true)).ShouldBeEmpty();
            (await tenantA.ReadAsync("notes.md")).ShouldBeNull();
        }

        using (new RunScope("drafter", "tenant-b", "s1"))
        {
            (await tenantB.ListChildrenAsync(string.Empty)).ShouldBeEmpty("another tenant with the same session id starts empty");
            (await tenantB.SearchAsync(string.Empty, "alpha", globPattern: null, recursive: true)).ShouldBeEmpty();
        }

        using (new RunScope("drafter", "tenant-a", "s1"))
        {
            (await tenantA.ListChildrenAsync(string.Empty)).Select(static entry => (entry.Name, entry.Type)).Order()
                .ShouldBe([("notes.md", "file"), ("reports", "directory")]);
            (await tenantA.ListChildrenAsync("reports")).Select(static entry => entry.Name).ShouldBe(["q3.md"]);

            (await tenantA.SearchAsync(string.Empty, "alpha", globPattern: null, recursive: true)).Select(static match => match.FileName).Order(StringComparer.Ordinal)
                .ShouldBe(["notes.md", "reports/q3.md"]);
            (await tenantA.SearchAsync("reports", "beta", globPattern: null, recursive: true)).Select(static match => match.FileName).ShouldBe(["q3.md"]);
            (await tenantA.ReadAsync("reports/q3.md")).ShouldBe("alpha beta");
        }

        // What the store holds: one directory per session, below the tenant, below the key.
        var stored = await context.AgentFiles.ReadAsync("tenant-a", SessionWorkspacePaths.SessionRoot("tenant-a", "s1"), CancellationToken.None);
        stored.Select(static file => file.Path).Order(StringComparer.Ordinal).ShouldBe(["drafter/notes.md", "drafter/reports/q3.md"]);
    }

    [Fact]
    public async Task A_data_subject_erasure_over_PostgreSQL_takes_the_workspace_of_the_sessions_in_the_same_call()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "session-1", Tenant);
        await SeedSessionAsync(context, "session-2", Tenant);
        var root1 = SessionWorkspacePaths.SessionRoot(Tenant, "session-1");
        await WriteAsync(context, "drafter", root1 + "/drafter/notes.md", "alpha");
        await WriteAsync(context, "reviewer", root1 + "/reviewer/notes.md", "beta");
        await WriteAsync(context, "drafter", SessionWorkspacePaths.SessionRoot(Tenant, "session-2") + "/drafter/notes.md", "another session");
        var store = new SqlDataSubjectStore(context.StoreContext, contentErasers: [Eraser(context)]);
        var scope = new DataSubjectScope { SessionIds = ["session-1"], RunIds = [], ConversationIds = [] };

        var preview = await store.PreviewAsync(Tenant, scope);
        preview["session_workspace"].ShouldBe(2);

        using (var export = JsonDocument.Parse((await store.ExportAsync(Tenant, scope)).Json))
        {
            var files = export.RootElement.GetProperty("session_workspace").EnumerateArray().ToList();
            files.Select(static file => file.GetProperty("agent").GetString()).Order(StringComparer.Ordinal).ShouldBe(["drafter", "reviewer"]);
            files.ShouldAllBe(static file => file.GetProperty("sessionId").GetString() == "session-1");
        }

        (await context.AgentFiles.MeasureAsync(Tenant, root1, CancellationToken.None)).Files.ShouldBe(2, "a preview and an export delete nothing");

        var erased = await store.EraseAsync(Tenant, scope, static (_, _) => ValueTask.CompletedTask);

        erased["session_workspace"].ShouldBe(2);
        (await context.AgentFiles.MeasureAsync(Tenant, root1, CancellationToken.None)).Files.ShouldBe(0);
        (await context.AgentFiles.MeasureAsync(Tenant, SessionWorkspacePaths.SessionRoot(Tenant, "session-2"), CancellationToken.None)).Files.ShouldBe(1);
        (await SessionExistsAsync(context, "session-1", Tenant)).ShouldBeFalse();
        (await SessionExistsAsync(context, "session-2", Tenant)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_sessions_retention_target_over_PostgreSQL_takes_the_workspace_of_each_expired_session()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "old-session", Tenant);
        var root = SessionWorkspacePaths.SessionRoot(Tenant, "old-session");
        await WriteAsync(context, "drafter", root + "/drafter/notes.md", "alpha");
        var retention = new SqlRetentionStore(context.StoreContext, contentErasers: [Eraser(context)]);

        var deleted = await retention.DeleteBatchAsync(RetentionTargets.Sessions, tenantId: Tenant, DateTimeOffset.UtcNow.AddDays(1), batchSize: 50);

        deleted.ShouldBe(1);
        (await context.AgentFiles.MeasureAsync(Tenant, root, CancellationToken.None)).Files.ShouldBe(0);
    }

    private static SessionWorkspaceEraser Eraser(PostgresTestContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton<AgentFileStore>(context.AgentFiles);

        return new SessionWorkspaceEraser(services.BuildServiceProvider());
    }

    /// <summary>Writes through the store the way a run does: inside the scope of a run of <paramref name="agent"/>.</summary>
    private static async Task WriteAsync(PostgresTestContext context, string agent, string path, string content, string tenant = Tenant)
    {
        using var scope = new RunScope(agent);
        var store = new SqlAgentFileStore(context.StoreContext, new FixedTenantContext(tenant));

        await store.WriteAsync(path, content);
    }

    private static async Task<string?> Read(PostgresTestContext context, string agent, string path, string tenant = Tenant)
    {
        using var scope = new RunScope(agent);

        return await new SqlAgentFileStore(context.StoreContext, new FixedTenantContext(tenant)).ReadAsync(path);
    }

    private static async Task SeedSessionAsync(PostgresTestContext context, string id, string tenantId)
        => await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', {EmptyJsonState}, 1, now() - interval '5 years', now() - interval '5 years');
            """);

    private static async Task<bool> SessionExistsAsync(PostgresTestContext context, string id, string tenantId)
        => await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {context.SchemaName}.sessions WHERE id = '{id}' AND tenant_id = '{tenantId}';") > 0;

    /// <summary>Sets the scope of a run of one agent, as the run does, and clears it.</summary>
    private sealed class RunScope : IDisposable
    {
        public RunScope(string agent, string? tenant = null, string? session = null)
            => TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), AgentName = agent, TenantId = tenant, SessionId = session });

        public void Dispose() => TraconRunContext.SetCurrent(null);
    }

    private sealed class FixedWorkspaceOptions(TraconWorkspaceOptions value) : Microsoft.Extensions.Options.IOptionsMonitor<TraconWorkspaceOptions>
    {
        public TraconWorkspaceOptions CurrentValue => value;

        public TraconWorkspaceOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TraconWorkspaceOptions, string?> listener) => null;
    }
}
#pragma warning restore MAAI001
