using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string? bearer)
    {
        var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    /// <summary>Sends the request and appends the status line, the headers and the body (a stream is read for its headers only).</summary>
    private static async Task<HttpStatusCode> SendAsync(TraconTestHost host, HttpRequestMessage request, StringBuilder seen)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            using var reply = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            seen.AppendLine(CultureInfo.InvariantCulture, $"{(int)reply.StatusCode} {reply.Headers} {reply.Content.Headers}");

            if (reply.Content.Headers.ContentType?.MediaType is not "text/event-stream")
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
