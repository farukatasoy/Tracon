using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Tests for the quota endpoints and <c>429</c> behavior (Phase 21).</summary>
public sealed class QuotaEndpointTests
{
    private static readonly Uri Quotas = new("/agentprism/api/quotas", UriKind.Relative);
    private static readonly Uri Usage = new("/agentprism/api/quotas/usage", UriKind.Relative);

    [Fact]
    public async Task Quota_is_created_listed_and_deleted()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        Guid id;

        using (var created = await host.Client.PutAsJsonAsync(Quotas, Request(maxRuns: 100)))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await AgentPrismTestHost.ReadJsonAsync(created);
            body.GetProperty("maxRuns").GetInt64().ShouldBe(100);
            body.GetProperty("period").GetString().ShouldBe("Daily");
            id = body.GetProperty("id").GetGuid();
        }

        using (var listed = await host.Client.GetAsync(Quotas))
        {
            listed.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using (var deleted = await host.Client.DeleteAsync(
                   new Uri($"/agentprism/api/quotas/{id}", UriKind.Relative)))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var listed = await host.Client.GetAsync(Quotas))
        {
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public async Task Same_scope_saved_a_second_time_overwrites()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var first = await host.Client.PutAsJsonAsync(Quotas, Request(maxRuns: 10)))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var second = await host.Client.PutAsJsonAsync(Quotas, Request(maxRuns: 20)))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var listed = await host.Client.GetAsync(Quotas);
        var body = await AgentPrismTestHost.ReadJsonAsync(listed);

        body.GetArrayLength().ShouldBe(1);
        body[0].GetProperty("maxRuns").GetInt64().ShouldBe(20);
    }

    [Fact]
    public async Task Unlimited_quota_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Quotas,
            new QuotaSaveRequest { Period = QuotaPeriod.Daily, Enabled = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Negative_limit_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Quotas, Request(maxRuns: -1));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Usage_endpoint_returns_period_reset_times()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(Usage);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("timeZone").GetString().ShouldBe("UTC");
        body.GetProperty("dailyResetsAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        body.GetProperty("monthlyResetsAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Run_returns_429_and_ProblemDetails_when_quota_is_exceeded()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        // A near-zero quota: even the first run must be rejected.
        // We fill the counter by hand to reach the quota — writing the
        // consumption directly instead of making a real run keeps the test
        // fast and independent of the provider.
        using (var created = await host.Client.PutAsJsonAsync(Quotas, Request(maxRuns: 1)))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var enforcer = host.Services.GetRequiredService<QuotaEnforcer>();

        await enforcer.RecordAsync(new QuotaConsumption
        {
            TenantId = "default",
            AgentName = "ozetleyici",
            Runs = 1,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/ozetleyici/run", UriKind.Relative),
            new { message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Retry-After tells the client when to retry.
        response.Headers.RetryAfter.ShouldNotBeNull();

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Quota exceeded");
        problem.GetProperty("quotaMetric").GetString().ShouldBe("Runs");
        problem.GetProperty("quotaPeriod").GetString().ShouldBe("Daily");
        problem.GetProperty("quotaLimit").GetDecimal().ShouldBe(1);
        problem.GetProperty("quotaUsed").GetDecimal().ShouldBe(1);
        problem.GetProperty("quotaResetsAt").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Run_is_not_rejected_when_under_quota()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Quotas, Request(maxRuns: 100)))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/ozetleyici/run", UriKind.Relative),
            new { message = "merhaba" });

        response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task No_run_is_rejected_when_no_rule_exists()
    {
        // There is NO default quota: an upgrading setup must not see an
        // unexpected 429.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/ozetleyici/run", UriKind.Relative),
            new { message = "merhaba" });

        response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    private static QuotaSaveRequest Request(long? maxRuns = null)
        => new()
        {
            AgentName = "ozetleyici",
            Period = QuotaPeriod.Daily,
            MaxRuns = maxRuns,
            Enabled = true,
        };
}
