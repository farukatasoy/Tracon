using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The OAuth token cache that keeps its tokens in the application's distributed cache
/// (<c>Tracon:Mcp:OAuthTokenStore = DistributedCache</c>).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The MCP SDK reads the cache on EVERY outbound request and decides on its own when a token has
/// expired (<c>ObtainedAt + ExpiresIn</c>, no margin; <c>ModelContextProtocol.Core</c> 2.3.0). These
/// tests therefore use that rule for "expired": a token the SDK still uses must not take the refresh
/// lease, because nothing would store a new token to release it.
/// </para>
/// </remarks>
public sealed class DistributedMcpTokenCacheTests
{
    private static readonly McpTenantServerKey Key = new("acme", "prod");
    private static readonly McpTokenBinding Binding = new("https://mcp.example.com/mcp", "client");

    [Fact]
    public async Task Stored_bytes_carry_no_plaintext_token()
    {
        var cache = new RecordingDistributedCache();
        var tokens = McpOAuthTestKit.Token();

        await Cache(McpOAuthTestKit.Backend(cache)).StoreTokensAsync(tokens, CancellationToken.None);

        var stored = cache.Peek(McpOAuthCacheKeys.Token(Key)).ShouldNotBeNull();
        var text = McpOAuthTestKit.AsText(stored);

        // Every secret field: the access token, the refresh token and the client secret that
        // dynamic client registration produced. The protected value is binary, so a plain search
        // of the bytes is the test (cat -v showed what a read of the source could not).
        text.ShouldNotContain(tokens.AccessToken, Case.Sensitive);
        text.ShouldNotContain(tokens.RefreshToken!, Case.Sensitive);
        text.ShouldNotContain(tokens.ClientSecret!, Case.Sensitive);
        text.ShouldNotContain("accessToken", Case.Insensitive);
    }

    [Fact]
    public async Task A_stored_token_reads_back_with_every_field()
    {
        var cache = new RecordingDistributedCache();
        var backend = McpOAuthTestKit.Backend(cache);
        var written = McpOAuthTestKit.Token();

        await Cache(backend).StoreTokensAsync(written, CancellationToken.None);

        // A second instance of the cache: another process, same cache.
        var read = (await Cache(backend).GetTokensAsync(CancellationToken.None)).ShouldNotBeNull();

        read.AccessToken.ShouldBe(written.AccessToken);
        read.RefreshToken.ShouldBe(written.RefreshToken);
        read.ExpiresIn.ShouldBe(written.ExpiresIn);
        read.ObtainedAt.ShouldBe(written.ObtainedAt);
        read.Scope.ShouldBe(written.Scope);
        read.TokenType.ShouldBe(written.TokenType);
        read.AuthorizationServer.ShouldBe(written.AuthorizationServer);
        read.ClientId.ShouldBe(written.ClientId);
        read.ClientSecret.ShouldBe(written.ClientSecret);
        read.TokenEndpointAuthMethod.ShouldBe(written.TokenEndpointAuthMethod);
    }

    [Fact]
    public async Task The_entry_slides_for_the_configured_time()
    {
        var cache = new RecordingDistributedCache();

        await Cache(McpOAuthTestKit.Backend(cache)).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);

        cache.OptionsOf(McpOAuthCacheKeys.Token(Key)).SlidingExpiration.ShouldBe(TimeSpan.FromDays(30));
    }

    /// <summary>
    /// 🚨 A token belongs to the resource it was issued for. The SDK sends a valid cached token to
    /// whatever endpoint its transport points at, so the stored value carries the endpoint and the
    /// client id and is given to no other definition.
    /// </summary>
    [Theory]
    [InlineData("https://other.example.com/mcp", "client")]
    [InlineData("https://mcp.example.com/mcp", "another-client")]
    public async Task A_token_is_not_given_to_a_definition_it_was_not_issued_for(string endpoint, string clientId)
    {
        var backend = McpOAuthTestKit.Backend(new RecordingDistributedCache());

        await Cache(backend).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);

        var other = new DistributedMcpTokenCache(backend, Key, new McpTokenBinding(endpoint, clientId), NullLogger.Instance);

        (await other.GetTokensAsync(CancellationToken.None)).ShouldBeNull();
        (await Cache(backend).GetTokensAsync(CancellationToken.None)).ShouldNotBeNull("the definition it was issued for still gets it.");
    }

    [Fact]
    public async Task Tokens_are_per_tenant()
    {
        var backend = McpOAuthTestKit.Backend(new RecordingDistributedCache());

        await Cache(backend, new McpTenantServerKey("acme", "prod")).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);

        (await Cache(backend, new McpTenantServerKey("globex", "prod")).GetTokensAsync(CancellationToken.None))
            .ShouldBeNull("another tenant's server of the same name must not see the token.");
    }

    [Fact]
    public async Task An_unreadable_envelope_is_treated_as_absent()
    {
        var cache = new RecordingDistributedCache();
        var tokenKey = McpOAuthCacheKeys.Token(Key);
        var logs = new RecordingLoggerFactory();
        var reader = new DistributedMcpTokenCache(McpOAuthTestKit.Backend(cache), Key, Binding, logs.CreateLogger("test"));

        // Written under a key that is gone now (a rotation that dropped the old key).
        await Cache(McpOAuthTestKit.Backend(cache, protector: McpOAuthTestKit.Protector("old-key"))).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);
        (await reader.GetTokensAsync(CancellationToken.None)).ShouldBeNull();

        // Nobody ever protected this: it must not pass for a token.
        cache.Plant(tokenKey, Encoding.UTF8.GetBytes("""{"endpoint":"https://mcp.example.com/mcp","boundClientId":"client","accessToken":"forged"}"""));
        (await reader.GetTokensAsync(CancellationToken.None)).ShouldBeNull();

        // Not an envelope at all.
        cache.Plant(tokenKey, [1, 2, 3, 4, 5, 6, 7, 8]);
        (await reader.GetTokensAsync(CancellationToken.None)).ShouldBeNull();

        // A real value that was cut short.
        var real = await WriteRealValueAsync();
        cache.Plant(tokenKey, real[..(real.Length / 2)]);
        (await reader.GetTokensAsync(CancellationToken.None)).ShouldBeNull();

        logs.Count(LogLevel.Warning, "cannot be read").ShouldBe(4);

        async Task<byte[]> WriteRealValueAsync()
        {
            var scratch = new RecordingDistributedCache();
            await Cache(McpOAuthTestKit.Backend(scratch)).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);

            return scratch.Peek(tokenKey)!;
        }
    }

    [Fact]
    public async Task A_failing_cache_returns_no_token_and_logs()
    {
        var cache = new RecordingDistributedCache();
        var logs = new RecordingLoggerFactory();
        var tokens = new DistributedMcpTokenCache(McpOAuthTestKit.Backend(cache), Key, Binding, logs.CreateLogger("test"));

        await tokens.StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None);
        cache.Failing = true;

        (await tokens.GetTokensAsync(CancellationToken.None)).ShouldBeNull();
        logs.Count(LogLevel.Warning, "could not be read").ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_cache_fails_the_write()
    {
        var cache = new RecordingDistributedCache { Failing = true };

        // The SDK ends the flow that stored the token: an authorization whose token is lost is a failure.
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Cache(McpOAuthTestKit.Backend(cache)).StoreTokensAsync(McpOAuthTestKit.Token(), CancellationToken.None));
    }

    [Fact]
    public async Task An_expired_token_takes_the_refresh_lease_until_the_new_token_is_stored()
    {
        var leases = new RecordingLeaseStore(new InMemorySingletonLeaseStore());
        var backend = McpOAuthTestKit.Backend(new RecordingDistributedCache(), leases);
        var tokens = Cache(backend);
        var leaseName = McpOAuthCacheKeys.RefreshLease(Key);

        await tokens.StoreTokensAsync(Expired(), CancellationToken.None);

        (await tokens.GetTokensAsync(CancellationToken.None)).ShouldNotBeNull("the expired token goes to the SDK, which refreshes it.");
        (await leases.TryAcquireAsync(leaseName, "another-instance", TimeSpan.FromSeconds(30))).ShouldBeFalse("the refresh is serialized.");

        // The SDK asks again inside the same refresh (it reads twice, three times on a fresh connection).
        (await tokens.GetTokensAsync(CancellationToken.None)).ShouldNotBeNull();
        leases.Acquired.ShouldBe(1, "one refresh takes one lease.");

        await tokens.StoreTokensAsync(McpOAuthTestKit.Token(suffix: "2"), CancellationToken.None);

        (await leases.TryAcquireAsync(leaseName, "another-instance", TimeSpan.FromSeconds(30))).ShouldBeTrue("storing the new token releases the lease.");
    }

    [Fact]
    public async Task An_instance_that_does_not_get_the_lease_waits_for_the_new_token()
    {
        var leases = new RecordingLeaseStore(new InMemorySingletonLeaseStore());
        var backend = McpOAuthTestKit.Backend(new RecordingDistributedCache(), leases);
        var refreshing = Cache(backend);
        var waiting = Cache(backend);

        await refreshing.StoreTokensAsync(Expired(), CancellationToken.None);
        await refreshing.GetTokensAsync(CancellationToken.None);

        var read = waiting.GetTokensAsync(CancellationToken.None).AsTask();

        // The refusal is what proves the second instance is waiting rather than reading late.
        await WaitUntil.TrueAsync(() => leases.Refused == 1, "the second instance to be refused the lease");
        read.IsCompleted.ShouldBeFalse();

        await refreshing.StoreTokensAsync(McpOAuthTestKit.Token(suffix: "2"), CancellationToken.None);

        var fresh = (await read).ShouldNotBeNull();
        fresh.AccessToken.ShouldContain("-2-", Case.Sensitive);
    }

    [Fact]
    public async Task A_wait_that_ends_returns_the_expired_token()
    {
        var leases = new InMemorySingletonLeaseStore();
        var timing = McpOAuthTestKit.FastTiming with { RefreshWait = TimeSpan.FromMilliseconds(100) };
        var backend = McpOAuthTestKit.Backend(new RecordingDistributedCache(), leases, timing: timing);

        await Cache(backend).StoreTokensAsync(Expired(), CancellationToken.None);
        await leases.TryAcquireAsync(McpOAuthCacheKeys.RefreshLease(Key), "an-instance-that-never-stores", TimeSpan.FromMinutes(1));

        // Today's behavior without the lease: the caller refreshes with what it has.
        var read = (await Cache(backend).GetTokensAsync(CancellationToken.None)).ShouldNotBeNull();

        read.AccessToken.ShouldContain("-1-", Case.Sensitive);
    }

    /// <summary>
    /// The SDK stores a token only when its refresh worked. When the provider answers with an error nothing is
    /// stored and nothing releases the lease, so the lease has to end by itself.
    /// </summary>
    [Fact]
    public async Task A_lease_that_nothing_releases_ends_by_itself()
    {
        var leases = new InMemorySingletonLeaseStore();
        var timing = McpOAuthTestKit.FastTiming with { RefreshLease = TimeSpan.FromMilliseconds(100) };
        var backend = McpOAuthTestKit.Backend(new RecordingDistributedCache(), leases, timing: timing);
        var tokens = Cache(backend);
        var leaseName = McpOAuthCacheKeys.RefreshLease(Key);

        await tokens.StoreTokensAsync(Expired(), CancellationToken.None);
        await tokens.GetTokensAsync(CancellationToken.None);

        // The refresh failed: no Store call follows. The other instance is refused first, then the lease is its own.
        await WaitUntil.ValueAsync(
            async () => await leases.TryAcquireAsync(leaseName, "another-instance", TimeSpan.FromSeconds(30)),
            static acquired => acquired,
            "the lease of the failed refresh to end by itself");
    }

    [Fact]
    public async Task The_wait_for_the_new_token_ends_with_cancellation()
    {
        var leases = new RecordingLeaseStore(new InMemorySingletonLeaseStore());
        var backend = McpOAuthTestKit.Backend(new RecordingDistributedCache(), leases);
        var refreshing = Cache(backend);
        using var cancellation = new CancellationTokenSource();

        await refreshing.StoreTokensAsync(Expired(), CancellationToken.None);
        await refreshing.GetTokensAsync(CancellationToken.None);

        var waiting = Cache(backend).GetTokensAsync(cancellation.Token).AsTask();
        await WaitUntil.TrueAsync(() => leases.Refused == 1, "the second instance to be refused the lease");

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await waiting);
    }

    [Fact]
    public async Task A_token_the_sdk_still_uses_takes_no_lease()
    {
        var leases = new RecordingLeaseStore(new InMemorySingletonLeaseStore());
        var tokens = Cache(McpOAuthTestKit.Backend(new RecordingDistributedCache(), leases));

        // One second left: the SDK uses it, so nothing would ever store a new one and release a lease.
        await tokens.StoreTokensAsync(McpOAuthTestKit.Token(obtainedAt: DateTimeOffset.UtcNow.AddSeconds(-3599)), CancellationToken.None);

        (await tokens.GetTokensAsync(CancellationToken.None)).ShouldNotBeNull();
        leases.Acquired.ShouldBe(0);
        leases.Refused.ShouldBe(0);
    }

    [Fact]
    public async Task An_expired_token_without_a_refresh_token_takes_no_lease()
    {
        var leases = new RecordingLeaseStore(new InMemorySingletonLeaseStore());
        var tokens = Cache(McpOAuthTestKit.Backend(new RecordingDistributedCache(), leases));
        var expired = Expired();
        expired.RefreshToken = null!;

        await tokens.StoreTokensAsync(expired, CancellationToken.None);

        // Nothing to refresh: the SDK starts a new authorization, and no lease protects that.
        (await tokens.GetTokensAsync(CancellationToken.None)).ShouldNotBeNull();
        leases.Acquired.ShouldBe(0);
    }

    [Fact]
    public async Task A_lease_store_that_fails_does_not_stop_the_refresh()
    {
        var logs = new RecordingLoggerFactory();
        var backend = McpOAuthTestKit.Backend(new RecordingDistributedCache(), new FailingLeaseStore());
        var tokens = new DistributedMcpTokenCache(backend, Key, Binding, logs.CreateLogger("test"));

        await tokens.StoreTokensAsync(Expired(), CancellationToken.None);

        (await tokens.GetTokensAsync(CancellationToken.None)).ShouldNotBeNull("without a lease the refresh runs as it does in memory mode.");
        logs.Count(LogLevel.Warning, "could not be taken").ShouldBe(1);
    }

    private static DistributedMcpTokenCache Cache(McpOAuthDistributedBackend backend, McpTenantServerKey? key = null)
        => new(backend, key ?? Key, Binding, NullLogger.Instance);

    private static ModelContextProtocol.Authentication.TokenContainer Expired()
        => McpOAuthTestKit.Token(obtainedAt: DateTimeOffset.UtcNow.AddHours(-2));

    private sealed class FailingLeaseStore : ISingletonLeaseStore
    {
        public ValueTask<bool> TryAcquireAsync(string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The lease store is unavailable.");

        public ValueTask<bool> RenewAsync(string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The lease store is unavailable.");

        public ValueTask ReleaseAsync(string name, string ownerId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The lease store is unavailable.");
    }
}
