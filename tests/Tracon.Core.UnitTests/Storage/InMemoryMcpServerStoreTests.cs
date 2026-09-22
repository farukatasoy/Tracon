namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// <see cref="InMemoryMcpServerStore"/> keys a definition by its tenant and name.
/// </summary>
/// <remarks>
/// The key used to be the two strings joined with an invisible U+001F. Nothing at
/// the store layer keeps that character out of a tenant id or a server name, so
/// two different pairs could produce the same key and one tenant's definition
/// could replace another's (Phase 182). HTTP validates both values before they
/// reach the store; a direct store caller does not.
/// </remarks>
public sealed class InMemoryMcpServerStoreTests
{
    [Fact]
    public async Task Two_tenant_and_name_pairs_that_join_to_the_same_text_stay_apart()
    {
        var store = new InMemoryMcpServerStore();

        await store.SaveAsync(Server(tenantId: "acme\u001Fops", name: "search"));
        await store.SaveAsync(Server(tenantId: "acme", name: "ops\u001Fsearch"));

        (await store.GetAsync("acme\u001Fops", "search"))!.TenantId.ShouldBe("acme\u001Fops");
        (await store.GetAsync("acme", "ops\u001Fsearch"))!.TenantId.ShouldBe("acme");
        (await store.ListAsync("acme\u001Fops")).ShouldHaveSingleItem();
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
