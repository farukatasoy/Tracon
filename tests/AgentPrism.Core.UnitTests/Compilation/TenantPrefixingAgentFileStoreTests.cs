namespace AgentPrism.Core.UnitTests.Compilation;

#pragma warning disable MAAI001 // InMemoryAgentFileStore is "evaluation purposes only" — for test setup only.

/// <summary>
/// Verifies that <see cref="TenantPrefixingAgentFileStore"/> truly isolates
/// tenants AND the agents inside a tenant on top of a single shared
/// <see cref="Microsoft.Agents.AI.AgentFileStore"/>.
/// </summary>
/// <remarks>
/// The rule these tests guard: a file written through <c>EnableFileMemory</c>
/// must NEVER APPEAR in the <c>EnableTextSearch</c> search of another tenant
/// (the "CRITICAL SUSPICION" note in 00-INDEKS.md, 2026-08-10) or of another
/// agent of the same tenant (defect F-105). The sessions of one agent DO share
/// the subtree; file memory is an agent-level memory.
/// </remarks>
public sealed class TenantPrefixingAgentFileStoreTests
{
    [Fact]
    public async Task A_file_written_by_one_tenant_is_not_visible_to_another_tenant()
    {
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var tenantA = new TenantPrefixingAgentFileStore(shared, "tenant-a", "agent-one");
        var tenantB = new TenantPrefixingAgentFileStore(shared, "tenant-b", "agent-one");

        await tenantA.WriteAsync("/secret.txt", "tenant-a's secret", CancellationToken.None);

        (await tenantA.ReadAsync("/secret.txt", CancellationToken.None)).ShouldBe("tenant-a's secret");
        (await tenantA.FileExistsAsync("/secret.txt", CancellationToken.None)).ShouldBeTrue();

        (await tenantB.FileExistsAsync("/secret.txt", CancellationToken.None)).ShouldBeFalse();
        (await tenantB.ListChildrenAsync("/", CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_written_by_one_agent_is_not_visible_to_another_agent_of_the_same_tenant()
    {
        // Defect F-105: the tenant boundary was closed (K-381) but the boundary
        // INSIDE a tenant was not. Two agents of the same tenant shared one
        // subtree, so a file written by an EnableFileMemory agent was found by
        // ANOTHER agent's EnableTextSearch.
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var writer = new TenantPrefixingAgentFileStore(shared, "tenant-a", "notes-agent");
        var reader = new TenantPrefixingAgentFileStore(shared, "tenant-a", "support-agent");

        await writer.WriteAsync("/secret.txt", "notes-agent's private note", CancellationToken.None);

        (await writer.ReadAsync("/secret.txt", CancellationToken.None))
            .ShouldBe("notes-agent's private note");

        (await reader.FileExistsAsync("/secret.txt", CancellationToken.None)).ShouldBeFalse();
        (await reader.ListChildrenAsync("/", CancellationToken.None)).ShouldBeEmpty();

        var matches = await reader.SearchAsync("/", "private", recursive: true, cancellationToken: CancellationToken.None);

        matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_agent_keeps_its_files_across_wrappers()
    {
        // File memory is an AGENT-level memory: the same agent must still find
        // what it wrote earlier, otherwise the feature loses its purpose.
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var first = new TenantPrefixingAgentFileStore(shared, "tenant-a", "notes-agent");
        var second = new TenantPrefixingAgentFileStore(shared, "tenant-a", "notes-agent");

        await first.WriteAsync("/journal.txt", "remembered", CancellationToken.None);

        (await second.ReadAsync("/journal.txt", CancellationToken.None)).ShouldBe("remembered");
    }

    [Fact]
    public async Task Same_tenant_sees_its_own_file_from_the_root_directory()
    {
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var tenant = new TenantPrefixingAgentFileStore(shared, "tenant-a", "agent-one");

        await tenant.WriteAsync("/notes/journal.txt", "hello", CancellationToken.None);

        var children = await tenant.ListChildrenAsync("/notes", CancellationToken.None);

        children.ShouldHaveSingleItem().Name.ShouldBe("journal.txt");
    }

    [Fact]
    public async Task The_real_prefix_in_the_inner_store_does_not_leak_out()
    {
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var tenant = new TenantPrefixingAgentFileStore(shared, "tenant-a", "agent-one");

        await tenant.WriteAsync("/notes.txt", "content", CancellationToken.None);

        var children = await tenant.ListChildrenAsync("/", CancellationToken.None);

        // The returned name must NOT be "tenant-a/notes.txt" but plain
        // "notes.txt", as if the tenant were operating in its own private
        // root directory.
        children.ShouldHaveSingleItem().Name.ShouldBe("notes.txt");

        // In the inner store the real path CARRIES the tenant prefix.
        (await shared.FileExistsAsync("tenant-a/agent-one/notes.txt", CancellationToken.None)).ShouldBeTrue();
    }
}
#pragma warning restore MAAI001
