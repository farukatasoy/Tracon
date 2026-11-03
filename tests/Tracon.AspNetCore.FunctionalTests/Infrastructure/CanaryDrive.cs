using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Drives one host the way a canary run needs and keeps every byte that came back: the status line, the headers and the
/// body of each response.
/// </summary>
/// <remarks>
/// The drive is the same on every store and every provider: the same requests, so a difference between two hosts is the
/// host's and not the drive's. A request is sent as one tenant (or as none), and a stream is read to its end.
/// </remarks>
internal sealed partial class CanaryDrive(TraconTestHost host, string? tenant = null)
{
    private const string TenantHeader = "X-Tracon-Tenant";

    private readonly StringBuilder _seen = new();

    /// <summary>Gets every response of the drive: status line, headers and body, one after the other.</summary>
    public string Seen
    {
        get
        {
            lock (_seen)
            {
                return _seen.ToString();
            }
        }
    }

    /// <summary>Sends one request and keeps what came back.</summary>
    /// <param name="method">The method.</param>
    /// <param name="url">The path, relative to the host.</param>
    /// <param name="body">The JSON body, or <see langword="null"/>.</param>
    /// <param name="idempotent">Sends an <c>Idempotency-Key</c>, which makes a run answer in one JSON body.</param>
    /// <param name="asTenant">The tenant of this request; <see langword="null"/> uses the drive's.</param>
    /// <param name="cancelAfter">Abandons the request after this long, as a client that disconnects does.</param>
    /// <returns>The status code, or <see cref="HttpStatusCode.RequestTimeout"/> when the request did not finish.</returns>
    public async Task<HttpStatusCode> SendAsync(
        HttpMethod method,
        string url,
        object? body = null,
        bool idempotent = false,
        string? asTenant = null,
        TimeSpan? cancelAfter = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));

        if ((asTenant ?? tenant) is { } sent)
        {
            request.Headers.Add(TenantHeader, sent);
        }

        if (idempotent)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(cancelAfter ?? TimeSpan.FromSeconds(30));

        try
        {
            using var reply = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            var text = await reply.Content.ReadAsStringAsync(limit.Token);

            Keep($"{(int)reply.StatusCode} {reply.Headers} {reply.Content.Headers}\n{text}");

            return reply.StatusCode;
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            return HttpStatusCode.RequestTimeout;
        }
        catch (Exception exception) when (exception is HttpRequestException or TraconException or InvalidOperationException)
        {
            // The in-process server surfaces a failure that a real server would answer with 500 as an exception of the client,
            // or of the request itself. Its whole text is kept: it is what a caller of that failure would be shown, and more.
            Keep($"transport failure: {exception}");

            return HttpStatusCode.InternalServerError;
        }
    }

    /// <summary>Appends text the caller read itself.</summary>
    /// <param name="text">The text.</param>
    public void Keep(string text)
    {
        lock (_seen)
        {
            _seen.AppendLine(text);
        }
    }

    /// <summary>
    /// Runs the agent the six ways a caller can: the agent endpoint (one JSON body and a stream), Chat Completions
    /// (buffered and streamed) and Responses (buffered and streamed).
    /// </summary>
    /// <param name="agent">The agent's name, which is also the <c>model</c> of the compatible endpoints.</param>
    /// <returns>The six statuses, in that order.</returns>
    public async Task<IReadOnlyList<HttpStatusCode>> RunTheSixWaysAsync(string agent)
    {
        var message = new[] { new { role = "user", content = "hello" } };

        return
        [
            await SendAsync(HttpMethod.Post, $"/tracon/api/agents/{agent}/run", new { message = "hello" }, idempotent: true),
            await SendAsync(HttpMethod.Post, $"/tracon/api/agents/{agent}/run", new { message = "hello" }),
            await SendAsync(HttpMethod.Post, "/tracon/v1/chat/completions", new { model = agent, messages = message }),
            await SendAsync(HttpMethod.Post, "/tracon/v1/chat/completions", new { model = agent, stream = true, messages = message }),
            await SendAsync(HttpMethod.Post, "/tracon/v1/responses", new { model = agent, input = "hello" }),
            await SendAsync(HttpMethod.Post, "/tracon/v1/responses", new { model = agent, stream = true, input = "hello" }),
        ];
    }

    /// <summary>Starts a streamed run and abandons it after a moment, as a client that disconnects does.</summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="after">How long the client waits.</param>
    /// <returns>A task that completes when the client has given up.</returns>
    public async Task AbandonStreamedRunAsync(string agent, TimeSpan after)
        => await SendAsync(HttpMethod.Post, $"/tracon/api/agents/{agent}/run", new { message = "hello" }, cancelAfter: after);

    /// <summary>Waits until no run of the tenant is still running.</summary>
    /// <returns>A task that completes when every run has reached a terminal state; it throws when one does not in time.</returns>
    public async Task WaitForRunsToSettleAsync()
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(20);

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/tracon/api/runs?limit=200", UriKind.Relative));

            if (tenant is not null)
            {
                request.Headers.Add(TenantHeader, tenant);
            }

            using var reply = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
            var runs = await TraconTestHost.ReadJsonAsync(reply);

            if (!runs.EnumerateArray().Any(static run => run.GetProperty("status").GetString() is { } status
                && (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase))))
            {
                return;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(until, "a run is still running 20 seconds after its client left");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Reads what the runs left behind the way an operator reads it: the list, every part of every run, the export, the
    /// audit trail, the diagnostics, and every other read operation the host serves.
    /// </summary>
    /// <param name="replayFirstCompleted">Also replays the first completed run, which calls the provider again.</param>
    /// <returns>The number of read operations that answered <c>200</c>.</returns>
    public async Task<int> ReadEverythingAsync(bool replayFirstCompleted = false)
    {
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/tracon/api/runs?limit=200", UriKind.Relative));

        if (tenant is not null)
        {
            listRequest.Headers.Add(TenantHeader, tenant);
        }

        using var listReply = await host.Client.SendAsync(listRequest, TestContext.Current.CancellationToken);
        var runs = await TraconTestHost.ReadJsonAsync(listReply);

        if (replayFirstCompleted)
        {
            var completed = runs.EnumerateArray().FirstOrDefault(static run => string.Equals(run.GetProperty("status").GetString(), "completed", StringComparison.OrdinalIgnoreCase));

            if (completed.ValueKind is System.Text.Json.JsonValueKind.Object)
            {
                await SendAsync(HttpMethod.Post, $"/tracon/api/runs/{completed.GetProperty("id").GetGuid()}/replay", new { toolMode = "LiveTools" });
            }
        }

        foreach (var run in runs.EnumerateArray())
        {
            var id = run.GetProperty("id").GetGuid();

            foreach (var suffix in (string[])["", "/events", "/trace", "/tools", "/input", "/tree", "/usage", "/feedback"])
            {
                await SendAsync(HttpMethod.Get, $"/tracon/api/runs/{id}{suffix}");
            }
        }

        foreach (var path in (string[])["/tracon/api/definitions/export", "/tracon/api/audit?limit=500", "/tracon/api/diagnostics", "/tracon/api/meta", "/tracon/api/agents", "/tracon/api/runs?limit=200"])
        {
            await SendAsync(HttpMethod.Get, path);
        }

        var reads = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is not null)
            .Where(static endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains("GET", StringComparer.Ordinal) == true)
            .Select(static endpoint => FillRoute(endpoint.RoutePattern.RawText!, "default"))
            .ToList();

        var answered = 0;

        foreach (var url in reads)
        {
            if (await SendAsync(HttpMethod.Get, url) == HttpStatusCode.OK)
            {
                answered++;
            }
        }

        return answered;
    }

    private static string FillRoute(string template, string tenantParameter)
        => ParameterPattern().Replace(
            template,
            match =>
            {
                if (string.Equals(match.Groups["name"].Value, "tenantId", StringComparison.Ordinal))
                {
                    return tenantParameter;
                }

                var constraint = match.Groups["constraint"].Value;

                if (constraint.Contains("guid", StringComparison.OrdinalIgnoreCase))
                {
                    return Guid.NewGuid().ToString();
                }

                return constraint.Contains("int", StringComparison.OrdinalIgnoreCase) || constraint.Contains("long", StringComparison.OrdinalIgnoreCase)
                    ? 1.ToString(CultureInfo.InvariantCulture)
                    : "probe";
            });

    [GeneratedRegex(@"\{\*{0,2}(?<name>\w+)(?<constraint>:[^}?]+)?\??\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ParameterPattern();
}
