using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>In-memory behavior tests for the scheduling and job queue endpoints (Phase 17).</summary>
public sealed class SchedulingEndpointTests
{
    private static readonly Uri Schedules = new("/agentprism/api/schedules", UriKind.Relative);
    private static readonly Uri Jobs = new("/agentprism/api/jobs", UriKind.Relative);

    [Fact]
    public async Task Schedule_is_created_updated_and_deleted()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(
                   new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
                   Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await AgentPrismTestHost.ReadJsonAsync(created);
            body.GetProperty("targetName").GetString().ShouldBe("ozetleyici");
            body.GetProperty("nextRunAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        }

        using (var updated = await host.Client.PutAsJsonAsync(
                   new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
                   Request() with { TargetName = "new-target" }))
        {
            updated.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await AgentPrismTestHost.ReadJsonAsync(updated)).GetProperty("targetName").GetString()
                .ShouldBe("new-target");
        }

        using (var listed = await host.Client.GetAsync(Schedules))
        {
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using var deleted = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var getAfterDelete = await host.Client.GetAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative));
        getAfterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_cron_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request() with { Cron = "* * * *" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invalid_time_zone_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request() with { TimeZone = "Not/A_Zone" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Save_is_rejected_when_item_count_exceeds_the_limit()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(o => o.MaxItemsPerJob = 1));

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request() with { Payload = JsonDocument.Parse("""["a","b"]""").RootElement });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invalid_lane_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request() with { Lane = "Media" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Trigger_inherits_the_schedules_lane()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request() with { Lane = "media" });

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu/trigger", UriKind.Relative),
            new { });

        (await AgentPrismTestHost.ReadJsonAsync(triggered)).GetProperty("lane").GetString().ShouldBe("media");
    }

    [Fact]
    public async Task Jobs_are_filtered_by_lane()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(new Uri("/agentprism/api/schedules/a", UriKind.Relative), Request());
        await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/b", UriKind.Relative),
            Request() with { Lane = "media" });

        await host.Client.PostAsJsonAsync(new Uri("/agentprism/api/schedules/a/trigger", UriKind.Relative), new { });
        await host.Client.PostAsJsonAsync(new Uri("/agentprism/api/schedules/b/trigger", UriKind.Relative), new { });

        using var filtered = await host.Client.GetAsync(new Uri("/agentprism/api/jobs?lane=media", UriKind.Relative));

        var jobs = await AgentPrismTestHost.ReadJsonAsync(filtered);
        jobs.GetArrayLength().ShouldBe(1);
        jobs[0].GetProperty("lane").GetString().ShouldBe("media");
    }

    [Fact]
    public async Task Nonexistent_schedule_returns_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/schedules/yok", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trigger_creates_a_job_and_generates_its_items()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative), Request());

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu/trigger", UriKind.Relative),
            new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var job = await AgentPrismTestHost.ReadJsonAsync(triggered);
        var jobId = job.GetProperty("id").GetGuid();
        job.GetProperty("status").GetString().ShouldBe("Pending");
        job.GetProperty("totalItems").GetInt32().ShouldBe(2);

        using var detail = await host.Client.GetAsync(new Uri($"/agentprism/api/jobs/{jobId}", UriKind.Relative));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(detail);
        body.GetProperty("job").GetProperty("id").GetGuid().ShouldBe(jobId);
        body.GetProperty("items").GetArrayLength().ShouldBe(2);

        using var listed = await host.Client.GetAsync(Jobs);
        (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Job_is_canceled_and_a_second_cancel_returns_409()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative), Request());

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu/trigger", UriKind.Relative),
            new { });
        var jobId = (await AgentPrismTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        using var cancelled = await host.Client.PostAsync(
            new Uri($"/agentprism/api/jobs/{jobId}/cancel", UriKind.Relative),
            content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var secondCancel = await host.Client.PostAsync(
            new Uri($"/agentprism/api/jobs/{jobId}/cancel", UriKind.Relative),
            content: null);
        secondCancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Nonexistent_job_returns_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri($"/agentprism/api/jobs/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Operator_can_trigger_but_cannot_save_a_schedule()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        // The schedule is written directly to the store, bypassing HTTP
        // authorization: the Admin endpoint is closed in this test, so the
        // schedule is seeded through another path.
        await host.Services.GetRequiredService<IJobScheduleStore>().SaveAsync(
            new JobSchedule
            {
                TenantId = "default",
                Name = "gece-raporu",
                Kind = JobKind.AgentBatch,
                TargetName = "ozetleyici",
                Payload = JsonDocument.Parse("""["a"]""").RootElement,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        using var trigger = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu/trigger", UriKind.Relative),
            new { });
        trigger.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var save = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request());
        save.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static JobScheduleSaveRequest Request()
        => new()
        {
            Kind = JobKind.AgentBatch,
            TargetName = "ozetleyici",
            Cron = "0 3 * * *",
            TimeZone = "UTC",
            Payload = JsonDocument.Parse("""["a","b"]""").RootElement,
            Enabled = true,
        };
}
