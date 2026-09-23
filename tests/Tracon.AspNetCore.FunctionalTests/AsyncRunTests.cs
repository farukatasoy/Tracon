using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Tests for <c>Prefer: respond-async</c> support (Phase 46).</summary>
public sealed class AsyncRunTests
{
    private const string PreferHeaderName = "Prefer";

    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Quotas = new("/tracon/api/quotas", UriKind.Relative);

    private static async Task<HttpResponseMessage> PostAsyncAsync(TraconTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add(PreferHeaderName, "respond-async");

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    [Fact]
    public async Task Run_is_queued_and_returns_202_Location_and_PreferenceApplied()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var response = await PostAsyncAsync(host, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.GetValues("Preference-Applied").ShouldContain("respond-async", StringComparer.Ordinal);
        response.Headers.Location.ShouldNotBeNull();

        var body = await TraconTestHost.ReadJsonAsync(response);
        var runId = body.GetProperty("runId").GetGuid();
        body.GetProperty("jobId").GetGuid().ShouldBe(runId);
        response.Headers.Location!.OriginalString.ShouldEndWith($"/api/runs/{runId}");
    }

    [Fact]
    public async Task GET_immediately_after_queuing_returns_Queued_not_404()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var accepted = await PostAsyncAsync(host, new AgentRunRequest { Message = "hello" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        using var run = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{runId}", UriKind.Relative));

        run.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(run)).GetProperty("status").GetString().ShouldBe("Queued");
    }

    [Fact]
    public async Task Job_is_picked_up_by_the_worker_and_becomes_Completed()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostAsyncAsync(host, new AgentRunRequest { Message = "hello" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        var uri = new Uri($"/tracon/api/runs/{runId}", UriKind.Relative);

        var status = await WaitUntil.ValueAsync(
            async () =>
            {
                using var poll = await host.Client.GetAsync(uri);

                return (await TraconTestHost.ReadJsonAsync(poll)).GetProperty("status").GetString();
            },
            static status => !string.Equals(status, "Queued", StringComparison.Ordinal) &&
                             !string.Equals(status, "Running", StringComparison.Ordinal),
            $"run {runId} to leave Queued and Running");

        status.ShouldBe("Completed");
    }

    [Fact]
    public async Task SSE_behavior_is_unchanged_without_the_header()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        response.Headers.Contains("Preference-Applied").ShouldBeFalse();
    }

    [Fact]
    public async Task Request_carrying_the_header_returns_501_when_disabled()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.Configure<TraconAsyncRunOptions>(
                static options => options.Enabled = false));

        using var response = await PostAsyncAsync(host, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Queuing_returns_429_when_the_quota_is_full()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using (var created = await host.Client.PutAsJsonAsync(
                   Quotas,
                   new QuotaSaveRequest { AgentName = "kod-agent", Period = QuotaPeriod.Daily, MaxRuns = 1, Enabled = true }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // In an empty period there is NO usage record at all (Allowed is returned);
        // for the quota to actually kick in, ONE run must first be consumed —
        // the next request (including queuing) then gets 429.
        using (var first = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await PostAsyncAsync(host, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Empty_message_returns_400()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var response = await PostAsyncAsync(host, new AgentRunRequest { Message = "  " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approval_decision_is_not_supported_returns_400()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var response = await PostAsyncAsync(
            host,
            new AgentRunRequest
            {
                SessionId = "session-1",
                Approvals = [new ToolApprovalDecision { RequestId = "request-1", Approved = true }],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invalid_lane_is_rejected_with_400()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var response = await PostAsyncAsync(host, new AgentRunRequest { Message = "hello", Lane = "Media" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Queued_run_carries_the_requested_lane()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var accepted = await PostAsyncAsync(host, new AgentRunRequest { Message = "hello", Lane = "media" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        using var job = await host.Client.GetAsync(new Uri($"/tracon/api/jobs/{runId}", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(job)).GetProperty("job").GetProperty("lane").GetString()
            .ShouldBe("media");
    }

    [Fact]
    public async Task Queued_run_without_a_lane_defaults_to_default()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var accepted = await PostAsyncAsync(host, new AgentRunRequest { Message = "hello" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        using var job = await host.Client.GetAsync(new Uri($"/tracon/api/jobs/{runId}", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(job)).GetProperty("job").GetProperty("lane").GetString()
            .ShouldBe("default");
    }

    [Fact]
    public async Task Queued_run_can_be_canceled()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var accepted = await PostAsyncAsync(host, new AgentRunRequest { Message = "hello" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        using var cancelled = await host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{runId}/cancel", UriKind.Relative), content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var run = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{runId}", UriKind.Relative));
        (await TraconTestHost.ReadJsonAsync(run)).GetProperty("status").GetString().ShouldBe("Canceled");

        using var secondCancel = await host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{runId}/cancel", UriKind.Relative), content: null);
        secondCancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
