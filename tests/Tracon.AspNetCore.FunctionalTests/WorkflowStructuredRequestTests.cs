using System.Net;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A pending request whose type is a record of the graph's own is answered like a string one (phase 211).
/// </summary>
/// <remarks>
/// 🚨 The run that answers reads the request back from a checkpoint, where its data is JSON, and
/// <c>ExternalRequest.TryGetDataAs</c> then deserializes it into whatever it is asked for. The plan-review check
/// asked for <c>MagenticPlanReviewRequest</c>, every member of which is optional, so a record such as
/// <c>{"text": "...", "violation": null}</c> read as a plan review and the answer was built the wrong response type:
/// "Message type MagenticPlanReviewResponse does not match expected response type". The run that PUBLISHED the
/// request held the live object and never showed it; only the run that answers does.
/// </remarks>
public sealed class WorkflowStructuredRequestTests
{
    private const string Flow = "structured-request";

    [Fact]
    public async Task A_request_that_is_a_record_is_answered_and_the_run_completes()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseWorkflows();
                builder.AddWorkflow(Flow, static _ => Build());
            });

        var (runId, requestId) = await RunToAwaitAsync(host, Flow);

        using var response = await RespondAsync(host, runId, new { requestId, approved = true });
        var frames = await FramesAsync(response);

        frames[^1].Event.ShouldBe("done");
        (await host.WaitForTerminalRunAsync(RunIdOf(frames))).Status.ShouldBe(RunStatus.Completed);
        frames.ShouldContain(static frame => frame.Data.Contains("approved Draft one", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_request_that_is_a_record_is_listed_with_a_yes_or_no_form()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseWorkflows();
                builder.AddWorkflow(Flow, static _ => Build());
            });

        var (runId, _) = await RunToAwaitAsync(host, Flow);

        using var listed = await host.Client.GetAsync(new Uri($"/tracon/api/workflows/runs/{runId}/requests", UriKind.Relative));

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var request = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().Single();

        request.GetProperty("form").GetString().ShouldBe("Boolean");
    }

    private static Workflow Build()
    {
        var port = RequestPort.Create<Brief, bool>(Port).BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, Brief>(
            static _ => new Brief("Draft one", null),
            id: "start");

        var finish = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "approved Draft one" : "rejected",
            id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, port)
            .AddEdge(port, finish)
            .WithOutputFrom(finish)
            .WithName(Flow)
            .Build();
    }

    // The shape that tripped the check: every member optional, as in the plan review it was mistaken for.
    private sealed record Brief(string Text, string? Violation);
}
