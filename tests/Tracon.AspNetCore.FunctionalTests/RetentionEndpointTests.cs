using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Tests for the data retention endpoints (Phase 25).</summary>
public sealed class RetentionEndpointTests
{
    private static readonly Uri Policies = new("/tracon/api/retention", UriKind.Relative);
    private static readonly Uri RunEventsPolicy = new("/tracon/api/retention/run_events", UriKind.Relative);
    private static readonly Uri Preview = new("/tracon/api/retention/preview", UriKind.Relative);
    private static readonly Uri Run = new("/tracon/api/retention/run", UriKind.Relative);
    private static readonly Uri History = new("/tracon/api/retention/history", UriKind.Relative);

    [Fact]
    public async Task Policy_is_created_listed_and_deleted()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(RunEventsPolicy, Request(maxAgeDays: 30)))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await TraconTestHost.ReadJsonAsync(created);
            body.GetProperty("target").GetString().ShouldBe("run_events");
            body.GetProperty("maxAgeDays").GetInt32().ShouldBe(30);
        }

        using (var listed = await host.Client.GetAsync(Policies))
        {
            listed.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using (var deleted = await host.Client.DeleteAsync(RunEventsPolicy))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var afterDelete = await host.Client.GetAsync(RunEventsPolicy);
        afterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Same_target_saved_a_second_time_overwrites()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(RunEventsPolicy, Request(maxAgeDays: 30));
        await host.Client.PutAsJsonAsync(RunEventsPolicy, Request(maxAgeDays: 7));

        using var listed = await host.Client.GetAsync(Policies);
        var body = await TraconTestHost.ReadJsonAsync(listed);

        body.GetArrayLength().ShouldBe(1);
        body[0].GetProperty("maxAgeDays").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task Unknown_target_returns_400()
    {
        await using var host = await TraconTestHost.StartAsync();

        var unknown = new Uri("/tracon/api/retention/not_a_real_table", UriKind.Relative);

        using var get = await host.Client.GetAsync(unknown);
        get.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var put = await host.Client.PutAsJsonAsync(unknown, Request(maxAgeDays: 30));
        put.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var delete = await host.Client.DeleteAsync(unknown);
        delete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var preview = await host.Client.GetAsync(
            new Uri("/tracon/api/retention/preview?target=not_a_real_table", UriKind.Relative));
        preview.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MaxAgeDays_below_zero_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(RunEventsPolicy, Request(maxAgeDays: 0));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// 🚨 The gap Phase 36 closed: a policy with MaxAgeDays EMPTY and only
    /// MaxRows filled used to show as "disabled" in the preview.
    /// </summary>
    [Fact]
    public async Task Policy_with_only_maxRows_filled_shows_as_enabled_in_preview()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(
            RunEventsPolicy,
            new RetentionPolicySaveRequest { MaxRows = 100, Enabled = true }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await TraconTestHost.ReadJsonAsync(created);
            body.GetProperty("maxRows").GetInt64().ShouldBe(100);
        }

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/retention/preview?target=run_events", UriKind.Relative));

        var preview = await TraconTestHost.ReadJsonAsync(response);
        preview[0].GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task MaxRows_below_zero_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            RunEventsPolicy,
            new RetentionPolicySaveRequest { MaxRows = 0, Enabled = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preview_shows_disabled_when_no_policy_exists()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/retention/preview?target=run_events", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body[0].GetProperty("target").GetString().ShouldBe("run_events");
        body[0].GetProperty("enabled").GetBoolean().ShouldBeFalse();
        body[0].GetProperty("matchingRows").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Preview_without_arguments_returns_all_targets()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(Preview);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetArrayLength().ShouldBe(RetentionTargets.All.Count);
    }

    [Fact]
    public async Task Run_now_enqueues_a_job()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsync(
            new Uri("/tracon/api/retention/run?target=run_events", UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("target").GetString().ShouldBe("run_events");
        var jobId = body.GetProperty("jobId").GetGuid();

        var jobStore = host.Services.GetRequiredService<IJobStore>();
        var job = await jobStore.GetAsync("default", jobId);

        job.ShouldNotBeNull();
        job.HandlerKey.ShouldBe(JobHandlerKeys.Retention);
        job.TargetName.ShouldBe("run_events");
    }

    [Fact]
    public async Task Empty_history_returns_empty_list()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(History);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(0);
    }

    private static RetentionPolicySaveRequest Request(int? maxAgeDays)
        => new() { MaxAgeDays = maxAgeDays, Enabled = true };
}
