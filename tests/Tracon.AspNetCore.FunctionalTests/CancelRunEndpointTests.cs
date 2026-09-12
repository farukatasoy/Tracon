using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Tests for the run cancellation endpoint (Phase 32).</summary>
public sealed class CancelRunEndpointTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task Run_registered_in_the_ledger_returns_202_and_the_source_is_canceled()
    {
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var cts = new CancellationTokenSource();
        var registry = host.Services.GetRequiredService<IRunCancellationRegistry>();
        using var registration = registry.Register(runId, runId, "default", cts);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task Attempting_to_cancel_a_nonexistent_run_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsync(CancelUri(TraconId.NewId()), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Run_that_is_running_but_absent_from_the_ledger_returns_409()
    {
        // Either it is executing in a different instance, or the process
        // restarted mid-run: in both cases 'runs' shows Running, but this
        // process's in-memory ledger has no record of it.
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Finished_run_returns_409()
    {
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedRunAsync(host, RunStatus.Completed);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Another_tenants_run_cannot_be_canceled_returns_the_SAME_404()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "tenant-a",
        });

        // "missing" and "belongs to another tenant" must return the SAME 404;
        // the same rationale as in RunFeedbackEndpointTests -- to avoid leaking existence.
        using var missing = await SendAsTenant(host, CancelUri(TraconId.NewId()), "tenant-b");
        using var wrongTenant = await SendAsTenant(host, CancelUri(runId), "tenant-b");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wrongTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var missingBody = await TraconTestHost.ReadJsonAsync(missing);
        var wrongTenantBody = await TraconTestHost.ReadJsonAsync(wrongTenant);
        missingBody.GetProperty("title").GetString().ShouldBe(wrongTenantBody.GetProperty("title").GetString());
    }

    private static async Task<Guid> SeedRunAsync(TraconTestHost host, RunStatus status)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        if (status != RunStatus.Running)
        {
            await runs.CompleteRunAsync(new RunCompletion
            {
                RunId = runId,
                Status = status,
                CompletedAt = DateTimeOffset.UtcNow,
            });
        }

        return runId;
    }

    private static Uri CancelUri(Guid runId) => new($"/tracon/api/runs/{runId}/cancel", UriKind.Relative);

    private static async Task<HttpResponseMessage> SendAsTenant(TraconTestHost host, Uri uri, string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add(TenantHeader, tenant);

        return await host.Client.SendAsync(request);
    }
}
