using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The retention fan-out through the real host: one schedule in the default tenant queues a retention job for every
/// tenant, each job reads its own tenant's policy, and no other tenant can schedule the key.
/// </summary>
/// <remarks>
/// The data plane of an in-memory host holds no rows, so what proves a tenant was swept is its own run history: a run
/// is written for every target whose policy applies to the tenant the job was queued for. The age each tenant sees is
/// read from the preview, which resolves the same policy.
/// </remarks>
public sealed class RetentionFanOutTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    private static readonly Uri HandlerKeys = new("/tracon/api/schedules/handler-keys", UriKind.Relative);
    private static readonly Uri Schedule = new("/tracon/api/schedules/retention-fanout", UriKind.Relative);

    [Fact]
    public async Task One_schedule_in_the_default_tenant_sweeps_every_tenant_by_that_tenants_own_policy()
    {
        await using var host = await StartAsync(tenants: ["tenant-a", "tenant-b", "tenant-c"]);

        await SavePolicyAsync(host, "tenant-a", maxAgeDays: 365, enabled: true);
        await SavePolicyAsync(host, "tenant-b", maxAgeDays: 7, enabled: false);
        await SaveDefaultPolicyAsync(host, maxAgeDays: 30);

        var fanOut = await TriggerFanOutAsync(host);

        await WaitForJobAsync(host, "default", fanOut);

        foreach (var tenant in new[] { "tenant-a", "tenant-b", "tenant-c" })
        {
            await WaitForRetentionJobAsync(host, tenant);
        }

        (await HistoryAsync(host, "tenant-a")).Count.ShouldBe(1, "its own policy is on");
        (await HistoryAsync(host, "tenant-b")).Count.ShouldBe(0, "its own policy is off, and an own policy beats the default");
        (await HistoryAsync(host, "tenant-c")).Count.ShouldBe(1, "it has no policy, so the default applies");

        (await PreviewAsync(host, "tenant-a")).MaxAgeDays.ShouldBe(365);
        (await PreviewAsync(host, "tenant-b")).Enabled.ShouldBeFalse();
        (await PreviewAsync(host, "tenant-c")).MaxAgeDays.ShouldBe(30);
    }

    [Fact]
    public async Task A_second_fan_out_while_the_jobs_are_still_waiting_queues_nothing_new()
    {
        // No worker: the retention jobs stay queued, so the second fan-out meets them waiting.
        await using var host = await StartAsync(tenants: ["tenant-a", "tenant-b"], runWorker: false);
        var store = host.Services.GetRequiredService<IJobStore>();

        await RunFanOutHandlerAsync(host);
        await RunFanOutHandlerAsync(host);

        (await store.QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.Retention, Take = 100 }, TestContext.Current.CancellationToken))
            .Select(static job => job.TenantId).Order(StringComparer.Ordinal).ToArray().ShouldBe(["tenant-a", "tenant-b"]);
    }

    [Fact]
    public async Task The_default_list_of_keys_leaves_the_fan_out_out_and_the_save_endpoint_says_how_to_allow_it()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var keys = await host.Client.GetAsync(HandlerKeys);
        (await TraconTestHost.ReadJsonAsync(keys)).EnumerateArray().Select(static key => key.GetString())
            .ShouldNotContain(static key => string.Equals(key, JobHandlerKeys.RetentionFanOut, StringComparison.Ordinal));

        using var saved = await host.Client.PutAsJsonAsync(Schedule, FanOutRequest());

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain(nameof(TraconSchedulingOptions.HttpSchedulableHandlerKeys));
    }

    [Fact]
    public async Task Once_the_host_allows_the_key_the_default_tenant_can_schedule_it_and_sees_it_listed()
    {
        await using var host = await StartAsync(tenants: ["tenant-a"], runWorker: false);

        using var saved = await host.Client.PutAsJsonAsync(Schedule, FanOutRequest());

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var keys = await host.Client.GetAsync(HandlerKeys);
        (await TraconTestHost.ReadJsonAsync(keys)).EnumerateArray().Select(static key => key.GetString())
            .ShouldContain(static key => string.Equals(key, JobHandlerKeys.RetentionFanOut, StringComparison.Ordinal));
    }

    /// <summary>
    /// 🚨 The key queues work for every tenant. A tenant that is not the default one is refused when it saves the
    /// schedule, even though the host allowed the key, and is not shown the key as one it may use.
    /// </summary>
    [Fact]
    public async Task Another_tenant_cannot_schedule_the_key_even_when_the_host_allows_it_and_is_not_shown_it()
    {
        await using var host = await StartAsync(tenants: ["tenant-a"], runWorker: false);

        using var saved = await SendAsync(host, HttpMethod.Put, Schedule, "tenant-a", JsonContent.Create(FanOutRequest()));

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("default tenant");

        using var read = await SendAsync(host, HttpMethod.Get, Schedule, "tenant-a");
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var keys = await SendAsync(host, HttpMethod.Get, HandlerKeys, "tenant-a");
        (await TraconTestHost.ReadJsonAsync(keys)).EnumerateArray().Select(static key => key.GetString())
            .ShouldNotContain(static key => string.Equals(key, JobHandlerKeys.RetentionFanOut, StringComparison.Ordinal));
    }

    /// <summary>
    /// The same refusal when the schedule is written from code and never meets the HTTP check: the job fails for good
    /// when it runs and queues nothing for any tenant.
    /// </summary>
    [Fact]
    public async Task A_fan_out_job_of_another_tenant_fails_without_queueing_anything()
    {
        await using var host = await StartAsync(tenants: ["tenant-a", "tenant-b"]);
        var dispatcher = host.Services.GetRequiredService<IJobDispatcher>();
        var store = host.Services.GetRequiredService<IJobStore>();

        var job = await dispatcher.EnqueueAsync(
            new JobRequest { TenantId = "tenant-a", HandlerKey = JobHandlerKeys.RetentionFanOut, TargetName = "*" },
            TestContext.Current.CancellationToken);

        var finished = await WaitForJobAsync(host, "tenant-a", job.Id);

        finished.Status.ShouldBe(JobStatus.Failed);
        finished.ErrorMessage.ShouldNotBeNull().ShouldStartWith(TraconErrorTypes.JobHostOnlyHandlerRefused);
        finished.Attempt.ShouldBe(1, "the refusal is final, so the job is not tried again");
        (await store.QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.Retention }, TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task The_preview_service_and_the_preview_endpoint_report_the_same_rows_for_the_same_tenant()
    {
        await using var host = await TraconTestHost.StartAsync();
        await SavePolicyAsync(host, tenant: null, maxAgeDays: 14, enabled: true);

        var fromService = await host.Services.GetRequiredService<IRetentionPreviewService>()
            .PreviewAsync(RetentionTargets.RunEvents, TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/retention/preview?target=run_events", UriKind.Relative));
        var fromEndpoint = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().Single();

        var preview = fromService.ShouldHaveSingleItem();
        fromEndpoint.GetProperty("target").GetString().ShouldBe(preview.Target);
        fromEndpoint.GetProperty("maxAgeDays").GetInt32().ShouldBe(preview.MaxAgeDays!.Value);
        fromEndpoint.GetProperty("enabled").GetBoolean().ShouldBe(preview.Enabled);
        fromEndpoint.GetProperty("matchingRows").GetInt64().ShouldBe(preview.MatchingRows);
    }

    private static Task<TraconTestHost> StartAsync(string[] tenants, bool runWorker = true)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.UseScheduling(options =>
                {
                    options.RunWorker = runWorker;
                    options.PollInterval = TimeSpan.FromMilliseconds(20);
                    options.HttpSchedulableHandlerKeys.Add(JobHandlerKeys.RetentionFanOut);
                });
            },
            configureServices: services => services.AddSingleton<IRetentionTenantSource>(new FixedTenantSource(tenants)));

    private static JobScheduleSaveRequest FanOutRequest() => new()
    {
        HandlerKey = JobHandlerKeys.RetentionFanOut,
        TargetName = "*",
        TimeZone = "UTC",
        Payload = JsonDocument.Parse("[]").RootElement,
        Enabled = true,
    };

    private static async Task<Guid> TriggerFanOutAsync(TraconTestHost host)
    {
        using (var saved = await host.Client.PutAsJsonAsync(Schedule, FanOutRequest()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        using var triggered = await host.Client.PostAsJsonAsync(new Uri("/tracon/api/schedules/retention-fanout/trigger", UriKind.Relative), new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();
    }

    private static async Task<JobRecord> WaitForJobAsync(TraconTestHost host, string tenant, Guid jobId)
    {
        var store = host.Services.GetRequiredService<IJobStore>();

        return await WaitUntil.ValueAsync(
            async () => (await store.GetAsync(tenant, jobId, TestContext.Current.CancellationToken))!,
            static job => job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            "the job to finish");
    }

    private static async Task WaitForRetentionJobAsync(TraconTestHost host, string tenant)
    {
        var store = host.Services.GetRequiredService<IJobStore>();

        await WaitUntil.ValueAsync(
            async () => await store.QueryAsync(
                new JobQuery { TenantId = tenant, HandlerKey = JobHandlerKeys.Retention }, TestContext.Current.CancellationToken),
            static jobs => jobs.Count == 1 && jobs[0].Status is JobStatus.Completed or JobStatus.Failed,
            $"the retention job of {tenant} to finish");
    }

    private static async Task RunFanOutHandlerAsync(TraconTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<RetentionFanOutJobHandler>();
        var jobId = Guid.NewGuid();

        await handler.ExecuteAsync(
            new JobContext
            {
                Job = new JobRecord
                {
                    Id = jobId,
                    TenantId = "default",
                    HandlerKey = JobHandlerKeys.RetentionFanOut,
                    TargetName = "*",
                    Status = JobStatus.Running,
                    ScheduledFor = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                Items = [],
                ReportItemAsync = static (_, _) => default,
                IsCancelledAsync = static _ => new ValueTask<bool>(false),
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task SavePolicyAsync(TraconTestHost host, string? tenant, int maxAgeDays, bool enabled)
    {
        using var response = await SendAsync(
            host,
            HttpMethod.Put,
            new Uri("/tracon/api/retention/run_events", UriKind.Relative),
            tenant,
            JsonContent.Create(new RetentionPolicySaveRequest { MaxAgeDays = maxAgeDays, Enabled = enabled }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The default of every tenant: <c>"*"</c> is not a tenant over HTTP, so it goes straight to the store.</summary>
    private static async Task SaveDefaultPolicyAsync(TraconTestHost host, int maxAgeDays)
    {
        var now = DateTimeOffset.UtcNow;

        await host.Services.GetRequiredService<IRetentionPolicyStore>().SavePolicyAsync(
            new RetentionPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = "*",
                Target = RetentionTargets.RunEvents,
                MaxAgeDays = maxAgeDays,
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<JsonElement>> HistoryAsync(TraconTestHost host, string tenant)
    {
        using var response = await SendAsync(host, HttpMethod.Get, new Uri("/tracon/api/retention/history", UriKind.Relative), tenant);

        return [.. (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().Select(static entry => entry.Clone())];
    }

    private static async Task<RetentionPreview> PreviewAsync(TraconTestHost host, string tenant)
    {
        using var response = await SendAsync(host, HttpMethod.Get, new Uri("/tracon/api/retention/preview?target=run_events", UriKind.Relative), tenant);
        var entry = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().Single();

        return new RetentionPreview
        {
            Target = entry.GetProperty("target").GetString()!,
            Enabled = entry.GetProperty("enabled").GetBoolean(),
            MaxAgeDays = entry.TryGetProperty("maxAgeDays", out var days) && days.ValueKind == JsonValueKind.Number ? days.GetInt32() : null,
        };
    }

    private static async Task<HttpResponseMessage> SendAsync(
        TraconTestHost host,
        HttpMethod method,
        Uri uri,
        string? tenant,
        HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class FixedTenantSource(IReadOnlyList<string> tenants) : IRetentionTenantSource
    {
        public async IAsyncEnumerable<string> ListTenantsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var tenant in tenants)
            {
                yield return tenant;

                await Task.Yield();
            }
        }
    }
}
