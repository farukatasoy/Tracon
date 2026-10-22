using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Workspace;

/// <summary>
/// What the erasure of a session takes and what it leaves: the files of that session, whichever agent wrote them,
/// in that tenant only.
/// </summary>
// MAAI001: AgentFileStore is marked "evaluation purposes only" - see K-020.
#pragma warning disable MAAI001
public sealed class SessionWorkspaceEraserTests : IDisposable
{
    private readonly InMemoryAgentFileStore _raw = new();

    public void Dispose() => TraconRunContext.SetCurrent(null);

    [Fact]
    public async Task It_counts_erases_and_exports_the_files_of_the_session_of_every_agent_and_leaves_the_rest()
    {
        await WriteAsync("acme", "session-1", "drafter", "notes.md", "alpha");
        await WriteAsync("acme", "session-1", "drafter", "reports/q3.md", "beta");
        await WriteAsync("acme", "session-1", "reviewer", "notes.md", "gamma");
        await WriteAsync("acme", "session-2", "drafter", "notes.md", "other session");
        await WriteAsync("other", "session-1", "drafter", "notes.md", "other tenant");
        await _raw.WriteAsync("acme/drafter/memory.md", "file memory of the agent");
        var eraser = Eraser();

        (await eraser.CountAsync("acme", ["session-1"], CancellationToken.None)).ShouldBe(3);
        (await ReadAsync("acme", "session-1", "drafter", "notes.md")).ShouldBe("alpha", "a count deletes nothing");

        var exported = await ExportAsync(eraser, "acme", ["session-1"]);
        exported.Select(static file => (file.Agent, file.Path, file.Content)).Order().ShouldBe(
        [
            ("drafter", "notes.md", "alpha"),
            ("drafter", "reports/q3.md", "beta"),
            ("reviewer", "notes.md", "gamma"),
        ]);
        exported.ShouldAllBe(static file => file.SessionId == "session-1");

        (await eraser.EraseAsync("acme", ["session-1"], CancellationToken.None)).ShouldBe(3);

        (await ReadAsync("acme", "session-1", "drafter", "notes.md")).ShouldBeNull();
        (await ReadAsync("acme", "session-1", "drafter", "reports/q3.md")).ShouldBeNull();
        (await ReadAsync("acme", "session-1", "reviewer", "notes.md")).ShouldBeNull();
        (await ReadAsync("acme", "session-2", "drafter", "notes.md")).ShouldBe("other session");
        (await ReadAsync("other", "session-1", "drafter", "notes.md")).ShouldBe("other tenant", "a session id is unique inside one tenant only");
        (await _raw.ReadAsync("acme/drafter/memory.md")).ShouldBe("file memory of the agent", "the file memory belongs to the agent, not to the session");
    }

    [Fact]
    public async Task It_finds_the_files_of_a_tenant_written_in_another_letter_case()
    {
        await WriteAsync("acme", "session-1", "drafter", "notes.md", "alpha");

        (await Eraser().EraseAsync("ACME", ["session-1"], CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task It_erases_several_sessions_and_ignores_an_empty_id()
    {
        await WriteAsync("acme", "session-1", "drafter", "notes.md", "alpha");
        await WriteAsync("acme", "session-2", "drafter", "notes.md", "beta");
        await WriteAsync("acme", "session-3", "drafter", "notes.md", "gamma");

        (await Eraser().EraseAsync("acme", ["session-1", string.Empty, "session-2"], CancellationToken.None)).ShouldBe(2);

        (await ReadAsync("acme", "session-3", "drafter", "notes.md")).ShouldBe("gamma");
    }

    [Fact]
    public async Task A_session_with_no_files_reports_none()
    {
        var eraser = Eraser();

        (await eraser.CountAsync("acme", ["nobody"], CancellationToken.None)).ShouldBe(0);
        (await eraser.EraseAsync("acme", ["nobody"], CancellationToken.None)).ShouldBe(0);
        (await ExportAsync(eraser, "acme", ["nobody"])).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_store_that_cannot_delete_fails_the_erasure_and_a_repeat_call_finishes_it()
    {
        await WriteAsync("acme", "session-1", "drafter", "a.md", "alpha");
        await WriteAsync("acme", "session-1", "drafter", "b.md", "beta");
        var flaky = new FlakyDeleteStore(_raw);
        var eraser = Eraser(flaky);

        await Should.ThrowAsync<InvalidOperationException>(async () => await eraser.EraseAsync("acme", ["session-1"], CancellationToken.None));
        (await eraser.CountAsync("acme", ["session-1"], CancellationToken.None)).ShouldBeGreaterThan(0, "the files that were not deleted are still there");

        flaky.Fail = false;

        (await eraser.EraseAsync("acme", ["session-1"], CancellationToken.None)).ShouldBeGreaterThan(0);
        (await eraser.CountAsync("acme", ["session-1"], CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task A_store_that_cannot_list_outside_a_run_fails_the_erasure_instead_of_leaving_files_behind()
    {
        // A host that wraps a run-scoped store loses the one-query path and is walked file by file. A listing that
        // fails must stop the erasure: a quiet zero would tell the audit trail a workspace was erased that was not.
        var eraser = Eraser(new FailingListStore());

        await Should.ThrowAsync<InvalidOperationException>(async () => await eraser.EraseAsync("acme", ["session-1"], CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(async () => await eraser.CountAsync("acme", ["session-1"], CancellationToken.None));
    }

    [Fact]
    public async Task A_store_that_answers_for_a_whole_directory_is_asked_once_per_session_and_is_not_walked()
    {
        var tree = new TreeStore();
        var eraser = Eraser(tree);

        (await eraser.EraseAsync("acme", ["session-1", "session-2"], CancellationToken.None)).ShouldBe(7);
        (await eraser.CountAsync("acme", ["session-1"], CancellationToken.None)).ShouldBe(3);

        tree.Deleted.ShouldBe(
        [
            ("acme", SessionWorkspacePaths.SessionRoot("acme", "session-1")),
            ("acme", SessionWorkspacePaths.SessionRoot("acme", "session-2")),
        ]);
        tree.Walked.ShouldBeFalse("the tree interface replaces the walk");
    }

    private SessionWorkspaceEraser Eraser(AgentFileStore? store = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store ?? _raw);

        return new SessionWorkspaceEraser(services.BuildServiceProvider());
    }

    private async Task WriteAsync(string tenant, string session, string agent, string path, string content)
        => await _raw.WriteAsync(SessionWorkspacePaths.AgentRoot(tenant, session, agent) + "/" + path, content);

    private async Task<string?> ReadAsync(string tenant, string session, string agent, string path)
        => await _raw.ReadAsync(SessionWorkspacePaths.AgentRoot(tenant, session, agent) + "/" + path);

    private static async Task<List<(string SessionId, string Agent, string Path, string Content)>> ExportAsync(
        SessionWorkspaceEraser eraser,
        string tenant,
        IReadOnlyList<string> sessions)
    {
        using var stream = new MemoryStream();

        await using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            await eraser.WriteExportAsync(tenant, sessions, writer, CancellationToken.None);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));

        return document.RootElement.GetProperty(eraser.Target).EnumerateArray()
            .Select(static file => (
                file.GetProperty("sessionId").GetString()!,
                file.GetProperty("agent").GetString()!,
                file.GetProperty("path").GetString()!,
                file.GetProperty("content").GetString()!))
            .ToList();
    }

    /// <summary>A store that fails every delete until told otherwise.</summary>
    private sealed class FlakyDeleteStore(AgentFileStore inner) : AgentFileStore
    {
        public bool Fail { get; set; } = true;

        public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) => inner.WriteAsync(path, content, cancellationToken);

        public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default) => inner.ReadAsync(path, cancellationToken);

        public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
            => Fail ? throw new InvalidOperationException("store is down") : inner.DeleteAsync(path, cancellationToken);

        public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => inner.FileExistsAsync(path, cancellationToken);

        public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => inner.CreateDirectoryAsync(path, cancellationToken);

        public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
            => inner.ListChildrenAsync(directory, cancellationToken);

        public override Task<IReadOnlyList<FileSearchResult>> SearchAsync(
            string directory,
            string regexPattern,
            string? globPattern = null,
            bool recursive = false,
            CancellationToken cancellationToken = default)
            => inner.SearchAsync(directory, regexPattern, globPattern, recursive, cancellationToken);
    }

    /// <summary>A store that fails to list, as a run-scoped store does when it is called outside a run.</summary>
    private sealed class FailingListStore : AgentFileStore
    {
        public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("this store can only be used within a run");

        public override Task<IReadOnlyList<FileSearchResult>> SearchAsync(
            string directory,
            string regexPattern,
            string? globPattern = null,
            bool recursive = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileSearchResult>>([]);
    }

    /// <summary>A store that answers for a whole directory, as the SQL store does, and fails when it is walked.</summary>
    private sealed class TreeStore : AgentFileStore, IAgentFileTree
    {
        public List<(string Tenant, string Directory)> Deleted { get; } = [];

        public bool Walked { get; private set; }

        public ValueTask<AgentFileTreeUsage> MeasureAsync(string tenantId, string directory, CancellationToken cancellationToken)
            => new(new AgentFileTreeUsage(3, 30));

        public ValueTask<IReadOnlyList<AgentFileTreeEntry>> ReadAsync(string tenantId, string directory, CancellationToken cancellationToken)
            => new([]);

        public ValueTask<int> DeleteAsync(string tenantId, string directory, CancellationToken cancellationToken)
        {
            Deleted.Add((tenantId, directory));

            return new(Deleted.Count == 1 ? 4 : 3);
        }

        public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) => Walk();

        public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default) => Walk<string?>();

        public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default) => Walk<bool>();

        public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => Walk<bool>();

        public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Walk();

        public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
            => Walk<IReadOnlyList<FileStoreEntry>>();

        public override Task<IReadOnlyList<FileSearchResult>> SearchAsync(
            string directory,
            string regexPattern,
            string? globPattern = null,
            bool recursive = false,
            CancellationToken cancellationToken = default)
            => Walk<IReadOnlyList<FileSearchResult>>();

        private Task Walk()
        {
            Walked = true;

            throw new InvalidOperationException("the store was walked");
        }

        private Task<T> Walk<T>()
        {
            Walked = true;

            throw new InvalidOperationException("the store was walked");
        }
    }
}
#pragma warning restore MAAI001
