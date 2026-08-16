namespace AgentPrism.Core.UnitTests.Compilation;

#pragma warning disable MAAI001 // InMemoryAgentFileStore is "evaluation purposes only" — for test setup only.

/// <summary>
/// Verifies that <see cref="TenantPrefixingAgentFileStore"/> truly isolates
/// tenants on top of a single shared <see cref="Microsoft.Agents.AI.AgentFileStore"/>.
/// </summary>
/// <remarks>
/// The rule these tests guard: a file written by one tenant through
/// <c>EnableFileMemory</c> must NEVER APPEAR in ANOTHER tenant's
/// <c>EnableTextSearch</c> search. See the "CRITICAL SUSPICION" note in
/// 00-INDEKS.md (2026-08-10).
/// </remarks>
public sealed class TenantPrefixingAgentFileStoreTests
{
    [Fact]
    public async Task A_file_written_by_one_tenant_is_not_visible_to_another_tenant()
    {
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var tenantA = new TenantPrefixingAgentFileStore(shared, "tenant-a");
        var tenantB = new TenantPrefixingAgentFileStore(shared, "tenant-b");

        await tenantA.WriteAsync("/secret.txt", "tenant-a's secret", CancellationToken.None);

        (await tenantA.ReadAsync("/secret.txt", CancellationToken.None)).ShouldBe("tenant-a's secret");
        (await tenantA.FileExistsAsync("/secret.txt", CancellationToken.None)).ShouldBeTrue();

        (await tenantB.FileExistsAsync("/secret.txt", CancellationToken.None)).ShouldBeFalse();
        (await tenantB.ListChildrenAsync("/", CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Same_tenant_sees_its_own_file_from_the_root_directory()
    {
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var tenant = new TenantPrefixingAgentFileStore(shared, "tenant-a");

        await tenant.WriteAsync("/notes/journal.txt", "hello", CancellationToken.None);

        var children = await tenant.ListChildrenAsync("/notes", CancellationToken.None);

        children.ShouldHaveSingleItem().Name.ShouldBe("journal.txt");
    }

    [Fact]
    public async Task The_real_prefix_in_the_inner_store_does_not_leak_out()
    {
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var tenant = new TenantPrefixingAgentFileStore(shared, "tenant-a");

        await tenant.WriteAsync("/notes.txt", "content", CancellationToken.None);

        var children = await tenant.ListChildrenAsync("/", CancellationToken.None);

        // The returned name must NOT be "tenant-a/notes.txt" but plain
        // "notes.txt", as if the tenant were operating in its own private
        // root directory.
        children.ShouldHaveSingleItem().Name.ShouldBe("notes.txt");

        // In the inner store the real path CARRIES the tenant prefix.
        (await shared.FileExistsAsync("tenant-a/notes.txt", CancellationToken.None)).ShouldBeTrue();
    }
}
#pragma warning restore MAAI001
