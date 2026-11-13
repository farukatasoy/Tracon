using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A stored workflow's response mode is read from the definition version the waiting run
/// recorded (the rule for the graph applied to the decision rule).
/// </summary>
/// <remarks>
/// A stored definition asks for human input through a manager agent's plan approval
/// (<see cref="WorkflowDefinition.RequirePlanApproval"/>); the host's echo model is enough for
/// the manager to publish its plan and wait.
/// </remarks>
public sealed class WorkflowResponseModeVersionTests
{
    private const string Name = "planned";

    [Fact]
    public async Task An_edit_made_while_the_run_waits_does_not_change_how_it_is_answered()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition("manager"))
            .AddAgent(TestData.Definition("worker"))
            .UseWorkflows());

        await SaveAsync(host, WorkflowResponseMode.SingleAnswer);

        var (sourceId, requestId) = await RunToAwaitAsync(host, Name);

        // Saved again while the run waits: version 2 opens a branch for every answer.
        await SaveAsync(host, WorkflowResponseMode.Branch);

        using var approve = await RespondAsync(host, sourceId, new { requestId, approved = true });
        await FramesAsync(approve);

        // Version 1, the one the run recorded, takes one answer per request.
        using var revise = await RespondAsync(host, sourceId, new { requestId, approved = false, text = "add a budget" });

        revise.StatusCode.ShouldBe(HttpStatusCode.Conflict, await revise.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_run_started_after_the_edit_takes_the_new_mode()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition("manager"))
            .AddAgent(TestData.Definition("worker"))
            .UseWorkflows());

        await SaveAsync(host, WorkflowResponseMode.SingleAnswer);
        await SaveAsync(host, WorkflowResponseMode.Branch);

        var (sourceId, requestId) = await RunToAwaitAsync(host, Name);

        using var approve = await RespondAsync(host, sourceId, new { requestId, approved = true });
        await FramesAsync(approve);

        using var revise = await RespondAsync(host, sourceId, new { requestId, approved = false, text = "add a budget" });

        revise.StatusCode.ShouldBe(HttpStatusCode.OK, await revise.Content.ReadAsStringAsync());
        await FramesAsync(revise);
    }

    [Fact]
    public async Task A_version_named_on_a_resume_does_not_lift_the_claim_a_single_answer_request_already_has()
    {
        // The request is published again under the same session and request id when a run is resumed. The claim is the
        // record of which answer the request took, whatever mode the version of the run that publishes it again names:
        // a resume that names a version which opens a branch for every answer must not turn an answered request into
        // an open one.
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition("manager"))
            .AddAgent(TestData.Definition("worker"))
            .UseWorkflows());

        await SaveAsync(host, WorkflowResponseMode.SingleAnswer);

        var (sourceId, requestId) = await RunToAwaitAsync(host, Name);

        await SaveAsync(host, WorkflowResponseMode.Branch);

        using var approve = await RespondAsync(host, sourceId, new { requestId, approved = true });
        await FramesAsync(approve);

        using var resumed = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{sourceId}/resume",
            new WorkflowResumeHttpRequest { DefinitionVersion = 2 },
            TestContext.Current.CancellationToken);

        resumed.StatusCode.ShouldBe(HttpStatusCode.OK, await resumed.Content.ReadAsStringAsync());

        var resumedId = RunIdOf(await FramesAsync(resumed));

        (await RequestsAsync(host, resumedId)).Contains(requestId, StringComparer.Ordinal)
            .ShouldBeTrue("the resumed run waits at the same request");

        using var revise = await RespondAsync(host, resumedId, new { requestId, approved = false, text = "add a budget" });

        revise.StatusCode.ShouldBe(HttpStatusCode.Conflict, await revise.Content.ReadAsStringAsync());
    }

    private static async Task SaveAsync(TraconTestHost host, WorkflowResponseMode mode)
    {
        using var saved = await host.Client.PutAsJsonAsync($"/tracon/api/workflows/{Name}", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            AgentNames = ["worker"],
            MaxIterations = 2,
            RequirePlanApproval = true,
            ResponseMode = mode,
        });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(saved)).GetProperty("responseMode").GetString().ShouldBe(mode.ToString());
    }
}
