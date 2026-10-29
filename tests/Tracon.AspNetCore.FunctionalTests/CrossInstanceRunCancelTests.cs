using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 262 (F-764): <c>POST /api/runs/{id}/cancel</c> stops a run whichever replica the
/// request lands on.
/// </summary>
/// <remarks>
/// Two hosts share one SQLite file (<see cref="TwoHostFixture"/>). Host A runs the run, host
/// B receives the cancel. Each host owns its <see cref="IRunCancellationRegistry"/>, so B
/// has no registration for A's run: the old endpoint answered <c>409</c> and the run went on.
/// </remarks>
public sealed class CrossInstanceRunCancelTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string AgentName = "kod-agent";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task Cancel_on_another_instance_stops_a_direct_run()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        await using var pair = await StartAsync(hanging, reconciliation: true);

        var running = StartDirectRun(pair.A);
        var runId = await WaitForRunAsync(pair.A);
        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30));

        try
        {
            using var response = await pair.B.Client.PostAsync(CancelUri(runId), content: null);

            response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

            // The request is on the row at once; the run ends when the owner reads it.
            (await TraconTestHost.ReadJsonAsync(response)).GetProperty("cancelRequestedAt").ValueKind.ShouldBe(JsonValueKind.String);

            await pair.A.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled));
            await pair.B.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled));

            hanging.Calls.ShouldBe(1);

            var record = await GetRunAsync(pair.B, runId);
            record.GetProperty("cancelRequestedAt").ValueKind.ShouldBe(JsonValueKind.String);
        }
        finally
        {
            await DrainAsync(running);
        }
    }

    [Fact]
    public async Task Cancel_on_another_instance_returns_409_with_a_hint_when_reconciliation_is_off()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        await using var pair = await StartAsync(hanging, reconciliation: false);

        var running = StartDirectRun(pair.A);
        var runId = await WaitForRunAsync(pair.A);
        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30));

        try
        {
            using var response = await pair.B.Client.PostAsync(CancelUri(runId), content: null);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()
                .ShouldNotBeNull().ShouldContain("Tracon:RunReconciliation", Case.Sensitive);

            // Nothing was written: no owner would read it.
            var record = await GetRunAsync(pair.B, runId);
            record.GetProperty("status").GetString().ShouldBe(nameof(RunStatus.Running));
            record.GetProperty("cancelRequestedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            pair.A.Services.GetRequiredService<IRunCancellationRegistry>().TryCancel(runId, "default");
            await DrainAsync(running);
        }
    }

    [Fact]
    public async Task Cancel_of_another_tenants_run_is_404_and_writes_nothing()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        await using var pair = await StartAsync(
            hanging,
            reconciliation: true,
            configure: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var running = StartDirectRun(pair.A, tenant: "tenant-a");
        var runId = await WaitForRunAsync(pair.A, tenant: "tenant-a");
        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30));

        try
        {
            // The endpoint's own tenant check answers 404 before any write. The store's filter is
            // proven by RunStoreContract.RequestCancellationAsync_ignores_another_tenants_run.
            using var wrongTenant = await SendAsync(pair.B, CancelUri(runId), tenant: "tenant-b");

            wrongTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

            var record = await GetRunAsync(pair.B, runId, tenant: "tenant-a");
            record.GetProperty("status").GetString().ShouldBe(nameof(RunStatus.Running));
            record.GetProperty("cancelRequestedAt").ValueKind.ShouldBe(JsonValueKind.Null);

            // The owner is alive and the right tenant still reaches the run.
            using var rightTenant = await SendAsync(pair.B, CancelUri(runId), tenant: "tenant-a");
            rightTenant.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            await pair.A.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled), tenant: "tenant-a");
        }
        finally
        {
            pair.A.Services.GetRequiredService<IRunCancellationRegistry>().TryCancel(runId, "tenant-a");
            await DrainAsync(running);
        }
    }

    [Fact]
    public async Task Cancel_of_a_root_cancels_its_children_on_the_owner()
    {
        await using var pair = await StartAsync(new HangingModelProvider("hanging", respectsCancellation: true), reconciliation: true);

        // The owner (A) holds a root and one child in its registry; B only knows the root's row.
        var rootId = TraconId.NewId();
        var childId = TraconId.NewId();
        await pair.A.Services.GetRequiredService<IRunStore>().StartRunAsync(new RunStartInfo
        {
            RunId = rootId,
            AgentName = AgentName,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        using var rootSource = new CancellationTokenSource();
        using var childSource = new CancellationTokenSource();
        var registry = pair.A.Services.GetRequiredService<IRunCancellationRegistry>();
        using var rootRegistration = registry.Register(rootId, rootId, "default", rootSource);
        using var childRegistration = registry.Register(childId, rootId, "default", childSource);

        using var response = await pair.B.Client.PostAsync(CancelUri(rootId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        await WaitUntil.TrueAsync(
            () => Task.FromResult(rootSource.IsCancellationRequested && childSource.IsCancellationRequested),
            "the owner to cancel the root and its child");
    }

    [Fact]
    public async Task Cancel_of_an_orphaned_run_leaves_its_request_and_the_claim_closes_it_as_Failed()
    {
        // The owner died before it read the request (nobody registers the run): the request stays
        // on the row, and reconciliation closes the row as Failed. The process died; the cancel
        // was not what ended the run (open question 2). The claim is called by hand: the service
        // that calls it on a timer is covered by RunReconciliationTests.
        await using var pair = await StartAsync(new HangingModelProvider("hanging", respectsCancellation: true), reconciliation: true);

        var runId = TraconId.NewId();
        var runs = pair.A.Services.GetRequiredService<IRunStore>();
        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = AgentName,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = "default",
        });

        using var response = await pair.B.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        var claimed = await runs.ClaimOrphanedRunsAsync(DateTimeOffset.UtcNow.AddMinutes(-5), max: 10);

        var record = claimed.ShouldHaveSingleItem();
        record.Id.ShouldBe(runId);
        record.Status.ShouldBe(RunStatus.Failed);
        record.CancelRequestedAt.ShouldNotBeNull();

        var stored = (await runs.GetRunAsync(runId)).ShouldNotBeNull();
        stored.Status.ShouldBe(RunStatus.Failed);
        stored.CancelRequestedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Cancel_on_another_instance_stops_a_queued_run_running_on_another_worker()
    {
        // Host A has the worker, host B only takes the requests. The cancel goes through the job
        // queue: it needs neither reconciliation nor a registration in B's process.
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        await using var pair = await StartAsync(
            hanging,
            reconciliation: false,
            configureA: static builder => builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(200)),
            configureB: static builder => builder.UseScheduling(static options => options.RunWorker = false));

        using var accepted = await PostAsyncAsync(pair.B, new AgentRunRequest { Message = "hello" });
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30));

        using var response = await pair.B.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        // Within the worker's poll interval, not a heartbeat later.
        await pair.A.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled));

        var job = await pair.B.Services.GetRequiredService<IJobStore>().GetAsync("default", runId);

        job.ShouldNotBeNull().Status.ShouldBe(JobStatus.Cancelled);
        job.Attempt.ShouldBe(1);
        hanging.Calls.ShouldBe(1);

        // Not a request on the row: the queue path never needs one.
        (await GetRunAsync(pair.B, runId)).GetProperty("cancelRequestedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static async Task<TwoHostFixture> StartAsync(
        HangingModelProvider hanging,
        bool reconciliation,
        Action<ITraconBuilder>? configure = null,
        Action<ITraconBuilder>? configureA = null,
        Action<ITraconBuilder>? configureB = null)
        => await TwoHostFixture.StartAsync(
            configure: builder =>
            {
                builder
                    .AddModelProvider(hanging)
                    .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" } });
                configure?.Invoke(builder);
            },
            configureA: configureA,
            configureB: configureB,
            reconciliation: reconciliation);

    private static Task<HttpResponseMessage> StartDirectRun(TraconTestHost host, string? tenant = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RunUri) { Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }) };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return host.Client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsyncAsync(TraconTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri) { Content = JsonContent.Create(body) };
        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request);
    }

    /// <summary>The direct run is a synchronous call: its response ends with the run, or with the host.</summary>
    private static async Task DrainAsync(Task<HttpResponseMessage> running)
    {
        try
        {
            using var response = await running.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or TimeoutException or InvalidOperationException)
        {
            // A cancelled run answers with an error the test does not look at.
        }
    }

    private static async Task<Guid> WaitForRunAsync(TraconTestHost host, string tenant = "default")
        => (await WaitUntil.ValueAsync(
            async () => await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { TenantId = tenant, Take = 10 }),
            static runs => runs.Count > 0,
            "the direct run to open its row"))[0].Id;

    private static async Task<JsonElement> GetRunAsync(TraconTestHost host, Guid runId, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tracon/api/runs/{runId}", UriKind.Relative));

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await TraconTestHost.ReadJsonAsync(response)).Clone();
    }

    private static async Task<HttpResponseMessage> SendAsync(TraconTestHost host, Uri uri, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add(TenantHeader, tenant);

        return await host.Client.SendAsync(request);
    }

    private static Uri CancelUri(Guid runId) => new($"/tracon/api/runs/{runId}/cancel", UriKind.Relative);
}
