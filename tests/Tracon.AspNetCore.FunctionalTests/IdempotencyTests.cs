using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary><c>Idempotency-Key</c> support tests (Phase 43).</summary>
public sealed class IdempotencyTests
{
    private const string HeaderName = "Idempotency-Key";

    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);
    private static readonly Uri Retention = new("/tracon/api/retention/idempotency_keys", UriKind.Relative);
    private static readonly Uri Quotas = new("/tracon/api/quotas", UriKind.Relative);

    private static async Task<HttpResponseMessage> PostWithKeyAsync(TraconTestHost host, Uri uri, object body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add(HeaderName, key);

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> PostAsyncWithKeyAsync(TraconTestHost host, Uri uri, object body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add(HeaderName, key);
        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    [Fact]
    public async Task Same_key_same_body_does_not_run_the_agent_a_second_time()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var key = Guid.NewGuid().ToString("N");
        var body = new AgentRunRequest { Message = "hello" };

        string firstBody;
        using (var first = await PostWithKeyAsync(host, Run, body, key))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            first.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
            first.Headers.Contains("Idempotency-Replayed").ShouldBeFalse();
            firstBody = await first.Content.ReadAsStringAsync();
        }

        using (var second = await PostWithKeyAsync(host, Run, body, key))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
            second.Headers.GetValues("Idempotency-Replayed").ShouldContain("true", StringComparer.Ordinal);

            // The replay is the SAME answer, not merely another successful one.
            // Re-running the agent and returning a fresh (equally valid) body
            // would keep the header, the status and the call count honest-looking
            // while breaking the only promise idempotency makes.
            (await second.Content.ReadAsStringAsync()).ShouldBe(firstBody);
        }

        var provider = host.Services.GetServices<IModelProvider>().OfType<FakeModelProvider>().Single();
        provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Prefer_respond_async_replay_also_returns_Location_and_Preference_Applied_headers()
    {
        // HATA-S3-008 / MT-JOB-083: replay only preserved the body — the
        // 'Location' and 'Preference-Applied' HTTP headers were not stored.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        var key = Guid.NewGuid().ToString("N");
        var body = new AgentRunRequest { Message = "hello" };

        using var first = await PostAsyncWithKeyAsync(host, Run, body, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        first.Headers.GetValues("Preference-Applied").ShouldContain("respond-async", StringComparer.Ordinal);
        first.Headers.Location.ShouldNotBeNull();

        using var second = await PostAsyncWithKeyAsync(host, Run, body, key);
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.Headers.GetValues("Idempotency-Replayed").ShouldContain("true", StringComparer.Ordinal);
        second.Headers.GetValues("Preference-Applied").ShouldContain("respond-async", StringComparer.Ordinal);
        second.Headers.Location.ShouldNotBeNull();
        second.Headers.Location!.OriginalString.ShouldBe(first.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Same_key_DIFFERENT_body_returns_422()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var key = Guid.NewGuid().ToString("N");

        using (var first = await PostWithKeyAsync(host, Run, new AgentRunRequest { Message = "hello" }, key))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var mismatched = await PostWithKeyAsync(host, Run, new AgentRunRequest { Message = "OTHER" }, key);

        mismatched.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The status code alone does not tell a 422 from a key reuse apart from
        // a 422 from anything else. The title is the part an operator reads,
        // and it is what the manual case compares against.
        (await TraconTestHost.ReadJsonAsync(mismatched)).GetProperty("title").GetString()
            .ShouldBe("Idempotency-Key used for a different request");
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_key_run_the_agent_ONLY_ONCE()
    {
        // 🚨 The test server (TestServer) may process concurrent requests
        // SEQUENTIALLY, unlike a real network; in that case the second
        // request gets a REPLAYED 200 instead of a 409 (the record is
        // already Completed). So instead of asserting "who got 409", the
        // ONE invariant that shows the separation is ATOMIC is asserted: the
        // agent runs ONLY ONCE no matter how many requests arrive. The real
        // race test for the separation (8 concurrent calls, no network
        // layer) lives in `IdempotencyStoreContract` and runs against all
        // three SQL providers.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var key = Guid.NewGuid().ToString("N");
        var body = new AgentRunRequest { Message = "hello" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostWithKeyAsync(host, Run, body, key)));

        try
        {
            responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
            responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThanOrEqualTo(1);

            var provider = host.Services.GetServices<IModelProvider>().OfType<FakeModelProvider>().Single();
            provider.Requests.Count.ShouldBe(1);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Retrying_with_the_same_key_after_a_failed_request_works()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var key = Guid.NewGuid().ToString("N");

        // An empty message is rejected with 400 inside RunAsync;
        // IdempotencyFilter must NOT STORE this failure and must DELETE the
        // record.
        using (var failed = await PostWithKeyAsync(host, Run, new AgentRunRequest { Message = "  " }, key))
        {
            failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var retried = await PostWithKeyAsync(host, Run, new AgentRunRequest { Message = "  " }, key);

        // 🚨 NOT 409 (InProgress) or a stored-stale-error: the key must have
        // been released, the same result (400) is produced AGAIN.
        retried.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Streaming_request_with_Idempotency_Key_returns_400()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostWithKeyAsync(
            host,
            Responses,
            new { model = "kod-agent", input = "hello", stream = true },
            Guid.NewGuid().ToString("N"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("title").GetString()
            .ShouldBe("Idempotency-Key not supported on streaming requests");
    }

    [Fact]
    public async Task Repeated_request_does_not_consume_the_quota_a_second_time()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using (var created = await host.Client.PutAsJsonAsync(
                   Quotas,
                   new QuotaSaveRequest { AgentName = "kod-agent", Period = QuotaPeriod.Daily, MaxRuns = 1, Enabled = true }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var key = Guid.NewGuid().ToString("N");
        var body = new AgentRunRequest { Message = "hello" };

        using (var first = await PostWithKeyAsync(host, Run, body, key))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Quota exhausted (maxRuns=1); a NEW request would get 429. But the
        // SAME key+body must return the stored response — it never reaches
        // the QuotaGate.
        using var replayed = await PostWithKeyAsync(host, Run, body, key);

        replayed.StatusCode.ShouldBe(HttpStatusCode.OK);
        replayed.Headers.GetValues("Idempotency-Replayed").ShouldContain("true", StringComparer.Ordinal);
    }

    [Fact]
    public async Task Is_subject_to_the_rate_limit()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.Configure<TraconRateLimitOptions>(
                static options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 1;
                    options.Window = TimeSpan.FromMinutes(5);
                }));

        var key = Guid.NewGuid().ToString("N");
        var body = new AgentRunRequest { Message = "hello" };

        using (var first = await PostWithKeyAsync(host, Run, body, key))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // The rate limit runs BEFORE IdempotencyFilter (43.1): even a repeat
        // with the same key gets 429 once the window is exhausted.
        using var limited = await PostWithKeyAsync(host, Run, body, key);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Request_carrying_the_header_returns_501_while_disabled()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.Configure<TraconIdempotencyOptions>(
                static options => options.Enabled = false));

        using var response = await PostWithKeyAsync(
            host, Run, new AgentRunRequest { Message = "hello" }, Guid.NewGuid().ToString("N"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Behavior_is_unchanged_when_the_header_is_absent()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
    }

    [Fact]
    public async Task Too_long_key_returns_400()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostWithKeyAsync(
            host, Run, new AgentRunRequest { Message = "hello" }, new string('a', 300));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("title").GetString().ShouldBe("Idempotency-Key too long");

        // Both numbers: the limit AND what was received. A detail carrying only
        // one of them leaves the caller guessing which end to change.
        var detail = json.GetProperty("detail").GetString() ?? string.Empty;
        detail.ShouldContain("255", Case.Sensitive);
        detail.ShouldContain("300", Case.Sensitive);
    }

    [Fact]
    public async Task Idempotency_keys_is_recognized_as_a_retention_target()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Retention,
            new RetentionPolicySaveRequest { MaxAgeDays = 1, Enabled = true });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
