using System.Data.Common;
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
    public async Task Run_running_elsewhere_is_recorded_for_its_owner_when_reconciliation_is_on()
    {
        // F-764: neither a queued job nor this process holds the run, so the request goes onto the row.
        // The owner (another host, covered by CrossInstanceRunCancelTests) reads it on its heartbeat.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Configure<RunReconciliationOptions>(static options => options.Enabled = true));
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var stored = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId);

        stored.ShouldNotBeNull().Status.ShouldBe(RunStatus.Running);
        stored.CancelRequestedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Run_running_elsewhere_returns_409_with_a_hint_when_reconciliation_is_off()
    {
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()
            .ShouldNotBeNull().ShouldContain("Tracon:RunReconciliation", Case.Sensitive);
        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull().CancelRequestedAt.ShouldBeNull();
    }

    // --- Failure paths and the audit trail (Phase 262 audit) ---

    [Fact]
    public async Task Run_registered_in_the_ledger_is_canceled_even_when_the_job_store_cannot_answer()
    {
        // The job is cancelled first (F-765). A job store that is down must not stop this process
        // from stopping a run it executes itself: the old endpoint never touched the job store for it.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.Services.ThrowFrom<IJobStore>(
                nameof(IJobStore.CancelAsync), static () => new FakeStoreException("the job store is down")));
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var cts = new CancellationTokenSource();
        using var registration = host.Services.GetRequiredService<IRunCancellationRegistry>().Register(runId, runId, "default", cts);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task Queued_run_nothing_here_holds_answers_503_when_the_job_store_cannot_answer()
    {
        // The job may be the only thing that holds this run. Saying "409, not executing here" or
        // writing a request nobody reads would hide that the cancel did not happen.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.Services.ThrowFrom<IJobStore>(
                nameof(IJobStore.CancelAsync), static () => new FakeStoreException("the job store is down")));
        var runId = TraconId.NewId();

        await host.Services.GetRequiredService<IRunStore>().StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
            Status = RunStatus.Queued,
        });

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Queued);
    }

    [Fact]
    public async Task A_request_that_loses_the_race_with_the_run_end_returns_409_and_writes_no_audit_entry()
    {
        // The row was Running when the endpoint read it and is over when it writes the request.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.Services.ReturnFrom<IRunStore, bool>(
                nameof(IRunStore.RequestCancellationAsync), false),
            configureServices: static services => services.Configure<RunReconciliationOptions>(static options => options.Enabled = true));
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("title").GetString().ShouldBe("Run already ended");
        (await AuditActionsAsync(host, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_store_error_while_the_request_is_recorded_answers_503_and_writes_no_audit_entry()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.Services.ThrowFrom<IRunStore>(
                nameof(IRunStore.RequestCancellationAsync), static () => new FakeStoreException("the run store is down")),
            configureServices: static services => services.Configure<RunReconciliationOptions>(static options => options.Enabled = true));
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await AuditActionsAsync(host, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancel_through_the_local_source_writes_one_run_cancel_audit_entry()
    {
        await using var host = await TraconTestHost.StartAsync();
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var cts = new CancellationTokenSource();
        using var registration = host.Services.GetRequiredService<IRunCancellationRegistry>().Register(runId, runId, "default", cts);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await AuditActionsAsync(host, runId)).ShouldBe(["run.cancel"]);
    }

    [Fact]
    public async Task Cancel_through_the_job_queue_writes_one_run_cancel_audit_entry()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder =>
            {
                builder.AddAgent(TestData.Definition());
                builder.UseScheduling(static options => options.RunWorker = false);
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await AuditActionsAsync(host, runId)).ShouldBe(["run.cancel"]);
    }

    [Fact]
    public async Task Cancel_recorded_for_the_owning_instance_writes_one_run_cancel_audit_entry()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Configure<RunReconciliationOptions>(static options => options.Enabled = true));
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await AuditActionsAsync(host, runId)).ShouldBe(["run.cancel"]);
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

    private static async Task<string[]> AuditActionsAsync(TraconTestHost host, Guid runId)
        => [.. (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { Entity = $"run:{runId}" }))
            .Select(static entry => entry.Action)];

    private static Uri CancelUri(Guid runId) => new($"/tracon/api/runs/{runId}/cancel", UriKind.Relative);

    private static async Task<HttpResponseMessage> SendAsTenant(TraconTestHost host, Uri uri, string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add(TenantHeader, tenant);

        return await host.Client.SendAsync(request);
    }

    /// <summary>A persistence failure: the provider's own <see cref="DbException"/> type, which the endpoint maps to <c>503</c>.</summary>
    private sealed class FakeStoreException(string message) : DbException(message);
}
