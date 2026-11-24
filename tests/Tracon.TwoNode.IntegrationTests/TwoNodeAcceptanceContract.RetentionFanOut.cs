using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// Behaviour 7: one retention fan-out queues one retention job for each tenant, and the two workers that poll the
/// same queue run each of those jobs once.
/// </summary>
/// <remarks>
/// The fan-out job is leased by one worker, so one schedule time is one fan-out. What this proves is the part that
/// crosses the database: both nodes poll while the fan-out queues, and every tenant still ends with exactly one
/// retention job and one history row. It does not claim that two fan-out jobs queued at the same instant queue one
/// job per tenant: the check for a job that is still open is a read before the write, and a repeated retention
/// sweep finds nothing past its cutoff.
/// </remarks>
public abstract partial class TwoNodeAcceptanceContract
{
    private static readonly string[] FanOutTenants = ["tenant-a", "tenant-b", "tenant-c"];

    [Fact]
    public async Task A_retention_fan_out_queues_one_job_for_each_tenant_and_the_two_workers_run_each_once()
    {
        await using var pair = await TwoNodeCluster.StartAsync(
            NewDatabase(),
            configure: static builder =>
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(100));
                builder.Services.AddSingleton<IRetentionTenantSource>(new FanOutTenantList());
            });

        foreach (var tenant in FanOutTenants)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/tracon/api/retention/run_events", UriKind.Relative))
            {
                Content = JsonContent.Create(new RetentionPolicySaveRequest { MaxAgeDays = 30, Enabled = true }),
            };
            request.Headers.Add(TenantHeader, tenant);

            using var saved = await pair.A.Client.SendAsync(request);

            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        await pair.A.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(
            new JobRequest { TenantId = "default", HandlerKey = JobHandlerKeys.RetentionFanOut, TargetName = RetentionTargets.RunEvents });

        var jobs = pair.B.Services.GetRequiredService<IJobStore>();

        foreach (var tenant in FanOutTenants)
        {
            await WaitUntil.ValueAsync(
                async () => await jobs.QueryAsync(new JobQuery { TenantId = tenant, HandlerKey = JobHandlerKeys.Retention }),
                static queued => queued.Count > 0 && queued.All(static job => job.Status is JobStatus.Completed or JobStatus.Failed),
                $"the retention job of {tenant} to finish");
        }

        var policies = pair.A.Services.GetRequiredService<IRetentionPolicyStore>();

        foreach (var tenant in FanOutTenants)
        {
            var queued = await jobs.QueryAsync(new JobQuery { TenantId = tenant, HandlerKey = JobHandlerKeys.Retention });

            queued.ShouldHaveSingleItem().Status.ShouldBe(JobStatus.Completed);
            queued[0].Attempt.ShouldBe(1);
            (await policies.ListRunsAsync(tenant, RetentionTargets.RunEvents, skip: 0, take: 10)).ShouldHaveSingleItem();
        }

        (await jobs.QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.Retention, Take = 50 }))
            .Count.ShouldBe(FanOutTenants.Length, "no tenant outside the source got a job");
    }

    private sealed class FanOutTenantList : IRetentionTenantSource
    {
        public async IAsyncEnumerable<string> ListTenantsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var tenant in FanOutTenants)
            {
                yield return tenant;

                await Task.Yield();
            }
        }
    }
}
