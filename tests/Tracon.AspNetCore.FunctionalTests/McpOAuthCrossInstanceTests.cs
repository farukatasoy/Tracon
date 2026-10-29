using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 264 (F-766): an MCP server that uses OAuth works on two instances. The token is kept in the
/// application's distributed cache, the provider's redirect may reach either instance, and one
/// instance at a time refreshes a token.
/// </summary>
/// <remarks>
/// <para>
/// Two hosts share one SQLite file (the server definitions and the refresh lease) and one
/// <see cref="IDistributedCache"/> object (what Redis is in production), each with its own DI
/// container, its own tool catalog and its own pending-flow table. A real MCP server that is its
/// own authorization server stands behind them (<see cref="OAuthProtectedMcpServer"/>).
/// </para>
/// <para>
/// The intervals are shortened (<see cref="McpOAuthDistributedTiming"/>); the production values are
/// measured by the manual acceptance case.
/// </para>
/// </remarks>
public sealed class McpOAuthCrossInstanceTests
{
    private static readonly McpOAuthDistributedTiming FastTiming = new()
    {
        CallbackPoll = TimeSpan.FromMilliseconds(50),
        OutcomePoll = TimeSpan.FromMilliseconds(50),
        RefreshPoll = TimeSpan.FromMilliseconds(50),
    };

    [Fact]
    public async Task A_token_authorized_on_one_instance_serves_the_other()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartPairAsync(new SharedCache());

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        (await OAuthFlowClient.AuthorizeAsync(pair.A)).ShouldContain("Authorization complete", Case.Sensitive);

        // Instance B did not take part and holds no token of its own: it reads A's from the cache.
        await OAuthFlowClient.RefreshAsync(pair.B);

        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup);
        server.TokenRequests.ShouldHaveSingleItem("B listed the tools with the token A obtained; it exchanged nothing.");
    }

    /// <summary>
    /// The call, not only the listing: an agent run on instance B calls the tool of a server that instance A
    /// authorized, and the server answers it.
    /// </summary>
    [Fact]
    public async Task A_tool_call_on_the_other_instance_reaches_the_server_with_the_token()
    {
        var model = new Tracon.Testing.FakeModelProvider("oauth-model").EchoesLastToolResult();
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartPairAsync(new SharedCache(), model);

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        (await OAuthFlowClient.AuthorizeAsync(pair.A)).ShouldContain("Authorization complete", Case.Sensitive);
        await OAuthFlowClient.RefreshAsync(pair.B);
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup);

        using (var saved = await pair.B.Client.PostAsJsonAsync(
                   new Uri("/tracon/api/agents", UriKind.Relative),
                   new AgentDefinitionRequest
                   {
                       Name = "looker",
                       Instructions = "Look the value up.",
                       Model = new ModelBinding { Provider = "oauth-model", Model = "lookup" },
                       ToolNames = [$"{OAuthFlowClient.ServerName}_lookup"],
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        }

        model.CallsTool($"{OAuthFlowClient.ServerName}_lookup");

        using var run = await pair.B.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/looker/run", UriKind.Relative),
            new AgentRunRequest { Message = "look it up", SessionId = "oauth-b" });

        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());

        // The server answered the call "forty-two" because the request carried the token A obtained.
        (await run.Content.ReadAsStringAsync()).ShouldContain("forty-two", Case.Sensitive);
    }

    /// <summary>
    /// 🚨 Two tenants save a server of the same name, at the same endpoint, with the same client id. Only one of them
    /// authorizes it. The other tenant must get no token from the cache the instances share: the key holds the tenant.
    /// </summary>
    [Fact]
    public async Task Tokens_are_per_tenant()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartPairAsync(new SharedCache(), tenancy: true);

        foreach (var slug in new[] { "tenant-a", "tenant-b" })
        {
            using var tenant = await pair.A.Client.PutAsJsonAsync(new Uri($"/tracon/api/tenants/{slug}", UriKind.Relative), new { displayName = slug });
            tenant.StatusCode.ShouldBe(HttpStatusCode.OK, await tenant.Content.ReadAsStringAsync());

            await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint, slug);
        }

        var state = await OAuthFlowClient.StartFlowAsync(pair.A, "tenant-a");
        (await OAuthFlowClient.CallbackAsync(pair.B, state)).ShouldContain("Authorization complete", Case.Sensitive);

        await OAuthFlowClient.RefreshAsync(pair.B);

        // The refresh ran for both tenants: the first one's tool is there, so the snapshot that holds both is in place.
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B, "tenant-a"), OAuthFlowClient.HasLookup);
        OAuthFlowClient.HasLookup(await OAuthFlowClient.ToolNamesAsync(pair.B, "tenant-b")).ShouldBeFalse();
        server.TokenRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_callback_that_reaches_the_other_instance_completes_the_flow()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartPairAsync(new SharedCache());

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        var state = await OAuthFlowClient.StartFlowAsync(pair.A);

        // The provider redirects to instance B, which started nothing. Round-robin does this half of the time.
        var page = await OAuthFlowClient.CallbackAsync(pair.B, state);

        page.ShouldContain("Authorization complete", Case.Sensitive);

        // The owner exchanged the code, once, with the verifier only it holds.
        var exchange = server.TokenRequests.ShouldHaveSingleItem();
        exchange.GrantType.ShouldBe("authorization_code");
        exchange.HasCodeVerifier.ShouldBeTrue();

        // Both instances list the tool: A refreshed after the exchange, B after it answered the browser.
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.A), OAuthFlowClient.HasLookup);
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup);
    }

    [Fact]
    public async Task A_redirect_that_the_provider_sends_twice_exchanges_the_code_once()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartPairAsync(new SharedCache());

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        var state = await OAuthFlowClient.StartFlowAsync(pair.A);

        (await OAuthFlowClient.CallbackAsync(pair.B, state)).ShouldContain("Authorization complete", Case.Sensitive);

        // A browser that reloads the page, or a provider that retries: the flow is over and nothing is left to answer.
        (await OAuthFlowClient.CallbackAsync(pair.B, state)).ShouldContain("not found or has expired", Case.Sensitive);
        (await OAuthFlowClient.CallbackAsync(pair.A, state)).ShouldContain("not found or has expired", Case.Sensitive);

        server.TokenRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_declined_authorization_on_the_other_instance_ends_the_flow_with_a_reason()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartPairAsync(new SharedCache());

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        var state = await OAuthFlowClient.StartFlowAsync(pair.A);

        using var declined = await pair.B.Client.GetAsync(new Uri(
            $"/tracon/api/mcp-servers/{OAuthFlowClient.ServerName}/oauth/callback?error=access_denied&state={Uri.EscapeDataString(state)}",
            UriKind.Relative));
        var page = await declined.Content.ReadAsStringAsync();

        page.ShouldContain("Authorization failed", Case.Sensitive);
        server.TokenRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_state_no_instance_announced_is_refused_without_a_write()
    {
        var cache = new SharedCache();
        await using var pair = await StartPairAsync(cache);

        // Well formed, and nobody started it: the anonymous endpoint must not write for it.
        var page = await OAuthFlowClient.CallbackAsync(pair.B, new string('A', 43));

        page.ShouldContain("not found or has expired", Case.Sensitive);
        cache.Writes.ShouldBe(0);
    }

    /// <summary>
    /// 🚨 The callback endpoint is anonymous and writes to a cache every instance shares. A value that is too
    /// long, or not shaped like a state, reaches no write, and the real flow survives the attempt.
    /// </summary>
    [Fact]
    public async Task The_callback_rejects_oversized_or_malformed_values_without_writing()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        var cache = new SharedCache();
        await using var pair = await StartPairAsync(cache);

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        var state = await OAuthFlowClient.StartFlowAsync(pair.A);
        var writesAfterStart = cache.Writes;

        // A code of 5000 characters (the limit is 4096), for the right state.
        (await OAuthFlowClient.CallbackAsync(pair.B, state, code: new string('c', 5000))).ShouldContain("not found or has expired", Case.Sensitive);

        // A state of 5000 characters, one that is too short, and one with characters no SDK state has.
        (await OAuthFlowClient.CallbackAsync(pair.B, new string('s', 5000))).ShouldContain("not found or has expired", Case.Sensitive);
        (await OAuthFlowClient.CallbackAsync(pair.B, "short")).ShouldContain("not found or has expired", Case.Sensitive);
        (await OAuthFlowClient.CallbackAsync(pair.B, "<script>alert(1)</script>0123456789")).ShouldContain("not found or has expired", Case.Sensitive);

        cache.Writes.ShouldBe(writesAfterStart, "no refused request wrote anything.");
        server.TokenRequests.ShouldBeEmpty();

        // The flow is still waiting; the administrator's real redirect completes it.
        (await OAuthFlowClient.CallbackAsync(pair.B, state)).ShouldContain("Authorization complete", Case.Sensitive);
        server.TokenRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_cache_holds_no_plaintext_token_or_code()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        var cache = new SharedCache();
        await using var pair = await StartPairAsync(cache);

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        var state = await OAuthFlowClient.StartFlowAsync(pair.A);
        (await OAuthFlowClient.CallbackAsync(pair.B, state, code: "a-distinctive-code-5e2f")).ShouldContain("Authorization complete", Case.Sensitive);

        cache.Keys.ShouldNotBeEmpty();

        foreach (var key in cache.Keys)
        {
            var text = Encoding.Latin1.GetString(cache.Peek(key)!);

            text.ShouldNotContain(server.AccessToken, Case.Sensitive);
            text.ShouldNotContain("refresh-", Case.Sensitive);
            text.ShouldNotContain("a-distinctive-code-5e2f", Case.Sensitive);
        }

        // The token was written at all: an empty cache would prove nothing.
        cache.Keys.ShouldContain(static key => key.Contains(":token:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Changing_the_endpoint_sends_no_token_to_the_new_endpoint()
    {
        await using var original = await OAuthProtectedMcpServer.StartAsync();
        await using var other = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartPairAsync(new SharedCache());

        await OAuthFlowClient.SaveServerAsync(pair.A, original.Endpoint);
        (await OAuthFlowClient.AuthorizeAsync(pair.A)).ShouldContain("Authorization complete", Case.Sensitive);

        // The token sits in the cache the instances share, under the key of the server's name. The
        // other instance must not hand it to a different address.
        await OAuthFlowClient.SaveServerAsync(pair.A, other.Endpoint);
        await OAuthFlowClient.RefreshAsync(pair.A);
        await OAuthFlowClient.RefreshAsync(pair.B);

        other.AuthorizationHeaders.ShouldNotContain(
            header => string.Equals(header, $"Bearer {original.AccessToken}", StringComparison.Ordinal));
        OAuthFlowClient.HasLookup(await OAuthFlowClient.ToolNamesAsync(pair.A)).ShouldBeFalse();
        OAuthFlowClient.HasLookup(await OAuthFlowClient.ToolNamesAsync(pair.B)).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_refreshes_on_two_instances_call_the_token_endpoint_once()
    {
        // The provider rotates the refresh token and refuses one it has seen: a second refresh with the
        // same token shows up as a rejection. The answer takes a moment, so the two refreshes overlap.
        await using var server = await OAuthProtectedMcpServer.StartAsync(refreshDelay: TimeSpan.FromMilliseconds(400));
        var cache = new SharedCache();
        await using var pair = await StartPairAsync(cache);

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        (await OAuthFlowClient.AuthorizeAsync(pair.A)).ShouldContain("Authorization complete", Case.Sensitive);
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.A), OAuthFlowClient.HasLookup);

        // The token the cache holds has run out: the SDK now has to refresh it. Everything else about it is real.
        await ExpireStoredTokenAsync(pair.A, cache);
        var refreshesBefore = server.RefreshGrants;

        await Task.WhenAll(OAuthFlowClient.RefreshAsync(pair.A), OAuthFlowClient.RefreshAsync(pair.B));

        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup);
        server.RejectedRefreshes.ShouldBe(0, "an instance that refreshed with a token already used would be refused.");
        (server.RefreshGrants - refreshesBefore).ShouldBe(1, "one instance refreshed; the other read the new token from the cache.");
    }

    /// <summary>
    /// The cost of the mode, measured: the SDK reads the token cache for each request it sends to the MCP
    /// server (<c>ModelContextProtocol.Core</c> 2.3.0), and a process keeps no copy. The numbers are in the
    /// production guide; this test holds their shape, not an exact count that an SDK upgrade would change.
    /// </summary>
    [Fact]
    public async Task A_connection_reads_the_cache_for_each_request_it_sends()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        var cache = new SharedCache();
        await using var pair = await StartPairAsync(cache);

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        (await OAuthFlowClient.AuthorizeAsync(pair.A)).ShouldContain("Authorization complete", Case.Sensitive);
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.A), OAuthFlowClient.HasLookup);

        var readsBefore = cache.TokenReads;
        var requestsBefore = server.AuthorizationHeaders.Count;

        await OAuthFlowClient.RefreshAsync(pair.B);
        await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(pair.B), OAuthFlowClient.HasLookup);

        var reads = cache.TokenReads - readsBefore;
        var requests = server.AuthorizationHeaders.Count - requestsBefore;

        // Measured on 2.3.0: one read for each request, two of each to connect and list the tools. Never fewer
        // reads than requests: that would mean the process keeps a copy of the token.
        requests.ShouldBeGreaterThan(0);
        reads.ShouldBeGreaterThanOrEqualTo(requests, $"{reads} reads for {requests} requests");
    }

    [Fact]
    public async Task The_memory_mode_explains_a_callback_on_the_wrong_instance()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var pair = await StartPairAsync(cache: null);

        await OAuthFlowClient.SaveServerAsync(pair.A, server.Endpoint);
        var state = await OAuthFlowClient.StartFlowAsync(pair.A);

        var page = await OAuthFlowClient.CallbackAsync(pair.B, state);

        page.ShouldContain("Tracon:Mcp:OAuthTokenStore=DistributedCache", Case.Sensitive);
        page.ShouldContain("not found or has expired", Case.Sensitive);
        server.TokenRequests.ShouldBeEmpty();

        // The owner is untouched: the redirect that did reach it still completes the flow.
        (await OAuthFlowClient.CallbackAsync(pair.A, state)).ShouldContain("Authorization complete", Case.Sensitive);
    }

    [Fact]
    public async Task The_distributed_mode_without_content_protection_fails_at_startup()
    {
        var failure = await Should.ThrowAsync<OptionsValidationException>(
            () => TraconTestHost.StartAsync(
                static builder => builder.UseMcp(),
                configureServices: static services =>
                {
                    services.AddSingleton<IDistributedCache>(new SharedCache());
                    services.Configure<TraconMcpOptions>(static options => options.OAuthTokenStore = McpOAuthTokenStore.DistributedCache);
                }));

        failure.Message.ShouldContain("content protection is off", Case.Sensitive);
    }

    [Fact]
    public async Task The_distributed_mode_without_a_cache_fails_at_startup()
    {
        var failure = await Should.ThrowAsync<OptionsValidationException>(
            () => TraconTestHost.StartAsync(
                static builder => builder.UseMcp(),
                configureServices: static services =>
                {
                    services.AddSingleton(McpOAuthKit.Protector());
                    services.Configure<TraconMcpOptions>(static options => options.OAuthTokenStore = McpOAuthTokenStore.DistributedCache);
                }));

        failure.Message.ShouldContain("no IDistributedCache is registered", Case.Sensitive);
    }

    [Fact]
    public async Task The_token_store_is_read_from_configuration()
    {
        var configuration = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Mcp:OAuthTokenStore"] = "DistributedCache",
            ["Tracon:Mcp:OAuthTokenSlidingLifetime"] = "3.00:00:00",
        };

        // The section reaches the options through the host's own configuration, as an environment
        // variable does in a container: with no cache registered the host refuses to start, which is
        // how the value is seen to have arrived.
        var failure = await Should.ThrowAsync<OptionsValidationException>(
            () => TraconTestHost.StartAsync(
                builder => builder.UseMcp(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build().GetSection(TraconMcpOptions.SectionName), configure: null),
                configureServices: static _ => { }));

        failure.Message.ShouldContain("Tracon:Mcp:OAuthTokenStore", Case.Sensitive);
    }

    private static async Task ExpireStoredTokenAsync(TraconTestHost host, SharedCache cache)
    {
        var backend = host.Services.GetRequiredService<McpOAuthDistributedBackend>();
        var key = new McpTenantServerKey("default", OAuthFlowClient.ServerName);
        var tokens = new DistributedMcpTokenCache(
            backend,
            key,
            new McpTokenBinding(
                (await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", OAuthFlowClient.ServerName))!.Endpoint.AbsoluteUri,
                OAuthFlowClient.ClientId),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var token = (await tokens.GetTokensAsync(CancellationToken.None)).ShouldNotBeNull();
        token.ObtainedAt = DateTimeOffset.UtcNow.AddHours(-2);

        await tokens.StoreTokensAsync(token, CancellationToken.None);
        cache.Keys.ShouldContain(stored => string.Equals(stored, McpOAuthCacheKeys.Token(key), StringComparison.Ordinal));
    }

    private static Task<TwoHostFixture> StartPairAsync(
        SharedCache? cache,
        Tracon.Testing.FakeModelProvider? model = null,
        bool tenancy = false)
    {
        var protector = McpOAuthKit.Protector();

        return TwoHostFixture.StartAsync(
            configure: builder =>
            {
                builder.UseMcp();

                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }

                if (model is not null)
                {
                    builder.AddModelProvider(model);
                }
            },
            configureServices: services =>
            {
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconMcpOptions>(options =>
                {
                    options.OAuthCallbackBaseUri = new Uri("http://localhost:5081/tracon");

                    if (cache is not null)
                    {
                        options.OAuthTokenStore = McpOAuthTokenStore.DistributedCache;
                    }
                });

                if (cache is not null)
                {
                    services.AddSingleton<IDistributedCache>(cache);
                    services.AddSingleton(protector);
                    services.AddSingleton(FastTiming);
                }
            });
    }

    /// <summary>The cache the two hosts share: one object, as one Redis is for two replicas.</summary>
    private sealed class SharedCache : IDistributedCache
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
        private int _writes;
        private int _tokenReads;

        public int Writes => Volatile.Read(ref _writes);

        public int TokenReads => Volatile.Read(ref _tokenReads);

        public IReadOnlyCollection<string> Keys => [.. _entries.Keys];

        public byte[]? Peek(string key) => _entries.TryGetValue(key, out var value) ? value : null;

        public byte[]? Get(string key)
        {
            if (key.Contains(":token:", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _tokenReads);
            }

            return Peek(key)?.ToArray();
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _entries.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);

            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _entries[key] = value.ToArray();
            Interlocked.Increment(ref _writes);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);

            return Task.CompletedTask;
        }
    }
}
