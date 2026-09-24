using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A credential header declared by configuration key NAME reaches the remote
/// MCP server and the webhook receiver with the value read from
/// configuration, and a key outside the record's tenant is never read
/// (phase 190).
/// </summary>
/// <remarks>
/// Both targets are real HTTP servers on loopback: the header is added by the
/// transport, after the SDK and the delivery handler have had their say, so
/// only a receiver proves it arrived.
/// </remarks>
public sealed class CredentialHeaderDeliveryTests
{
    private const string McpKey = "Tracon:McpSecrets:DemoKey";
    private const string WebhookKey = "Tracon:WebhookSecrets:DemoKey";
    private const string CredentialValue = "dogrulama-degeri";

    [Fact]
    public async Task Mcp_connection_sends_the_resolved_header_and_no_response_returns_it()
    {
        await using var target = await HeaderCapturingServer.StartAsync();
        await using var host = await StartAsync();

        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri("/tracon/api/mcp-servers/m1", UriKind.Relative),
                   CredentialHeaderSaveTests.McpBody(
                       headers: CredentialHeaderSaveTests.Map(("X-Tenant", "plain-190")),
                       keys: CredentialHeaderSaveTests.Map(("X-API-Key", McpKey)),
                       endpoint: target.Mcp.ToString())))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        var toolCount = await RefreshAsync(host);

        toolCount.ShouldBeGreaterThan(0, "the server answered, so its tool is listed");
        target.Values("X-API-Key").ShouldNotBeEmpty();
        target.Values("X-API-Key").ShouldAllBe(static value => value == CredentialValue);
        target.Values("X-Tenant").ShouldAllBe(static value => value == "plain-190");

        var listed = await host.Client.GetStringAsync(new Uri("/tracon/api/mcp-servers", UriKind.Relative));
        listed.ShouldNotContain(CredentialValue);
        listed.ShouldContain(McpKey);
        host.Logs.AllText.ShouldNotContain(CredentialValue);
    }

    /// <summary>
    /// A record written by a third-party store, or before the rule existed,
    /// may name a key of another tenant. The connection must fail before the
    /// value is read — not send it.
    /// </summary>
    [Fact]
    public async Task Mcp_key_outside_the_tenant_is_never_resolved()
    {
        await using var target = await HeaderCapturingServer.StartAsync();
        await using var host = await StartAsync();

        await host.Services.GetRequiredService<IMcpServerStore>().SaveAsync(new McpServerDefinition
        {
            Id = Guid.Empty,
            TenantId = "default",
            Name = "m1",
            Endpoint = target.Mcp,
            HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-API-Key"] = "Tracon:McpSecrets:globex:DemoKey",
            },
        });

        (await RefreshAsync(host)).ShouldBe(0);

        target.RequestCount.ShouldBe(0, "the connection failed before a single request");
        host.Logs.AllText.ShouldContain("headerConfigurationKeys[X-API-Key]");
        host.Logs.AllText.ShouldNotContain(CredentialValue);
    }

    [Fact]
    public async Task Webhook_delivery_sends_the_resolved_header()
    {
        await using var receiver = await HeaderCapturingServer.StartAsync();
        await using var host = await StartAsync();

        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri("/tracon/api/webhooks/w1", UriKind.Relative),
                   CredentialHeaderSaveTests.WebhookBody(
                       headers: CredentialHeaderSaveTests.Map(("X-Tenant", "plain-190")),
                       keys: CredentialHeaderSaveTests.Map(("X-API-Key", WebhookKey)),
                       url: receiver.Hook.ToString())))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        await SendTestEventAsync(host);

        var delivery = await WaitForDeliveryAsync(host);

        delivery.GetProperty("status").GetString().ShouldBe("Delivered");
        receiver.Values("X-API-Key").ShouldBe([CredentialValue]);
        receiver.Values("X-Tenant").ShouldBe(["plain-190"]);
        host.Logs.AllText.ShouldNotContain(CredentialValue);
    }

    [Fact]
    public async Task Webhook_key_outside_the_tenant_drops_the_delivery()
    {
        await using var receiver = await HeaderCapturingServer.StartAsync();
        await using var host = await StartAsync();

        await host.Services.GetRequiredService<IWebhookStore>().SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = "default",
            Name = "w1",
            Url = receiver.Hook.ToString(),
            Events = [WebhookEvents.RunCompleted],
            HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-API-Key"] = "Tracon:WebhookSecrets:globex:DemoKey",
            },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await SendTestEventAsync(host);

        var delivery = await WaitForDeliveryAsync(host);

        delivery.GetProperty("status").GetString().ShouldBe("Dropped");

        var error = delivery.GetProperty("error").GetString().ShouldNotBeNull();
        error.ShouldContain("headerConfigurationKeys[X-API-Key]");
        error.ShouldContain("Tracon:WebhookSecrets:");
        receiver.RequestCount.ShouldBe(0);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(
            static builder => builder.UseMcp(),
            configureServices: static services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection(
                    [
                        new(McpKey, CredentialValue),
                        new(WebhookKey, CredentialValue),
                        new("Tracon:McpSecrets:globex:DemoKey", CredentialValue),
                        new("Tracon:WebhookSecrets:globex:DemoKey", CredentialValue),
                    ])
                    .Build());
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconWebhookOptions>(static options => options.AllowInsecureHttp = true);
                services.Configure<TraconSchedulingOptions>(static options => options.PollInterval = TimeSpan.FromMilliseconds(50));
            });

    private static async Task<int> RefreshAsync(TraconTestHost host)
    {
        using var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());

        return (await TraconTestHost.ReadJsonAsync(refreshed)).GetProperty("toolCount").GetInt32();
    }

    private static async Task SendTestEventAsync(TraconTestHost host)
    {
        using var test = await host.Client.PostAsJsonAsync(new Uri("/tracon/api/webhooks/w1/test", UriKind.Relative), new { });

        test.StatusCode.ShouldBe(HttpStatusCode.OK, await test.Content.ReadAsStringAsync());
    }

    private static Task<System.Text.Json.JsonElement> WaitForDeliveryAsync(TraconTestHost host)
        => WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync(new Uri("/tracon/api/webhooks/w1/deliveries", UriKind.Relative));
                var deliveries = await TraconTestHost.ReadJsonAsync(response);

                return deliveries.GetArrayLength() == 0 ? default : deliveries[0];
            },
            static delivery => delivery.ValueKind == System.Text.Json.JsonValueKind.Object
                && delivery.GetProperty("status").GetString() is "Delivered" or "Dropped" or "Failed",
            "the delivery to settle");

    /// <summary>A real server on loopback: an MCP endpoint with one tool, and a webhook receiver.</summary>
    private sealed class HeaderCapturingServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<IHeaderDictionary> _requests = new();

        private HeaderCapturingServer(WebApplication app, Uri address)
        {
            _app = app;
            Mcp = new Uri(address, "mcp");
            Hook = new Uri(address, "hook");
        }

        public Uri Mcp { get; }

        public Uri Hook { get; }

        public int RequestCount => _requests.Count;

        public IReadOnlyList<string> Values(string header)
            => [.. _requests.Where(headers => headers.ContainsKey(header)).Select(headers => headers[header].ToString())];

        public static async Task<HeaderCapturingServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithTools([McpServerTool.Create(
                    [Description("Looks the value up.")] () => "found",
                    new McpServerToolCreateOptions { Name = "lookup" })]);

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");

            HeaderCapturingServer? server = null;

            app.Use(async (context, next) =>
            {
                server!._requests.Enqueue(new HeaderDictionary(context.Request.Headers.ToDictionary(StringComparer.OrdinalIgnoreCase)));
                await next(context);
            });

            app.MapMcp("/mcp");
            app.MapPost("/hook", static () => Results.Ok());

            await app.StartAsync();

            server = new HeaderCapturingServer(app, new Uri(app.Urls.First().TrimEnd('/') + "/"));

            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
