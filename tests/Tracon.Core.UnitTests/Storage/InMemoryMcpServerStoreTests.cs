namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// <see cref="InMemoryMcpServerStore"/> keys a definition by its tenant and name.
/// </summary>
/// <remarks>
/// The key used to be the two strings joined with an invisible U+001F. Nothing at
/// the store layer kept that character out of a tenant id or a server name, so
/// two different pairs could produce the same key and one tenant's definition
/// could replace another's. The tenant rule now refuses the character, and the
/// name stays a part of a composite key rather than a joined text.
/// </remarks>
public sealed class InMemoryMcpServerStoreTests
{
    [Fact]
    public async Task A_tenant_that_holds_the_old_separator_never_reaches_the_store()
    {
        var store = new InMemoryMcpServerStore();

        await Should.ThrowAsync<ArgumentException>(async () => await store.SaveAsync(Server(tenantId: "acme\u001Fops", name: "search")));
        await Should.ThrowAsync<ArgumentException>(async () => await store.GetAsync("acme\u001Fops", "search"));
        await Should.ThrowAsync<ArgumentException>(async () => await store.ListAsync("acme\u001Fops"));
    }

    [Fact]
    public async Task A_name_that_holds_the_old_separator_stays_a_key_of_its_own()
    {
        var store = new InMemoryMcpServerStore();

        await store.SaveAsync(Server(tenantId: "acme", name: "ops\u001Fsearch"));
        await store.SaveAsync(Server(tenantId: "acme", name: "ops"));
        await store.SaveAsync(Server(tenantId: "acme-ops", name: "search"));

        (await store.GetAsync("acme", "ops\u001Fsearch"))!.Name.ShouldBe("ops\u001Fsearch");
        (await store.GetAsync("acme", "ops"))!.Name.ShouldBe("ops");
        (await store.GetAsync("acme-ops", "search"))!.TenantId.ShouldBe("acme-ops");
        (await store.ListAsync("acme")).Count.ShouldBe(2);
        (await store.ListAsync("acme-ops")).ShouldHaveSingleItem();
    }

    private static McpServerDefinition Server(string tenantId, string name)
        => new()
        {
            Id = Guid.Empty,
            TenantId = tenantId,
            Name = name,
            Endpoint = new Uri("https://mcp.example.com/"),
        };
}
