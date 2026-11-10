using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Three secrets the installation holds, each with a value nobody could guess, reach no response, no header and no log
/// line while the whole read surface and one run are driven.
/// </summary>
/// <remarks>
/// <para>
/// The secrets are the static token that guards the endpoints, the API key a provider is called with, and an API key
/// the installation issued. Each has a point test where it was once found in the wrong place; this is the sweep that
/// asks the question of every read operation at once, including the ones nobody has thought to ask about. The
/// response that issues the key is left out of the search, because that response is the one place the key belongs.
/// </para>
/// <para>
/// A search that finds nothing proves nothing unless the sweep reached something, so the test also asks for a count
/// of operations that answered 200 and for a completed run.
/// </para>
/// </remarks>
public sealed class SecretCanarySweepTests
{
    private const string StaticToken = "canary-static-token-7f3a91c2e0b84d5a";
    private const string ProviderKey = "canary-provider-key-4b8e60d1a9c37f25";
    private const string WebhookConfigurationKey = "Tracon:WebhookSecrets:CanaryKey";
    private const string McpConfigurationKey = "Tracon:McpSecrets:CanaryKey";
    private const string WebhookSecret = "canary-webhook-header-1d95c7e2f06a8b43";
    private const string McpSecret = "canary-mcp-header-8a2f4c61d03e9b57";

    [Fact]
    public async Task No_secret_reaches_a_response_a_header_or_a_log_line()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .UseOpenAI(options => options.ApiKey = ProviderKey)
                .AddAgent(TestData.Definition()),
            configureEndpoints: static options =>
            {
                options.AuthToken = StaticToken;
                options.EnableDiagnosticsEndpoint = true;
            });

        var seen = new StringBuilder();

        string issuedKey;

        using (var create = Authorized(HttpMethod.Post, "/tracon/api/api-keys", StaticToken))
        {
            create.Content = JsonContent.Create(new { name = "canary-issued", scopes = new[] { "RunsRead", "AgentsRead" } });
            using var created = await host.Client.SendAsync(create, TestContext.Current.CancellationToken);
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
            issuedKey = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("plaintextKey").GetString()!;
        }

        issuedKey.Length.ShouldBeGreaterThan(16);

        var reads = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is not null)
            .Where(static endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET", StringComparer.Ordinal) == true)
            .Select(static endpoint => EndpointAuthorizationMatrixTests.Fill(endpoint.RoutePattern.RawText!, "default"))
            .ToList();

        var answered = 0;

        foreach (var url in reads)
        {
            foreach (var credential in new[] { StaticToken, issuedKey, "not-a-credential", null })
            {
                using var request = Authorized(HttpMethod.Get, url, credential);
                var status = await SendAsync(host, request, seen);

                if (status == HttpStatusCode.OK && string.Equals(credential, StaticToken, StringComparison.Ordinal))
                {
                    answered++;
                }
            }
        }

        // One real run, then everything it left behind.
        using var run = Authorized(HttpMethod.Post, "/tracon/api/agents/kod-agent/run", StaticToken);
        run.Content = JsonContent.Create(new { message = "hello" });
        using var runReply = await host.Client.SendAsync(run, TestContext.Current.CancellationToken);
        var runBody = await runReply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        seen.AppendLine(runBody);
        runReply.StatusCode.ShouldBe(HttpStatusCode.OK, runBody);

        using var runList = Authorized(HttpMethod.Get, "/tracon/api/runs", StaticToken);
        using var listed = await host.Client.SendAsync(runList, TestContext.Current.CancellationToken);
        var runId = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().First().GetProperty("id").GetGuid();

        foreach (var url in (string[])[$"/tracon/api/runs/{runId}", $"/tracon/api/runs/{runId}/events", $"/tracon/api/runs/{runId}/trace"])
        {
            using var request = Authorized(HttpMethod.Get, url, StaticToken);
            await SendAsync(host, request, seen);
        }

        answered.ShouldBeGreaterThan(40, "the sweep reached too few operations for an empty search to mean anything");

        var everything = seen + "\n" + host.Logs.AllText;

        foreach (var (name, value) in (ValueTuple<string, string>[])[("static token", StaticToken), ("provider key", ProviderKey), ("issued key", issuedKey)])
        {
            everything.ShouldNotContain(value, customMessage: $"the {name} reached a response, a header or a log line");
        }
    }

    [Fact]
    public async Task The_header_secrets_of_a_webhook_and_an_mcp_server_reach_no_response_a_header_or_a_log_line()
    {
        // The two values are read from configuration by the key a record names, and sent as a request header. They are
        // used on the way out (the webhook is delivered, the MCP connection fails), so a leak would come from an error
        // text, a delivery record, a server status or a log line.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.UseMcp().AddAgent(TestData.Definition()),
            configureEndpoints: static options =>
            {
                options.AuthToken = StaticToken;
                options.EnableDiagnosticsEndpoint = true;
            },
            configureServices: static services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection(
                    [
                        new(WebhookConfigurationKey, WebhookSecret),
                        new(McpConfigurationKey, McpSecret),
                    ])
                    .Build());
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconWebhookOptions>(static options => options.AllowInsecureHttp = true);
                services.Configure<TraconSchedulingOptions>(static options => options.PollInterval = TimeSpan.FromMilliseconds(50));
            });

        var seen = new StringBuilder();

        // A receiver that answers 200 to the webhook; the MCP endpoint on the same server answers 404, so that connection fails.
        var receiverBuilder = WebApplication.CreateSlimBuilder();
        receiverBuilder.Logging.ClearProviders();
        await using var receiver = receiverBuilder.Build();
        receiver.Urls.Add("http://127.0.0.1:0");
        var delivered = false;
        receiver.MapPost("/hook", (HttpRequest request) =>
        {
            delivered = string.Equals(request.Headers["X-API-Key"], WebhookSecret, StringComparison.Ordinal);

            return Results.Ok();
        });
        await receiver.StartAsync(TestContext.Current.CancellationToken);

        var address = receiver.Urls.First();

        using (var webhook = Authorized(HttpMethod.Put, "/tracon/api/webhooks/w1", StaticToken))
        {
            webhook.Content = JsonContent.Create(new
            {
                url = $"{address}/hook",
                events = new[] { "run.completed" },
                headerConfigurationKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-API-Key"] = WebhookConfigurationKey },
                enabled = true,
            });
            (await SendAsync(host, webhook, seen)).ShouldBe(HttpStatusCode.OK);
        }

        using (var mcp = Authorized(HttpMethod.Put, "/tracon/api/mcp-servers/m1", StaticToken))
        {
            mcp.Content = JsonContent.Create(new
            {
                endpoint = $"{address}/mcp",
                headerConfigurationKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-API-Key"] = McpConfigurationKey },
            });
            (await SendAsync(host, mcp, seen)).ShouldBe(HttpStatusCode.OK);
        }

        using (var refresh = Authorized(HttpMethod.Post, "/tracon/api/mcp-servers/refresh", StaticToken))
        {
            await SendAsync(host, refresh, seen);
        }

        using (var test = Authorized(HttpMethod.Post, "/tracon/api/webhooks/w1/test", StaticToken))
        {
            test.Content = JsonContent.Create(new { });
            (await SendAsync(host, test, seen)).ShouldBe(HttpStatusCode.OK);
        }

        // The delivery settles before the sweep looks at what it left.
        var settled = await WaitUntil.ValueAsync(
            async () =>
            {
                using var request = Authorized(HttpMethod.Get, "/tracon/api/webhooks/w1/deliveries", StaticToken);
                using var reply = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
                var deliveries = await TraconTestHost.ReadJsonAsync(reply);

                return deliveries.GetArrayLength() == 0 ? string.Empty : deliveries[0].GetProperty("status").GetString() ?? string.Empty;
            },
            static status => status is "Delivered" or "Dropped" or "Failed",
            "the delivery to settle");

        settled.ShouldNotBeEmpty();
        delivered.ShouldBeTrue("the secret has to have been resolved and sent, or an empty search proves nothing");

        var reads = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is not null)
            .Where(static endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET", StringComparer.Ordinal) == true)
            .Select(static endpoint => EndpointAuthorizationMatrixTests.Fill(endpoint.RoutePattern.RawText!, "default"))
            .Concat(["/tracon/api/webhooks/w1/deliveries", "/tracon/api/mcp-servers/m1", "/tracon/api/webhooks/w1"])
            .ToList();

        var answered = 0;

        foreach (var url in reads)
        {
            using var request = Authorized(HttpMethod.Get, url, StaticToken);

            if (await SendAsync(host, request, seen) == HttpStatusCode.OK)
            {
                answered++;
            }
        }

        answered.ShouldBeGreaterThan(40, "the sweep reached too few operations for an empty search to mean anything");

        var everything = seen + "\n" + host.Logs.AllText;

        everything.ShouldContain(McpConfigurationKey, customMessage: "the record names the key, so the sweep has to have seen it (it reached the records)");

        foreach (var (name, value) in (ValueTuple<string, string>[])[("webhook header secret", WebhookSecret), ("mcp header secret", McpSecret)])
        {
            everything.ShouldNotContain(value, customMessage: $"the {name} reached a response, a header or a log line");
        }
    }

    internal static HttpRequestMessage Authorized(HttpMethod method, string url, string? bearer)
    {
        var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    /// <summary>
    /// Sends the request and appends the status line, the headers and the body (a stream is read for its headers only,
    /// unless <paramref name="readEventStreams"/> says the stream ends by itself).
    /// </summary>
    internal static async Task<HttpStatusCode> SendAsync(
        TraconTestHost host,
        HttpRequestMessage request,
        StringBuilder seen,
        bool readEventStreams = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10)); // cancel: bound

        try
        {
            using var reply = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            seen.AppendLine(CultureInfo.InvariantCulture, $"{(int)reply.StatusCode} {reply.Headers} {reply.Content.Headers}");

            if (readEventStreams || reply.Content.Headers.ContentType?.MediaType is not "text/event-stream")
            {
                seen.AppendLine(await reply.Content.ReadAsStringAsync(timeout.Token));
            }

            return reply.StatusCode;
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            return HttpStatusCode.RequestTimeout;
        }
    }
}
