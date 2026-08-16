using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Tests for the run/message score endpoints (Phase 31).</summary>
public sealed class RunFeedbackEndpointTests
{
    private const string TenantHeader = "X-AgentPrism-Tenant";

    [Fact]
    public async Task Score_is_written_and_read()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedRunAsync(host);

        using var saved = await host.Client.PostAsJsonAsync(
            FeedbackUri(runId),
            new { kind = "Binary", value = 1, comment = "correct answer" });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(saved);
        body.GetProperty("kind").GetString().ShouldBe("Binary");
        body.GetProperty("value").GetInt32().ShouldBe(1);
        body.GetProperty("comment").GetString().ShouldBe("correct answer");
        body.GetProperty("source").GetString().ShouldBe("human");

        using var listed = await host.Client.GetAsync(FeedbackUri(runId));
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Row_count_does_NOT_grow_when_the_same_author_writes_a_second_time()
    {
        // Requires an authenticated actor: on an unauthenticated request,
        // Author is always null and, by design (open question 4), every call
        // opens a new row -- the "same author" scenario is only testable on
        // an authenticated request.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services));
        var runId = await SeedRunAsync(host);

        using (var first = await host.Client.PostAsJsonAsync(FeedbackUri(runId), new { kind = "Binary", value = 1 }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var second = await host.Client.PostAsJsonAsync(FeedbackUri(runId), new { kind = "Binary", value = 0 }))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var listed = await host.Client.GetAsync(FeedbackUri(runId));
        var body = await AgentPrismTestHost.ReadJsonAsync(listed);

        body.GetArrayLength().ShouldBe(1);
        body[0].GetProperty("value").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("Binary", 2)]
    [InlineData("Binary", -1)]
    [InlineData("Stars", 0)]
    [InlineData("Stars", 6)]
    public async Task Out_of_range_value_returns_400(string kind, int value)
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedRunAsync(host);

        using var response = await host.Client.PostAsJsonAsync(FeedbackUri(runId), new { kind, value });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scoring_a_nonexistent_run_returns_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            FeedbackUri(AgentPrismId.NewId()),
            new { kind = "Binary", value = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_tenants_run_cannot_be_scored_returns_the_SAME_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "tenant-a",
        });

        // "doesn't exist" and "belongs to another tenant" must return the
        // SAME 404; a separate message would leak the entity's existence.
        using var missing = await SendAsTenant(host, HttpMethod.Post, FeedbackUri(AgentPrismId.NewId()), "tenant-b",
            new { kind = "Binary", value = 1 });
        using var wrongTenant = await SendAsTenant(host, HttpMethod.Post, FeedbackUri(runId), "tenant-b",
            new { kind = "Binary", value = 1 });

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wrongTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var missingBody = await AgentPrismTestHost.ReadJsonAsync(missing);
        var wrongTenantBody = await AgentPrismTestHost.ReadJsonAsync(wrongTenant);
        missingBody.GetProperty("title").GetString().ShouldBe(wrongTenantBody.GetProperty("title").GetString());

        using var ownTenant = await SendAsTenant(host, HttpMethod.Post, FeedbackUri(runId), "tenant-a",
            new { kind = "Binary", value = 1 });

        ownTenant.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Score_is_deleted()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedRunAsync(host);

        Guid scoreId;

        using (var saved = await host.Client.PostAsJsonAsync(FeedbackUri(runId), new { kind = "Binary", value = 1 }))
        {
            scoreId = (await AgentPrismTestHost.ReadJsonAsync(saved)).GetProperty("id").GetGuid();
        }

        using (var deleted = await host.Client.DeleteAsync(FeedbackUri(runId, scoreId)))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var listed = await host.Client.GetAsync(FeedbackUri(runId));
        (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_a_nonexistent_score_returns_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedRunAsync(host);

        using var response = await host.Client.DeleteAsync(FeedbackUri(runId, AgentPrismId.NewId()));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> SeedRunAsync(AgentPrismTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        return runId;
    }

    private static Uri FeedbackUri(Guid runId)
        => new($"/agentprism/api/runs/{runId}/feedback", UriKind.Relative);

    private static Uri FeedbackUri(Guid runId, Guid scoreId)
        => new($"/agentprism/api/runs/{runId}/feedback/{scoreId}", UriKind.Relative);

    private static async Task<HttpResponseMessage> SendAsTenant(
        AgentPrismTestHost host,
        HttpMethod method,
        Uri uri,
        string tenant,
        object? body)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(TenantHeader, tenant);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await host.Client.SendAsync(request);
    }
}
