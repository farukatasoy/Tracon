using Microsoft.Agents.AI;

namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Tenant isolation contract for the persistent agent file memory.
/// </summary>
/// <remarks>
/// <para>
/// File memory is not an <c>IStore</c>; it is the
/// Microsoft Agent Framework <see cref="AgentFileStore"/> type. The tenant
/// is read from <see cref="ITenantContext"/>, and the agent name is read
/// from an implementation-defined ambient scope that <see cref="EnterRunScope"/>
/// establishes -- Tracon's own stores read it from
/// <c>TraconRunContext</c> (<c>Tracon.Core</c>), which this package
/// deliberately does not reference (it depends on
/// <c>Tracon.Abstractions</c> only).
/// </para>
/// <para>
/// The ambient scope is set up at the <strong>start of each test body</strong>,
/// not in <c>InitializeAsync</c>: the xunit v3 (MTP) lifecycle hook can run
/// the test body as a separately scheduled task, which breaks an
/// <c>AsyncLocal</c>-based flow.
/// </para>
/// </remarks>
#pragma warning disable MAAI001 // AgentFileStore "evaluation purposes only" — same rationale as the product code.
public abstract class AgentFileStoreContract : TenantIsolationContract<AgentFileStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        Enter(tenantId);

        var path = $"/{name}.md";
        await Store.WriteAsync(path, $"{tenantId} content");

        return path;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        Enter(tenantId);

        var path = (string)key;
        var content = await Store.ReadAsync(path);

        // Existence checks and search must carry the same boundary.
        (await Store.FileExistsAsync(path)).ShouldBe(content is not null);

        return content is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        Enter(tenantId);
        return (await Store.ListChildrenAsync("/")).Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
    {
        Enter(tenantId);

        var path = (string)key;

        if (!await Store.FileExistsAsync(path))
        {
            return false;
        }

        await Store.DeleteAsync(path);
        return true;
    }

    [Fact]
    public async Task Search_does_not_find_another_tenants_file()
    {
        Enter(TenantA);
        await Store.WriteAsync("/notes/a.md", "invoice number 42");

        Enter(TenantB);
        (await Store.SearchAsync("/", "invoice", recursive: true)).ShouldBeEmpty();

        Enter(TenantA);
        (await Store.SearchAsync("/", "invoice", recursive: true)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Creating_a_directory_does_not_produce_a_record()
    {
        // The MAF contract expects a directory call; in the persistent store,
        // directories live implicitly within paths and do not produce a
        // separate record.
        Enter(TenantA);

        await Store.CreateDirectoryAsync("/notes");

        (await Store.ListChildrenAsync("/")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Recursive_false_skips_matches_in_subdirectories()
    {
        // Phase 51, Job A: the depth limit moved down to SQL
        // (prefix_deep_like). This test proves the behavior did not change.
        Enter(TenantA);

        await Store.WriteAsync("/notes/top.md", "keyword is here");
        await Store.WriteAsync("/notes/sub/deep.md", "keyword is here too");

        var shallow = await Store.SearchAsync("/notes", "keyword", recursive: false);
        shallow.ShouldHaveSingleItem().FileName.ShouldBe("/notes/top.md");

        var deep = await Store.SearchAsync("/notes", "keyword", recursive: true);
        deep.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Glob_filter_narrows_by_file_name()
    {
        // Phase 51, Job A: glob moved down to SQL (name_like). `*` crosses
        // directory boundaries, matching the original .NET regex-based
        // behavior.
        Enter(TenantA);

        await Store.WriteAsync("/notes/a.md", "shared value");
        await Store.WriteAsync("/notes/a.txt", "shared value");
        await Store.WriteAsync("/notes/sub/b.md", "shared value");

        var results = await Store.SearchAsync("/notes", "shared", globPattern: "*.md", recursive: true);

        results.Select(static r => r.FileName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldBe(["/notes/a.md", "/notes/sub/b.md"]);
    }

    [Fact]
    public async Task Search_in_a_large_store_returns_only_the_target_directory()
    {
        // Phase 51, Job A: `LoadAllAsync` was removed. This test proves that
        // the single match in the target directory is found correctly while
        // many UNRELATED files exist (the row-count measurement is done
        // separately with a Postgres-specific EXPLAIN, see
        // docs/arsiv/fazlar/51-VEKTOR-BELLEK-VE-RAG.md).
        Enter(TenantA);

        const int UnrelatedFileCount = 500;

        for (var i = 0; i < UnrelatedFileCount; i++)
        {
            await Store.WriteAsync($"/archive/file-{i:D4}.md", "unrelated content");
        }

        await Store.WriteAsync("/target/note.md", "search-keyword here");

        var results = await Store.SearchAsync("/target", "search-keyword", recursive: true);

        results.ShouldHaveSingleItem().FileName.ShouldBe("/target/note.md");
    }

    /// <summary>
    /// Sets the active tenant and establishes the ambient run scope.
    /// </summary>
    /// <param name="tenantId">The tenant id.</param>
    protected void Enter(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;
        EnterRunScope();
    }

    /// <summary>
    /// Establishes whatever ambient run scope the store under test reads the
    /// agent name from. Tracon's own SQL-backed implementations read
    /// <c>TraconRunContext.Current</c> (<c>Tracon.Core</c>); a
    /// derived contract in a project that already references
    /// <c>Tracon.Core</c> sets that up here.
    /// </summary>
    protected abstract void EnterRunScope();

    /// <summary>Clears the ambient run scope <see cref="EnterRunScope"/> established.</summary>
    protected abstract void ExitRunScope();

    /// <inheritdoc />
    protected override ValueTask OnDisposeAsync()
    {
        ExitRunScope();
        return default;
    }
}
#pragma warning restore MAAI001
