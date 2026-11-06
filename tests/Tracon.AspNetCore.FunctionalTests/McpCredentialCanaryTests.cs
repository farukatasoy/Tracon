using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The credentials an MCP server and a webhook are called with are sent to that server and appear nowhere else: the
/// OAuth client secret and the tokens it earns, and the plain header values a record written before the credential rule
/// still carries.
/// </summary>
/// <remarks>
/// <para>
/// The OAuth flow is the real one, against a server that is its own authorization server: the authorization starts, the
/// redirect comes back, the code is exchanged (the client's secret travels in that request), the tools are listed with
/// the token, and a refresh rotates it. The token store is the distributed cache with content protection on, so the cache
/// a test hands in is the place the tokens are kept; what it holds is read back and has to be ciphertext. The test
/// asks the server what it was sent, so the secret is known to have been used.
/// </para>
/// <para>
/// A plain header with a credential-looking name is refused at the HTTP boundary, so the legacy record is written through
/// the store, as a record written by a third-party store or before the rule is. Its values are sent on the wire (a
/// receiver says so) and must reach no response, export, audit entry, delivery record or log record.
/// </para>
/// </remarks>
public sealed class McpCredentialCanaryTests
{
    private const string ClientSecretKey = "Tracon:McpSecrets:OAuthClient";
    private const string ClientSecretStem = "canary-oauth-client-secret";
    private const string ClientSecret = ClientSecretStem + "-9a3e61d5c0f8472b";

    private const string LegacyMcpStem = "canary-legacy-mcp-header";
    private const string LegacyHookStem = "canary-legacy-webhook-header";

    [Fact]
    public async Task The_oauth_client_secret_and_the_tokens_of_an_mcp_server_are_in_no_output_and_the_cache_holds_ciphertext()
    {
        using var database = new TempSqliteDatabase("mcp-oauth-canary");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();
        var cache = new RecordingCache();

        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.UseSqlite(database.ConnectionString).UseMcp(),
            configureServices: services =>
            {
                services.AddSingleton<IDistributedCache>(cache);
                services.AddSingleton(McpOAuthKit.Protector());
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection([new(ClientSecretKey, ClientSecret)]).Build());
                services.AddLogging(logging => logging.AddProvider(structured));
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconMcpOptions>(static options =>
                {
                    options.OAuthCallbackBaseUri = new Uri("http://localhost:5081/tracon");
                    options.OAuthTokenStore = McpOAuthTokenStore.DistributedCache;
                });
                services.Configure<TraconOptions>(static options =>
                {
                    options.Observability.RecordSensitiveData = true;
                    options.Observability.SuccessSampleRatio = 1;
                });
            });

        var drive = new CanaryDrive(host);

        (await drive.SendAsync(
            HttpMethod.Put,
            $"/tracon/api/mcp-servers/{OAuthFlowClient.ServerName}",
            new
            {
                endpoint = server.Endpoint.ToString(),
                enabled = true,
                oauthEnabled = true,
                oauthClientId = OAuthFlowClient.ClientId,
                oauthClientSecretConfigurationKey = ClientSecretKey,
                requiresApproval = false,
            })).ShouldBe(HttpStatusCode.OK);

        // A callback for a flow nobody started, then the real one, then the tool list and a refresh.
        drive.Keep(await OAuthFlowClient.CallbackAsync(host, state: "not-a-state-the-flow-issued"));
        var page = await OAuthFlowClient.AuthorizeAsync(host);
        drive.Keep(page);
        page.ShouldContain("Authorization complete", Case.Sensitive);

        var names = await WaitUntil.ValueAsync(() => OAuthFlowClient.ToolNamesAsync(host), OAuthFlowClient.HasLookup);
        drive.Keep(string.Join(',', names));

        await OAuthFlowClient.RefreshAsync(host);
        (await drive.ReadEverythingAsync()).ShouldBeGreaterThan(40);

        // The client's secret reached the token endpoint, in the form or as Basic credentials; the tokens are real.
        server.ClientCredentials.ShouldNotBeEmpty();
        string.Join('\n', server.ClientCredentials.Select(Decoded)).ShouldContain(ClientSecretStem, Case.Sensitive, "the token endpoint was never sent the client's secret");
        server.AuthorizationHeaders.ShouldContain(header => header.Contains(server.AccessToken, StringComparison.Ordinal), "the MCP server was never sent the access token");
        server.IssuedRefreshTokens.ShouldNotBeEmpty();

        // The cache is where the tokens are kept; what it holds is not readable as a token.
        cache.Values.ShouldNotBeEmpty("the token store kept nothing, so the cache search proves nothing");

        var secrets = new List<KeyValuePair<string, string>>
        {
            new("OAuth client secret", ClientSecretStem),
            new("access token", server.AccessToken),
        };
        secrets.AddRange(server.IssuedRefreshTokens.Select(static token => new KeyValuePair<string, string>("refresh token", token)));

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = drive.Seen,
            ["log lines"] = host.Logs.AllText,
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
            ["distributed cache values"] = Encoding.UTF8.GetString(cache.Values.SelectMany(static value => value).ToArray()),
        };

        var hits = SecretHits.Find(outputs, secrets);
        var bytes = database.ReadAllBytes();

        SecretHits.FindInBytes(bytes, OAuthFlowClient.ServerName).ShouldNotBeEmpty("the server record is not in the database, so the scan reads the wrong place");

        foreach (var (name, value) in secrets)
        {
            foreach (var encoding in SecretHits.FindInBytes(bytes, value))
            {
                hits.Add($"the {name} is in the database as {encoding}");
            }
        }

        hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_plain_header_values_of_a_legacy_mcp_server_and_webhook_are_in_no_output()
    {
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var receiver = await HeaderReceiver.StartAsync();
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.UseMcp().AddAgent(TestData.Definition()),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: services =>
            {
                services.AddLogging(logging => logging.AddProvider(structured));
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconWebhookOptions>(static options => options.AllowInsecureHttp = true);
                services.Configure<TraconSchedulingOptions>(static options => options.PollInterval = TimeSpan.FromMilliseconds(50));
            });

        var mcpValue = LegacyMcpStem + "-4e7c20a9b1d35f68";
        var hookValue = LegacyHookStem + "-d1950b3e7a2c4486";

        // Written through the stores, as a third-party store or an older version wrote them: credential-looking names, plain values.
        await host.Services.GetRequiredService<IMcpServerStore>().SaveAsync(new McpServerDefinition
        {
            Id = Guid.Empty,
            TenantId = "default",
            Name = "legacy-mcp",
            Endpoint = new Uri(receiver.Address + "/mcp"),
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Authorization"] = "Bearer " + mcpValue, ["X-Api-Key"] = mcpValue },
        });

        await host.Services.GetRequiredService<IWebhookStore>().SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = Guid.Empty,
            TenantId = "default",
            Name = "legacy-hook",
            Url = receiver.Address + "/hook",
            Events = ["run.completed"],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = "Bearer " + hookValue, ["X-Api-Key"] = hookValue },
        });

        var drive = new CanaryDrive(host);

        await drive.SendAsync(HttpMethod.Post, "/tracon/api/mcp-servers/refresh", new { });
        (await drive.SendAsync(HttpMethod.Post, "/tracon/api/webhooks/legacy-hook/test", new { })).ShouldBe(HttpStatusCode.OK);

        // An update that leaves the headers out keeps the old ones: the audit entry and the answer must not carry them.
        await drive.SendAsync(HttpMethod.Put, "/tracon/api/webhooks/legacy-hook", new { url = receiver.Address + "/hook", events = new[] { "run.completed" }, enabled = true });
        await drive.SendAsync(HttpMethod.Put, "/tracon/api/mcp-servers/legacy-mcp", new { endpoint = receiver.Address + "/mcp", enabled = true });

        var settled = await WaitUntil.ValueAsync(
            async () =>
            {
                using var reply = await host.Client.GetAsync(new Uri("/tracon/api/webhooks/legacy-hook/deliveries", UriKind.Relative), TestContext.Current.CancellationToken);
                var deliveries = await TraconTestHost.ReadJsonAsync(reply);

                return deliveries.GetArrayLength() == 0 ? string.Empty : deliveries[0].GetProperty("status").GetString() ?? string.Empty;
            },
            static status => status is "Delivered" or "Dropped" or "Failed",
            "the delivery to settle");

        settled.ShouldNotBeEmpty();

        foreach (var path in (string[])["/tracon/api/webhooks", "/tracon/api/webhooks/legacy-hook", "/tracon/api/webhooks/legacy-hook/deliveries", "/tracon/api/mcp-servers", "/tracon/api/mcp-servers/legacy-mcp"])
        {
            await drive.SendAsync(HttpMethod.Get, path);
        }

        (await drive.ReadEverythingAsync()).ShouldBeGreaterThan(40);

        // The values were sent: both targets saw them.
        receiver.Seen.ShouldContain(seen => seen.Contains(mcpValue, StringComparison.Ordinal), "the MCP connection never sent the legacy header");
        receiver.Seen.ShouldContain(seen => seen.Contains(hookValue, StringComparison.Ordinal), "the webhook delivery never sent the legacy header");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = drive.Seen,
            ["log lines"] = host.Logs.AllText,
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        SecretHits.Find(outputs, [new("legacy MCP header value", LegacyMcpStem), new("legacy webhook header value", LegacyHookStem)]).ShouldBeEmpty();
    }

    private static string Decoded(string credential)
    {
        // A Basic credential is Base64 of "id:secret": decoded so the secret can be looked for in it.
        var basic = credential.IndexOf("Basic ", StringComparison.Ordinal);

        if (basic < 0)
        {
            return credential;
        }

        try
        {
            return credential + " " + Encoding.UTF8.GetString(Convert.FromBase64String(credential[(basic + "Basic ".Length)..].Trim()));
        }
        catch (FormatException)
        {
            return credential;
        }
    }

    /// <summary>A distributed cache that keeps what it is handed, so a test can read the bytes a store wrote.</summary>
    private sealed class RecordingCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

        public IReadOnlyCollection<byte[]> Values => [.. _entries.Values];

        public byte[]? Get(string key) => _entries.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Refresh(string key)
        {
            // Entries do not expire here.
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _entries.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);

            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);

            return Task.CompletedTask;
        }
    }

    /// <summary>A server on the loopback interface that answers a webhook with 200 and an MCP endpoint with 404, and keeps the headers it was sent.</summary>
    private sealed class HeaderReceiver : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<string> _seen = new();

        private HeaderReceiver(WebApplication app, string address)
        {
            _app = app;
            Address = address;
        }

        public string Address { get; }

        public IReadOnlyCollection<string> Seen => [.. _seen];

        public static async Task<HeaderReceiver> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            HeaderReceiver? receiver = null;

            app.MapPost("/hook", (HttpRequest request) =>
            {
                receiver!._seen.Enqueue(string.Join(" | ", request.Headers.Select(static header => $"{header.Key}={header.Value}")));

                return Results.Ok();
            });

            app.MapMethods("/mcp", ["POST", "GET", "DELETE"], (HttpRequest request) =>
            {
                receiver!._seen.Enqueue(string.Join(" | ", request.Headers.Select(static header => $"{header.Key}={header.Value}")));

                return Results.NotFound();
            });

            await app.StartAsync();

            receiver = new HeaderReceiver(app, app.Urls.First());

            return receiver;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
