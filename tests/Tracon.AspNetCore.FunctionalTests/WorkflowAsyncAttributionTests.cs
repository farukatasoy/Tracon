using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Failure mode 9: a queued workflow operation carries the caller's attribution and
/// session in its job, because the worker has no request to read them from.
/// </summary>
/// <remarks>
/// The attribution here is bound to the HTTP request, the way a consumer binds its
/// identity pipeline: in the worker it reads only the ambient scope the handler opens
/// from the payload. If the payload did not carry it, the workflow's steps would run
/// with no user and no labels.
/// </remarks>
public sealed class WorkflowAsyncAttributionTests
{
    [Fact]
    public async Task The_job_payload_carries_the_operation_the_session_and_the_callers_attribution()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartBoundAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(
            host,
            $"/api/workflows/{Recoverable}/run",
            new WorkflowRunHttpRequest
            {
                Message = "episode 7",
                SessionId = "production-7",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["tone"] = "formal" },
            });
        var runId = await AcceptedRunIdAsync(accepted);

        var job = (await host.Services.GetRequiredService<IJobStore>().GetAsync(TenantOf(host), runId)).ShouldNotBeNull();
        var payload = job.Payload;

        payload.GetProperty("operation").GetString().ShouldBe("run");
        payload.GetProperty("runId").GetGuid().ShouldBe(runId);
        payload.GetProperty("sessionId").GetString().ShouldBe("production-7");
        payload.GetProperty("message").GetString().ShouldBe("episode 7");
        payload.GetProperty("parameters").GetProperty("tone").GetString().ShouldBe("formal");
        payload.GetProperty("userId").GetString().ShouldBe("ada");
        payload.GetProperty("labels").GetProperty("team").GetString().ShouldBe("audio");
    }

    [Fact]
    public async Task The_worker_runs_the_steps_under_the_queued_callers_user_and_labels()
    {
        var probe = new RecoveryProbe();
        AgentRunScope? seen = null;

        await using var host = await StartBoundAsync(
            probe,
            runWorker: true,
            configureTracon: builder => builder.AddWorkflowFunction<List<Microsoft.Extensions.AI.ChatMessage>, List<Microsoft.Extensions.AI.ChatMessage>>(
                "attribution-probe",
                _ => (messages, _, _) =>
                {
                    seen = TraconRunContext.Current;

                    return new ValueTask<List<Microsoft.Extensions.AI.ChatMessage>>(messages);
                }));
        await using var lifetime = probe;

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/workflows/attributed",
                   new WorkflowSaveRequest
                   {
                       Kind = WorkflowKind.Sequential,
                       Nodes = [new WorkflowNodeReference { Name = "attribution-probe", Kind = WorkflowNodeKind.Function }],
                   }))
        {
            saved.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        }

        using var accepted = await PostAsync(host, "/api/workflows/attributed/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var runId = await AcceptedRunIdAsync(accepted);

        await host.WaitForRunStatusAsync(runId, nameof(RunStatus.Completed));

        seen.ShouldNotBeNull();
        seen.UserId.ShouldBe("ada");
        seen.Labels.ShouldNotBeNull()["team"].ShouldBe("audio");

        var row = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();
        row.UserId.ShouldBe("ada");
        row.Labels.ShouldNotBeNull()["team"].ShouldBe("audio");
    }

    [Fact]
    public async Task A_queued_response_carries_the_answering_callers_attribution()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartBoundAsync(probe, runWorker: true);
        await using var lifetime = probe;

        using var waiting = await PostAsync(host, $"/api/workflows/{Approval}/run", new WorkflowRunHttpRequest { Message = "the draft" });
        var waitingId = await AcceptedRunIdAsync(waiting);
        await host.WaitForRunStatusAsync(waitingId, nameof(RunStatus.AwaitingInput));

        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/runs/{waitingId}/requests");
        var requestId = (await TraconTestHost.ReadJsonAsync(listed))[0].GetProperty("requestId").GetString()!;

        using var accepted = await PostAsync(host, $"/api/workflows/runs/{waitingId}/respond", new { requestId, approved = true });
        var answerId = await AcceptedRunIdAsync(accepted);

        var job = (await host.Services.GetRequiredService<IJobStore>().GetAsync(TenantOf(host), answerId)).ShouldNotBeNull();

        job.Payload.GetProperty("operation").GetString().ShouldBe("respond");
        job.Payload.GetProperty("sourceRunId").GetGuid().ShouldBe(waitingId);
        job.Payload.GetProperty("requestId").GetString().ShouldBe(requestId);
        job.Payload.GetProperty("approved").GetBoolean().ShouldBeTrue();
        job.Payload.GetProperty("userId").GetString().ShouldBe("ada");

        await host.WaitForRunStatusAsync(answerId, nameof(RunStatus.Completed));

        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(answerId)).ShouldNotBeNull().UserId.ShouldBe("ada");
    }

    private static Task<TraconTestHost> StartBoundAsync(
        RecoveryProbe probe,
        bool runWorker,
        Action<ITraconBuilder>? configureTracon = null)
        => StartAsync(
            probe,
            runWorker: runWorker,
            configureTracon: configureTracon,
            configureServices: static services =>
            {
                services.AddHttpContextAccessor();
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext, RequestOrAmbientAttribution>());
            });

    /// <summary>The request's identity on a request; the ambient scope in the worker.</summary>
    private sealed class RequestOrAmbientAttribution(IHttpContextAccessor accessor) : IRunAttributionContext
    {
        private static readonly IReadOnlyDictionary<string, string> Team =
            new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "audio" };

        public string? UserId => accessor.HttpContext is not null ? "ada" : AmbientRunAttributionScope.CurrentUserId;

        public IReadOnlyDictionary<string, string>? Labels
            => accessor.HttpContext is not null ? Team : AmbientRunAttributionScope.CurrentLabels;
    }
}
