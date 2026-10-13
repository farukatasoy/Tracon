using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Failure mode 6: another tenant's run cannot be queued for a resume or a response,
/// and the continuation filter does not cross the tenant boundary.
/// </summary>
/// <remarks>
/// The worker opens the job's tenant, not the caller's; a job queued for another
/// tenant's run would run in the wrong place. Both directions are checked: the
/// owner sees its own data, the other tenant sees nothing and learns nothing.
/// </remarks>
public sealed class WorkflowAsyncTenantTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    [Fact]
    public async Task Another_tenants_run_cannot_be_resumed_or_answered_through_the_queue()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartTenantAsync(probe);
        await using var lifetime = probe;
        await SaveForAsync(host, TenantA);

        using var first = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" }, tenant: TenantA);
        var sourceId = await AcceptedRunIdAsync(first);
        await host.WaitForRunStatusAsync(sourceId, nameof(RunStatus.Completed), TenantA);

        using var waiting = await PostAsync(host, $"/api/workflows/{Approval}/run", new WorkflowRunHttpRequest { Message = "the draft" }, tenant: TenantA);
        var waitingId = await AcceptedRunIdAsync(waiting);
        await host.WaitForRunStatusAsync(waitingId, nameof(RunStatus.AwaitingInput), TenantA);

        using (var resume = await PostAsync(host, $"/api/workflows/runs/{sourceId}/resume", new WorkflowResumeHttpRequest(), tenant: TenantB))
        {
            resume.StatusCode.ShouldBe(HttpStatusCode.NotFound, await resume.Content.ReadAsStringAsync());
        }

        using (var respond = await PostAsync(host, $"/api/workflows/runs/{waitingId}/respond", new { requestId = "gate", approved = true }, tenant: TenantB))
        {
            respond.StatusCode.ShouldBe(HttpStatusCode.NotFound, await respond.Content.ReadAsStringAsync());
        }

        var jobs = host.Services.GetRequiredService<IJobStore>();
        (await jobs.QueryAsync(new JobQuery { TenantId = TenantB })).ShouldBeEmpty();

        // The owner can still resume its own run.
        using var own = await PostAsync(host, $"/api/workflows/runs/{sourceId}/resume", new WorkflowResumeHttpRequest(), tenant: TenantA);
        own.StatusCode.ShouldBe(HttpStatusCode.Accepted, await own.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_continuation_filter_does_not_return_another_tenants_runs()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartTenantAsync(probe);
        await using var lifetime = probe;

        var runs = host.Services.GetRequiredService<IRunStore>();
        var sourceId = TraconId.NewId();
        var continuationId = TraconId.NewId();

        await runs.StartRunAsync(Row(sourceId, TenantA, continuedFrom: null));
        await runs.StartRunAsync(Row(continuationId, TenantA, continuedFrom: sourceId));

        (await ListContinuationsAsync(host, sourceId, TenantA)).ShouldBe([continuationId]);
        (await ListContinuationsAsync(host, sourceId, TenantB)).ShouldBeEmpty();
    }

    private static RunStartInfo Row(Guid runId, string tenant, Guid? continuedFrom) => new()
    {
        RunId = runId,
        AgentName = Recoverable,
        Kind = RunKind.Workflow,
        WorkflowName = Recoverable,
        StartedAt = DateTimeOffset.UtcNow,
        TenantId = tenant,
        SessionId = "production-7",
        ContinuedFromRunId = continuedFrom,
    };

    private static async Task<IReadOnlyList<Guid>> ListContinuationsAsync(TraconTestHost host, Guid runId, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/tracon/api/runs?continuedFromRunId={runId}");
        request.Headers.Add("X-Tracon-Tenant", tenant);

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return [.. (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().Select(static run => run.GetProperty("id").GetGuid())];
    }

    private static Task<TraconTestHost> StartTenantAsync(RecoveryProbe probe)
        => StartAsync(
            probe,
            configureTracon: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

    private static async Task SaveForAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/tracon/api/workflows/{Recoverable}")
        {
            Content = JsonContent.Create(RecoverableDefinition()),
        };

        request.Headers.Add("X-Tracon-Tenant", tenant);

        using var saved = await host.Client.SendAsync(request);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }
}
