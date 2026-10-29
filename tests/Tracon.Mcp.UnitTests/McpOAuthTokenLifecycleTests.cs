using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The token caches of the registry in the distributed mode, and what happens to a token when its
/// server is deleted.
/// </summary>
public sealed class McpOAuthTokenLifecycleTests
{
    [Fact]
    public async Task The_registry_shares_one_cache_per_pair_in_the_distributed_mode()
    {
        var registry = Registry(new RecordingDistributedCache());

        var first = registry.GetOrCreate("acme", McpOAuthTestKit.Server());
        var second = registry.GetOrCreate("acme", McpOAuthTestKit.Server());

        first.ShouldBeOfType<DistributedMcpTokenCache>();
        ReferenceEquals(first, second).ShouldBeTrue("one process knows which refresh lease it holds.");

        await first.StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);
        (await second.GetTokensAsync(CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_changed_definition_gets_no_token_in_the_distributed_mode()
    {
        var registry = Registry(new RecordingDistributedCache());

        var before = registry.GetOrCreate("acme", McpOAuthTestKit.Server());
        await before.StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);

        var after = registry.GetOrCreate("acme", McpOAuthTestKit.Server(endpoint: "https://other.example.com/mcp"));

        ReferenceEquals(before, after).ShouldBeFalse();

        // The old token is still in the cache under the same key, and no read returns it.
        (await after.GetTokensAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task The_registry_in_memory_mode_is_the_default_and_touches_no_cache()
    {
        var registry = new McpOAuthTokenCacheRegistry();

        var cache = registry.GetOrCreate("acme", McpOAuthTestKit.Server());

        cache.ShouldBeOfType<InMemoryMcpTokenCache>();

        await cache.StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);
        (await cache.GetTokensAsync(CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Removing_a_server_forgets_its_token_in_the_shared_cache()
    {
        var cache = new RecordingDistributedCache();
        var registry = Registry(cache);
        var server = McpOAuthTestKit.Server();

        await registry.GetOrCreate("acme", server).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);
        cache.Peek(McpOAuthCacheKeys.Token(new McpTenantServerKey("acme", server.Name))).ShouldNotBeNull();

        await registry.RemoveAsync(new McpTenantServerKey("acme", server.Name), CancellationToken.None);

        cache.Peek(McpOAuthCacheKeys.Token(new McpTenantServerKey("acme", server.Name))).ShouldBeNull();
    }

    [Fact]
    public async Task A_cache_that_fails_does_not_stop_the_removal()
    {
        var cache = new RecordingDistributedCache();
        var logs = new RecordingLoggerFactory();
        var registry = new McpOAuthTokenCacheRegistry(McpOAuthTestKit.Backend(cache), logs);

        await registry.GetOrCreate("acme", McpOAuthTestKit.Server()).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);
        cache.Failing = true;

        // The entry ends by itself (sliding lifetime); the refresh that asked must not fail over it.
        await Should.NotThrowAsync(async () => await registry.RemoveAsync(new McpTenantServerKey("acme", "prod"), CancellationToken.None));

        logs.Count(Microsoft.Extensions.Logging.LogLevel.Warning, "could not be removed").ShouldBe(1);
    }

    [Fact]
    public async Task A_refresh_forgets_the_token_of_a_server_that_was_deleted()
    {
        var (catalog, servers, cache) = Catalog();
        var kept = McpOAuthTestKit.Server("kept", tenantId: "default");
        var deleted = McpOAuthTestKit.Server("deleted", tenantId: "default");

        await servers.SaveAsync(kept, CancellationToken.None);
        await servers.SaveAsync(deleted, CancellationToken.None);
        await catalog.Registry.GetOrCreate("default", kept).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);
        await catalog.Registry.GetOrCreate("default", deleted).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);

        await servers.DeleteAsync("default", "deleted", CancellationToken.None);
        await catalog.RefreshAsync();

        cache.Peek(McpOAuthCacheKeys.Token(new McpTenantServerKey("default", "deleted"))).ShouldBeNull();
        cache.Peek(McpOAuthCacheKeys.Token(new McpTenantServerKey("default", "kept"))).ShouldNotBeNull();
    }

    /// <summary>
    /// Forgetting a token is cleanup. A request that is cancelled while the cache answers must not fail the refresh:
    /// the list of this pass is published before the cleanup runs.
    /// </summary>
    [Fact]
    public async Task A_refresh_that_is_cancelled_during_the_cleanup_still_completes()
    {
        var (catalog, servers, cache) = Catalog();
        var deleted = McpOAuthTestKit.Server("deleted", tenantId: "default");
        using var cancellation = new CancellationTokenSource();

        await servers.SaveAsync(deleted, CancellationToken.None);
        await catalog.Registry.GetOrCreate("default", deleted).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);
        await servers.DeleteAsync("default", "deleted", CancellationToken.None);

        cache.OnRemove = cancellation.Cancel;

        var outcome = await catalog.RefreshAsync(cancellation.Token);

        outcome.ToolCount.ShouldBe(0);
        cancellation.IsCancellationRequested.ShouldBeTrue("the cache was asked to remove the token, and the request was cancelled there.");
    }

    [Fact]
    public async Task A_disabled_server_keeps_its_token()
    {
        var (catalog, servers, cache) = Catalog();
        var enabled = McpOAuthTestKit.Server("paused", tenantId: "default");

        await servers.SaveAsync(enabled, CancellationToken.None);
        await catalog.Registry.GetOrCreate("default", enabled).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);

        // Switching a server off is not deleting it: the administrator switches it on again
        // and does not authorize it twice.
        await servers.SaveAsync(McpOAuthTestKit.Server("paused", tenantId: "default", enabled: false), CancellationToken.None);
        await catalog.RefreshAsync();

        cache.Peek(McpOAuthCacheKeys.Token(new McpTenantServerKey("default", "paused"))).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_tenant_that_was_not_listed_keeps_the_tokens_of_its_servers()
    {
        var (catalog, _, cache) = Catalog();

        // The tenant store lists no tenant, so only the default tenant is read in the pass. A server
        // of another tenant is absent from the pass, and absent is not deleted.
        await catalog.Registry.GetOrCreate("acme", McpOAuthTestKit.Server("elsewhere")).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);
        await catalog.RefreshAsync();

        cache.Peek(McpOAuthCacheKeys.Token(new McpTenantServerKey("acme", "elsewhere"))).ShouldNotBeNull();
    }

    /// <summary>
    /// 🚨 A server that is saved and authorized while a refresh runs is not in the list the refresh
    /// reads. The refresh takes its snapshot of the known tokens BEFORE it lists, so that token is
    /// not a candidate: read after the list, it would look like the token of a deleted server.
    /// </summary>
    [Fact]
    public async Task A_server_authorized_while_a_refresh_runs_is_not_taken_for_deleted()
    {
        var cache = new RecordingDistributedCache();
        var registry = Registry(cache);
        var hooked = new HookedServerStore(new InMemoryMcpServerStore());
        var catalog = Catalog(registry, hooked);
        var fresh = McpOAuthTestKit.Server("fresh", tenantId: "default");

        hooked.OnList = () => registry.GetOrCreate("default", fresh)
            .StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        await catalog.RefreshAsync();

        cache.Peek(McpOAuthCacheKeys.Token(new McpTenantServerKey("default", "fresh"))).ShouldNotBeNull();
    }

    private static McpOAuthTokenCacheRegistry Registry(RecordingDistributedCache cache)
        => new(McpOAuthTestKit.Backend(cache), NullLoggerFactory.Instance);

    private static (CatalogHandle Catalog, InMemoryMcpServerStore Servers, RecordingDistributedCache Cache) Catalog()
    {
        var cache = new RecordingDistributedCache();
        var registry = Registry(cache);
        var servers = new InMemoryMcpServerStore();

        return (Catalog(registry, servers), servers, cache);
    }

    private static CatalogHandle Catalog(McpOAuthTokenCacheRegistry registry, IMcpServerStore servers)
        => new(
            new McpToolCatalog(
                servers,
                new InMemoryTenantStore(),
                new ConfigurationBuilder().Build(),
                Options.Create(new TraconOptions()),
                Options.Create(new TraconMcpOptions { ConnectionTimeout = TimeSpan.FromSeconds(5) }),
                NullLoggerFactory.Instance,
                registry,
                new AllowAllToolAuthorizationHandler(),
                McpToolPolicyTestGates.Open(),
                NoOpToolArgumentsValidator.Instance,
                attribution: null),
            registry);

    private sealed class CatalogHandle(McpToolCatalog catalog, McpOAuthTokenCacheRegistry registry)
    {
        public McpOAuthTokenCacheRegistry Registry { get; } = registry;

        public ValueTask<McpRefreshOutcome> RefreshAsync(CancellationToken cancellationToken = default) => catalog.RefreshAsync(cancellationToken);
    }

    /// <summary>A server store that runs an action once, in the middle of the first listing.</summary>
    private sealed class HookedServerStore(IMcpServerStore inner) : IMcpServerStore
    {
        public Action? OnList { get; set; }

        public ValueTask<IReadOnlyList<McpServerDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var hook = OnList;
            OnList = null;
            hook?.Invoke();

            return inner.ListAsync(tenantId, cancellationToken);
        }

        public ValueTask<McpServerDefinition?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, name, cancellationToken);

        public ValueTask<McpServerDefinition> SaveAsync(McpServerDefinition server, CancellationToken cancellationToken = default)
            => inner.SaveAsync(server, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, name, cancellationToken);
    }
}
