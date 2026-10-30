using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>Behaviour 1: <c>POST /api/runs/{id}/cancel</c> stops a run whichever node the request lands on.</summary>
public abstract partial class TwoNodeAcceptanceContract
{
    private const string AgentName = "kod-agent";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task Cancel_on_the_other_node_stops_a_direct_run()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        await using var pair = await StartCancelPairAsync(hanging, reconciliation: true);

        var running = StartDirectRun(pair.A);
        var runId = await WaitForRunAsync(pair.A);
        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30));

        try
        {
            // Node A runs it, node B receives the cancel. B holds no registration for A's run: before this change the
            // endpoint answered 409 and the run went on.
            using var response = await pair.B.Client.PostAsync(CancelUri(runId), content: null);

            response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

            // The request is on the row at once; the run ends when its owner reads it on the next heartbeat.
            (await TraconTestHost.ReadJsonAsync(response)).GetProperty("cancelRequestedAt").ValueKind.ShouldBe(JsonValueKind.String);

            await pair.A.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled));
            await pair.B.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled));

            hanging.Calls.ShouldBe(1);
        }
        finally
        {
            await DrainAsync(running);
        }
    }

    [Fact]
    public async Task Cancel_on_the_other_node_stops_a_queued_run_that_a_worker_on_that_node_executes()
    {
        // Node A has the worker, node B only takes requests. The cancel goes through the job queue: it needs neither
        // reconciliation nor a registration in B's process.
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        await using var pair = await StartCancelPairAsync(
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

        // Not retried on the other node: the cancel is final, and the model was called once.
        job.Attempt.ShouldBe(1);
        hanging.Calls.ShouldBe(1);
    }

    private async Task<TwoNodeCluster> StartCancelPairAsync(
        HangingModelProvider hanging,
        bool reconciliation,
        Action<ITraconBuilder>? configureA = null,
        Action<ITraconBuilder>? configureB = null)
        => await TwoNodeCluster.StartAsync(
            NewDatabase(),
            configure: builder => builder
                .AddModelProvider(hanging)
                .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" } }),
            configureA: configureA,
            configureB: configureB,
            reconciliation: reconciliation);

    private static Task<HttpResponseMessage> StartDirectRun(TraconTestHost host)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RunUri) { Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }) };

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

    private static async Task<Guid> WaitForRunAsync(TraconTestHost host)
        => (await WaitUntil.ValueAsync(
            async () => await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { TenantId = "default", Take = 10 }),
            static runs => runs.Count > 0,
            "the direct run to open its row"))[0].Id;

    private static Uri CancelUri(Guid runId) => new($"/tracon/api/runs/{runId}/cancel", UriKind.Relative);
}
