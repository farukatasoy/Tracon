using System.Text;
using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Workspace;

/// <summary>
/// What a session workspace store keeps apart and what it refuses: whose files a run reads and writes, a path that
/// would leave the workspace, and a write that would pass a limit.
/// </summary>
/// <remarks>
/// The store reads the session from the run that is calling (<see cref="TraconRunContext"/>, an <c>AsyncLocal</c>),
/// so each test sets the scope in its own body and the class clears it.
/// </remarks>
// MAAI001: AgentFileStore is marked "evaluation purposes only"
#pragma warning disable MAAI001
public sealed class SessionWorkspaceFileStoreTests : IDisposable
{
    private readonly InMemoryAgentFileStore _raw = new();

    public void Dispose() => TraconRunContext.SetCurrent(null);

    [Fact]
    public async Task A_file_is_kept_below_the_directory_of_the_tenant_the_session_and_the_agent()
    {
        var store = WorkspaceTestSupport.Store(_raw);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        await store.WriteAsync("reports/q3.md", "alpha");

        (await _raw.ReadAsync(SessionWorkspacePaths.AgentRoot("acme", "session-1", "drafter") + "/reports/q3.md")).ShouldBe("alpha");
        (await store.ReadAsync("/reports/q3.md")).ShouldBe("alpha", "a leading slash and a backslash name the same file");
        (await store.ReadAsync("reports\\q3.md")).ShouldBe("alpha");
    }

    [Fact]
    public async Task Another_session_does_not_see_the_file()
    {
        var store = WorkspaceTestSupport.Store(_raw);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await store.WriteAsync("notes.md", "alpha");

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-2"));

        (await store.ReadAsync("notes.md")).ShouldBeNull();
        (await store.FileExistsAsync("notes.md")).ShouldBeFalse();
        (await store.ListChildrenAsync(string.Empty)).ShouldBeEmpty();
        (await store.SearchAsync(string.Empty, "alpha", globPattern: null, recursive: true)).ShouldBeEmpty();
        (await store.DeleteAsync("notes.md")).ShouldBeFalse();

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        (await store.ReadAsync("notes.md")).ShouldBe("alpha", "the delete from another session touched nothing");
    }

    [Fact]
    public async Task Another_tenant_does_not_see_the_file_even_with_the_same_session_id_and_agent()
    {
        var acme = WorkspaceTestSupport.Store(_raw, tenant: "acme");
        var other = WorkspaceTestSupport.Store(_raw, tenant: "other");
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "shared-session"));
        await acme.WriteAsync("notes.md", "alpha");

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("other", "drafter", "shared-session"));

        (await other.ReadAsync("notes.md")).ShouldBeNull();
    }

    [Fact]
    public async Task Another_agent_in_the_same_session_does_not_see_the_file()
    {
        var drafter = WorkspaceTestSupport.Store(_raw, agent: "drafter");
        var reviewer = WorkspaceTestSupport.Store(_raw, agent: "reviewer");
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await drafter.WriteAsync("notes.md", "alpha");

        (await reviewer.ReadAsync("notes.md")).ShouldBeNull();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../session-2/notes.md")]
    [InlineData("reports/../../notes.md")]
    [InlineData("./notes.md")]
    public async Task A_path_that_leaves_the_workspace_is_refused_by_every_operation(string path)
    {
        var store = WorkspaceTestSupport.Store(_raw);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        await Should.ThrowAsync<TraconException>(async () => await store.ReadAsync(path));
        await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync(path, "x"));
        await Should.ThrowAsync<TraconException>(async () => await store.DeleteAsync(path));
        await Should.ThrowAsync<TraconException>(async () => await store.FileExistsAsync(path));
        await Should.ThrowAsync<TraconException>(async () => await store.ListChildrenAsync(path));
        await Should.ThrowAsync<TraconException>(async () => await store.SearchAsync(path, "x", globPattern: null, recursive: true));
    }

    [Fact]
    public async Task A_path_longer_than_the_narrowest_column_is_refused_and_says_how_long_it_may_be()
    {
        var store = WorkspaceTestSupport.Store(_raw);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        var root = SessionWorkspacePaths.AgentRoot("acme", "session-1", "drafter");
        var room = SessionWorkspacePaths.MaxPathLength - 2 - root.Length;

        await store.WriteAsync(new string('a', room), "fits");
        var tooLong = await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync(new string('a', room + 1), "does not"));

        tooLong.Message.ShouldContain($"{room} characters");
    }

    [Fact]
    public async Task A_run_with_no_session_has_no_workspace()
    {
        var store = WorkspaceTestSupport.Store(_raw);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", session: null));

        store.HasWorkspace.ShouldBeFalse();
        await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("notes.md", "alpha"));
        await Should.ThrowAsync<TraconException>(async () => await store.ReadAsync("notes.md"));
    }

    [Fact]
    public async Task A_run_whose_session_has_no_record_has_no_workspace_because_nothing_could_delete_what_it_kept()
    {
        var store = WorkspaceTestSupport.Store(_raw);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "workflow-session", sessionHasNoRecord: true));

        store.HasWorkspace.ShouldBeFalse();
        await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("notes.md", "alpha"));
    }

    [Fact]
    public async Task A_run_of_another_tenant_than_the_agent_was_compiled_for_has_no_workspace()
    {
        var store = WorkspaceTestSupport.Store(_raw, tenant: "acme");
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("other", "drafter", "session-1"));

        store.HasWorkspace.ShouldBeFalse();
        await Should.ThrowAsync<TraconException>(async () => await store.ReadAsync("notes.md"));
    }

    [Fact]
    public void Outside_a_run_there_is_no_workspace()
    {
        TraconRunContext.SetCurrent(null);

        WorkspaceTestSupport.Store(_raw).HasWorkspace.ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_may_hold_as_many_bytes_as_the_limit_and_no_more()
    {
        var store = WorkspaceTestSupport.Store(_raw, limits: WorkspaceTestSupport.Limits(file: 10));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        await store.WriteAsync("fits.md", new string('a', 10));
        var refused = await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("big.md", new string('a', 11)));

        refused.Message.ShouldStartWith("Workspace quota reached");
        refused.Message.ShouldContain("10 bytes");
        (await store.FileExistsAsync("big.md")).ShouldBeFalse("a refused write stores nothing");
    }

    [Fact]
    public async Task The_limit_counts_bytes_and_not_characters()
    {
        var store = WorkspaceTestSupport.Store(_raw, limits: WorkspaceTestSupport.Limits(file: 10));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        // Six characters, twelve bytes in UTF-8.
        var text = new string('é', 6);
        Encoding.UTF8.GetByteCount(text).ShouldBe(12);

        await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("accents.md", text));
    }

    [Fact]
    public async Task The_files_of_a_session_may_hold_as_many_bytes_as_the_limit_across_agents_and_no_more()
    {
        var limits = WorkspaceTestSupport.Limits(file: 10, session: 25, tenant: 1000);
        var drafter = WorkspaceTestSupport.Store(_raw, agent: "drafter", limits: limits);
        var reviewer = WorkspaceTestSupport.Store(_raw, agent: "reviewer", limits: limits);

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await drafter.WriteAsync("a.md", new string('a', 10));
        await drafter.WriteAsync("b.md", new string('b', 10));

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "reviewer", "session-1"));
        var refused = await Should.ThrowAsync<TraconException>(async () => await reviewer.WriteAsync("c.md", new string('c', 10)));
        refused.Message.ShouldContain("25 bytes");
        await reviewer.WriteAsync("c.md", new string('c', 5));

        // Another session starts again from zero.
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "reviewer", "session-2"));
        await reviewer.WriteAsync("c.md", new string('c', 10));
    }

    [Fact]
    public async Task A_write_that_replaces_a_file_adds_only_the_difference()
    {
        var store = WorkspaceTestSupport.Store(_raw, limits: WorkspaceTestSupport.Limits(file: 10, session: 20, tenant: 1000));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await store.WriteAsync("a.md", new string('a', 10));
        await store.WriteAsync("b.md", new string('b', 10));

        // The session is full; the same file again at the same size, or smaller, still fits.
        await store.WriteAsync("a.md", new string('x', 10));
        await store.WriteAsync("a.md", new string('y', 4));
        await store.WriteAsync("a.md", new string('z', 10));

        await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("c.md", "1"));
    }

    [Fact]
    public async Task Empty_files_cannot_pile_up_for_free_because_each_file_counts_the_cost_of_its_row()
    {
        // 2 500 bytes for the session, 1 000 for each row: two files fit, a third does not, whatever it holds.
        var store = WorkspaceTestSupport.Store(_raw, limits: WorkspaceTestSupport.Limits(file: 10, session: 2500, tenant: 100_000, overhead: 1000));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        await store.WriteAsync("a.md", string.Empty);
        await store.WriteAsync("b.md", string.Empty);

        var refused = await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("c.md", string.Empty));

        refused.Message.ShouldStartWith("Workspace quota reached");
        refused.Message.ShouldContain("each file counts 1000 bytes");
        (await store.FileExistsAsync("c.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_that_is_written_again_does_not_pay_for_its_row_twice()
    {
        var store = WorkspaceTestSupport.Store(_raw, limits: WorkspaceTestSupport.Limits(file: 10, session: 2500, tenant: 100_000, overhead: 1000));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await store.WriteAsync("a.md", string.Empty);
        await store.WriteAsync("b.md", string.Empty);

        await store.WriteAsync("a.md", "1234567890");
        await store.WriteAsync("a.md", "x");
        await store.WriteAsync("a.md", string.Empty);

        (await store.ReadAsync("a.md")).ShouldBe(string.Empty);
    }

    [Fact]
    public async Task The_cost_of_a_row_counts_toward_the_limit_of_the_tenant_across_sessions()
    {
        var store = WorkspaceTestSupport.Store(_raw, limits: WorkspaceTestSupport.Limits(file: 10, session: 10_000, tenant: 2500, overhead: 1000));

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await store.WriteAsync("a.md", string.Empty);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-2"));
        await store.WriteAsync("a.md", string.Empty);

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-3"));

        await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("a.md", string.Empty));
    }

    [Fact]
    public void The_cost_of_a_row_is_a_kibibyte_unless_a_test_says_otherwise()
        => new TraconWorkspaceOptions().FileOverheadBytes.ShouldBe(1024);

    [Fact]
    public async Task A_refused_write_leaves_the_file_it_would_have_replaced()
    {
        var store = WorkspaceTestSupport.Store(_raw, limits: WorkspaceTestSupport.Limits(file: 10));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await store.WriteAsync("a.md", "kept");

        await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("a.md", new string('a', 11)));

        (await store.ReadAsync("a.md")).ShouldBe("kept");
    }

    [Fact]
    public async Task The_workspaces_of_a_tenant_may_hold_as_many_bytes_as_the_limit_and_no_more()
    {
        var limits = WorkspaceTestSupport.Limits(file: 10, session: 20, tenant: 25);
        var store = WorkspaceTestSupport.Store(_raw, limits: limits);

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await store.WriteAsync("a.md", new string('a', 10));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-2"));
        await store.WriteAsync("a.md", new string('a', 10));

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-3"));
        var refused = await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("a.md", new string('a', 10)));
        refused.Message.ShouldContain("tenant");
        await store.WriteAsync("a.md", new string('a', 5));

        // Another tenant has a tenant limit of its own.
        var other = WorkspaceTestSupport.Store(_raw, tenant: "other", limits: limits);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("other", "drafter", "session-1"));
        await other.WriteAsync("a.md", new string('a', 10));
    }

    [Fact]
    public async Task The_file_memory_of_the_agent_does_not_count_toward_the_limits_of_the_workspace()
    {
        var store = WorkspaceTestSupport.Store(_raw, limits: WorkspaceTestSupport.Limits(file: 10, session: 10, tenant: 10));
        await _raw.WriteAsync("acme/drafter/memory.md", new string('m', 5000));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        await store.WriteAsync("a.md", new string('a', 10));
    }

    [Fact]
    public async Task A_search_names_each_file_relative_to_the_directory_searched_even_when_the_store_answers_with_the_whole_stored_path()
    {
        // The SQL store answers with "/{stored path}"; the in-memory store answers relative to the directory. The
        // framework tool puts the directory in front of whatever comes back, so a stored path would show the tenant
        // and the session key to the model.
        var store = WorkspaceTestSupport.Store(new FullPathSearchStore(_raw));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await store.WriteAsync("reports/q3.md", "alpha beta");
        await store.WriteAsync("notes.md", "alpha");

        var inRoot = await store.SearchAsync(string.Empty, "alpha", globPattern: null, recursive: true);
        var inReports = await store.SearchAsync("reports", "alpha", globPattern: null, recursive: true);

        inRoot.Select(static match => match.FileName).Order(StringComparer.Ordinal).ShouldBe(["notes.md", "reports/q3.md"]);
        inReports.Select(static match => match.FileName).ShouldBe(["q3.md"]);
    }

    [Fact]
    public async Task A_search_in_the_in_memory_store_is_relative_to_the_directory_and_stays_so()
    {
        var store = WorkspaceTestSupport.Store(_raw);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await store.WriteAsync("reports/q3.md", "alpha beta");

        (await store.SearchAsync("reports", "alpha", globPattern: null, recursive: true)).Select(static match => match.FileName).ShouldBe(["q3.md"]);
    }

    [Fact]
    public async Task A_listing_names_the_children_of_the_directory_and_shows_no_path_of_the_store()
    {
        var store = WorkspaceTestSupport.Store(_raw);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        await store.WriteAsync("reports/q3.md", "alpha");
        await store.WriteAsync("notes.md", "alpha");

        var children = await store.ListChildrenAsync(string.Empty);

        children.Select(static entry => (entry.Name, entry.Type)).Order().ShouldBe([("notes.md", "file"), ("reports", "directory")]);
    }

    [Fact]
    public async Task The_limits_are_read_when_the_write_happens_so_a_change_applies_to_an_agent_that_is_already_compiled()
    {
        var options = new TraconWorkspaceOptions { MaxFileBytes = 5, MaxSessionBytes = 100, MaxTenantBytes = 1000, FileOverheadBytes = 0 };
        var store = WorkspaceTestSupport.Store(_raw, limits: new StaticOptionsMonitor<TraconWorkspaceOptions>(options));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        await Should.ThrowAsync<TraconException>(async () => await store.WriteAsync("a.md", "123456"));

        options.MaxFileBytes = 10;

        await store.WriteAsync("a.md", "123456");
    }

    /// <summary>A store that answers a search the way the SQL store does: with the whole stored path and a leading slash.</summary>
    private sealed class FullPathSearchStore(AgentFileStore inner) : AgentFileStore
    {
        public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) => inner.WriteAsync(path, content, cancellationToken);

        public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default) => inner.ReadAsync(path, cancellationToken);

        public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default) => inner.DeleteAsync(path, cancellationToken);

        public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => inner.FileExistsAsync(path, cancellationToken);

        public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => inner.CreateDirectoryAsync(path, cancellationToken);

        public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
            => inner.ListChildrenAsync(directory, cancellationToken);

        public override async Task<IReadOnlyList<FileSearchResult>> SearchAsync(
            string directory,
            string regexPattern,
            string? globPattern = null,
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            var prefix = directory.TrimEnd('/') + "/";
            var found = await inner.SearchAsync(directory, regexPattern, globPattern, recursive, cancellationToken);

            return found.Select(match => new FileSearchResult
            {
                FileName = "/" + prefix + match.FileName,
                MatchingLines = match.MatchingLines,
                Snippet = match.Snippet,
            }).ToList();
        }
    }
}
#pragma warning restore MAAI001
