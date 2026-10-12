using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-341: <c>POST /api/workflows/runs/{runId}/respond</c> refuses a response that does
/// not fit the port's response type with <c>400</c>, before any run opens.
/// </summary>
/// <remarks>
/// The contract (<see cref="WorkflowRespondHttpRequest"/>) said so, but the response was
/// only translated inside the resumed run, after the SSE headers were sent: the caller
/// got <c>200</c>, a NEW run row opened and ended <c>Failed</c>. The cases run the real
/// host, so the check is proven where the caller meets it - the HTTP status, before the
/// stream - and the run table proves nothing was opened.
/// </remarks>
public sealed class WorkflowRespondValidationTests
{
    private const string DecisionPort = "decision-port";
    private const string ApprovalPort = "approval-port";
    private const string NotePort = "note-port";

    [Fact]
    public async Task A_data_value_that_does_not_read_into_the_ports_type_is_refused_with_400_and_opens_no_run()
    {
        await using var host = await StartAsync();
        var (runId, requestId) = await RunToAwaitAsync(host, "decision-flow");
        var before = await CountRunsAsync(host);

        using var response = await RespondAsync(host, runId, new
        {
            requestId,
            data = new { approved = "maybe" },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("title").GetString().ShouldBe("Response invalid");
        problem.GetProperty("detail").GetString()!.ShouldContain(nameof(ReviewDecision), Case.Sensitive);
        (await CountRunsAsync(host)).ShouldBe(before);
    }

    [Fact]
    public async Task A_json_port_answered_without_data_is_refused_with_400()
    {
        await using var host = await StartAsync();
        var (runId, requestId) = await RunToAwaitAsync(host, "decision-flow");
        var before = await CountRunsAsync(host);

        using var response = await RespondAsync(host, runId, new { requestId, approved = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain(DecisionPort, Case.Sensitive);
        detail.ShouldContain("'data'", Case.Sensitive);
        (await CountRunsAsync(host)).ShouldBe(before);
    }

    [Fact]
    public async Task A_yes_no_port_answered_without_approved_is_refused_with_400()
    {
        await using var host = await StartAsync();
        var (runId, requestId) = await RunToAwaitAsync(host, "approval-flow");
        var before = await CountRunsAsync(host);

        using var response = await RespondAsync(host, runId, new { requestId, text = "yes" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain(ApprovalPort, Case.Sensitive);
        detail.ShouldContain("'approved'", Case.Sensitive);
        (await CountRunsAsync(host)).ShouldBe(before);
    }

    [Fact]
    public async Task A_text_port_answered_without_text_is_refused_with_400()
    {
        await using var host = await StartAsync();
        var (runId, requestId) = await RunToAwaitAsync(host, "note-flow");
        var before = await CountRunsAsync(host);

        using var response = await RespondAsync(host, runId, new { requestId, approved = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain(NotePort, Case.Sensitive);
        detail.ShouldContain("'text'", Case.Sensitive);
        (await CountRunsAsync(host)).ShouldBe(before);
    }

    [Fact]
    public async Task A_response_that_fits_the_port_still_resumes_the_run()
    {
        await using var host = await StartAsync();
        var (runId, requestId) = await RunToAwaitAsync(host, "decision-flow");

        using var response = await RespondAsync(host, runId, new
        {
            requestId,
            data = new { approved = true, note = "ship it" },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var newRunId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();

        frames.ShouldContain(static frame => frame.Data.Contains("approved: ship it", StringComparison.Ordinal));
        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(newRunId))
            .ShouldNotBeNull().Status.ShouldBe(RunStatus.Completed);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder
            .UseWorkflows()
            .AddWorkflow("decision-flow", static _ => BuildDecisionWorkflow(), "Waits for a structured decision.")
            .AddWorkflow("approval-flow", static _ => BuildApprovalWorkflow(), "Waits for a yes or no.")
            .AddWorkflow("note-flow", static _ => BuildNoteWorkflow(), "Waits for a note."));

    private static async Task<(Guid RunId, string RequestId)> RunToAwaitAsync(TraconTestHost host, string name)
    {
        using var started = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{name}/run",
            new WorkflowRunHttpRequest { Message = "the draft" });

        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await started.Content.ReadAsStreamAsync());
        var runId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();

        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))
            .ShouldNotBeNull().Status.ShouldBe(RunStatus.AwaitingInput);

        using var listed = await host.Client.GetAsync(new Uri($"/tracon/api/workflows/runs/{runId}/requests", UriKind.Relative));
        var requestId = (await TraconTestHost.ReadJsonAsync(listed))[0].GetProperty("requestId").GetString()!;

        return (runId, requestId);
    }

    private static Task<HttpResponseMessage> RespondAsync(TraconTestHost host, Guid runId, object body)
        => host.Client.PostAsJsonAsync($"/tracon/api/workflows/runs/{runId}/respond", body);

    private static async Task<int> CountRunsAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 200 })).Count;

    private static Workflow BuildDecisionWorkflow()
    {
        var port = RequestPort.Create<string, ReviewDecision>(DecisionPort).BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Review: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var end = ExecutorBindingExtensions.BindAsExecutor<ReviewDecision, string>(
            static decision => decision.Approved ? $"approved: {decision.Note}" : "rejected",
            id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, port)
            .AddEdge(port, end)
            .WithOutputFrom(end)
            .WithName("decision-flow")
            .Build();
    }

    private static Workflow BuildApprovalWorkflow()
    {
        var port = RequestPort.Create<string, bool>(ApprovalPort).BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Approve: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var end = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "approved" : "rejected",
            id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, port)
            .AddEdge(port, end)
            .WithOutputFrom(end)
            .WithName("approval-flow")
            .Build();
    }

    private static Workflow BuildNoteWorkflow()
    {
        var port = RequestPort.Create<string, string>(NotePort).BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Comment on: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var end = ExecutorBindingExtensions.BindAsExecutor<string, string>(
            static note => $"noted: {note}",
            id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, port)
            .AddEdge(port, end)
            .WithOutputFrom(end)
            .WithName("note-flow")
            .Build();
    }

    /// <summary>The structured answer the decision port expects.</summary>
    internal sealed record ReviewDecision(bool Approved, string? Note);
}
