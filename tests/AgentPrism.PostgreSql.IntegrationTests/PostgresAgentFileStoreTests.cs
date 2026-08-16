using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// <see cref="PostgresAgentFileStore"/>'s path hierarchy, tenant/agent
/// isolation, and search behavior.
/// </summary>
/// <remarks>
/// The agent name is not a parameter on the interface, so it is read from the
/// ambient scope (<see cref="AgentPrismRunContext"/>). The scope is set up at
/// the START of each test BODY, not in <c>InitializeAsync</c>: xunit v3 (MTP)
/// can run lifecycle hooks and the test body as separately scheduled
/// operations, which breaks the <c>AsyncLocal</c> flow — this was measured.
/// </remarks>
public sealed class PostgresAgentFileStoreTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext? _context;

    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    public async ValueTask DisposeAsync()
    {
        AgentPrismRunContext.SetCurrent(null);

        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Written_file_is_read()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notes/a.md", "hello");

        (await _context.AgentFiles.ReadAsync("/notes/a.md")).ShouldBe("hello");
    }

    [Fact]
    public async Task Nonexistent_file_returns_null()
    {
        SetScope("agent-a");

        (await _context!.AgentFiles.ReadAsync("/none")).ShouldBeNull();
    }

    [Fact]
    public async Task Existing_file_is_overwritten()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/a.md", "first");
        await _context.AgentFiles.WriteAsync("/a.md", "second");

        (await _context.AgentFiles.ReadAsync("/a.md")).ShouldBe("second");
    }

    [Fact]
    public async Task Delete_works_and_returns_false_the_second_time()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/a.md", "content");

        (await _context.AgentFiles.DeleteAsync("/a.md")).ShouldBeTrue();
        (await _context.AgentFiles.FileExistsAsync("/a.md")).ShouldBeFalse();
        (await _context.AgentFiles.DeleteAsync("/a.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task Different_agents_do_not_see_each_others_file()
    {
        SetScope("agent-a");
        await _context!.AgentFiles.WriteAsync("/a.md", "agent-a content");

        SetScope("agent-b");
        (await _context.AgentFiles.FileExistsAsync("/a.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task Directory_listing_returns_direct_children()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notes/a.md", "a");
        await _context.AgentFiles.WriteAsync("/notes/b.md", "b");
        await _context.AgentFiles.WriteAsync("/notes/sub/c.md", "c");
        await _context.AgentFiles.WriteAsync("/other.md", "d");

        var children = await _context.AgentFiles.ListChildrenAsync("/notes");

        children.Select(static entry => entry.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldBe(["a.md", "b.md", "sub"]);

        children.Single(static entry => string.Equals(entry.Name, "sub", StringComparison.Ordinal))
            .Type.ShouldBe("directory");
        children.Single(static entry => string.Equals(entry.Name, "a.md", StringComparison.Ordinal))
            .Type.ShouldBe("file");
    }

    [Fact]
    public async Task Search_returns_the_matching_line()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notes/a.md", "first line\ninvoice number 42\nlast line");
        await _context.AgentFiles.WriteAsync("/notes/b.md", "unrelated content");

        var results = await _context.AgentFiles.SearchAsync("/", "invoice", recursive: true);

        var match = results.ShouldHaveSingleItem();
        match.FileName.ShouldBe("/notes/a.md");
        match.MatchingLines.ShouldHaveSingleItem().LineNumber.ShouldBe(2);
    }

    private static void SetScope(string agentName)
        => AgentPrismRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            AgentName = agentName,
        });
}
