using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.AgentStepHost;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.ToolApprovalWorkflowHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The definition version a queued answer to a stored workflow's request carries from the endpoint to the worker: the
/// version the run recorded when none is named, the one named when it is, and a <c>400</c> before any job is queued when
/// the workflow has no such version.
/// </summary>
/// <remarks>
/// The code-defined case is <c>WorkflowResumeVersionHttpTests</c>; a queued resume is <c>WorkflowAsyncRunTests</c>. The
/// stamp on the continuation row is the observable: it is the version the worker compiled, so a payload that lost
/// <c>definitionVersion</c> would show the current version whatever the request named.
/// </remarks>
public sealed class WorkflowAsyncRespondVersionTests
{
    [Fact]
    public async Task A_queued_answer_without_a_version_continues_the_version_the_run_recorded()
    {
        var calls = new int[1];
        await using var host = await StartHostAsync(calls);
        var waiting = await RunToApprovalAsync(host, StoredApproval);
        await EditAsync(host);

        var answerId = await QueueAnswerAsync(host, waiting, definitionVersion: null);

        (await host.WaitForTerminalRunAsync(answerId)).AgentVersion.ShouldBe(1, "no version was named: the worker continued the recorded one");
    }

    [Fact]
    public async Task A_queued_answer_naming_a_version_continues_that_version()
    {
        var calls = new int[1];
        await using var host = await StartHostAsync(calls);
        var waiting = await RunToApprovalAsync(host, StoredApproval);
        await EditAsync(host);

        var answerId = await QueueAnswerAsync(host, waiting, definitionVersion: 2);

        (await host.WaitForTerminalRunAsync(answerId)).AgentVersion.ShouldBe(2, "the version the request named reached the worker through the job");
    }

    [Fact]
    public async Task A_queued_answer_naming_a_version_the_workflow_does_not_have_is_400_and_queues_no_job()
    {
        var calls = new int[1];
        await using var host = await StartHostAsync(calls);
        var waiting = await RunToApprovalAsync(host, StoredApproval);
        var jobsBefore = (await QueuedWorkflowHarness.JobsAsync(host)).Count;

        using var refused = await RespondQueuedAsync(host, waiting, definitionVersion: 9);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await QueuedWorkflowHarness.JobsAsync(host)).Count.ShouldBe(jobsBefore, "the refusal came before a job was queued");
        calls[0].ShouldBe(0, "no tool ran for a refused answer");
    }

    private static Task<TraconTestHost> StartHostAsync(int[] calls)
        => ToolApprovalWorkflowHost.StartAsync(
            PricedProvider().CallsTool(Tool, new { orderId = "ORD-7" }).EchoesLastToolResult(),
            calls);

    /// <summary>Edits the stored chain so that it has a second version; a description is enough.</summary>
    private static async Task EditAsync(TraconTestHost host)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{StoredApproval}",
            new WorkflowSaveRequest { Kind = WorkflowKind.Sequential, AgentNames = [Agent], Description = "Edited while the run waited." });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> QueueAnswerAsync(TraconTestHost host, WaitingRun waiting, int? definitionVersion)
    {
        using var accepted = await RespondQueuedAsync(host, waiting, definitionVersion);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    private static async Task<HttpResponseMessage> RespondQueuedAsync(TraconTestHost host, WaitingRun waiting, int? definitionVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/runs/{waiting.Run.Id}/respond")
        {
            Content = JsonContent.Create(new { requestId = waiting.RequestId, approved = true, definitionVersion }),
        };

        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request);
    }
}
