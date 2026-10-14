using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The answers the resume and respond endpoints give about the definition version a run
/// continues with (phase 200, failure mode 11): a version that does not exist is a <c>400</c>
/// before a stream opens or a job is queued, never a run that silently takes the current
/// definition.
/// </summary>
public sealed class WorkflowResumeVersionHttpTests
{
    private const string Name = "narrated";

    [Fact]
    public async Task A_resume_naming_a_version_the_workflow_does_not_have_is_a_bad_request_and_opens_no_run()
    {
        await using var host = await StartAsync();

        await SaveOkAsync(host, Name, Chain("""{"voice":"alloy"}"""));

        var first = await RunAsync(host, Name);
        var rows = await CountRunsAsync(host);

        using var response = await ResumeAsync(host, first.Record.Id, definitionVersion: 9);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await ProblemOf(response);

        problem.GetProperty("title").GetString().ShouldBe("Definition version not found");
        problem.GetProperty("detail").GetString()!.ShouldContain("Workflow 'narrated' has no version 9");
        (await CountRunsAsync(host)).ShouldBe(rows, "the refusal came before any run row was opened");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task A_resume_naming_a_version_below_one_is_a_bad_request(int version)
    {
        await using var host = await StartAsync();

        await SaveOkAsync(host, Name, Chain("""{"voice":"alloy"}"""));

        var first = await RunAsync(host, Name);

        using var response = await ResumeAsync(host, first.Record.Id, definitionVersion: version);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemOf(response)).GetProperty("title").GetString().ShouldBe("Definition version invalid");
    }

    [Fact]
    public async Task A_resume_of_a_run_whose_recorded_version_is_gone_is_a_bad_request_that_says_to_name_one()
    {
        await using var host = await StartAsync();

        await SaveOkAsync(host, Name, Chain("""{"voice":"alloy"}"""));

        var first = await RunAsync(host, Name);

        // The row names version 7, which the workflow never had (a restored row, a lost history).
        await RewriteStampAsync(host, first.Record, 7);

        using var response = await ResumeAsync(host, first.Record.Id, definitionVersion: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await ProblemOf(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("recorded version 7 of workflow 'narrated'");
        detail.ShouldContain("'definitionVersion'");
    }

    [Fact]
    public async Task A_resume_that_names_the_recorded_version_or_the_current_one_is_accepted()
    {
        await using var host = await StartAsync();

        await SaveOkAsync(host, Name, Chain("""{"voice":"alloy"}"""));

        var first = await RunAsync(host, Name);

        await SaveOkAsync(host, Name, Chain("""{"voice":"nova"}"""));

        foreach (var version in new int?[] { null, 1, 2 })
        {
            using var response = await ResumeAsync(host, first.Record.Id, version);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

            var resumed = await ReadRunAsync(host, response);

            resumed.Record.Status.ShouldBe(RunStatus.Completed, resumed.Record.Error?.Message);
            resumed.Record.AgentVersion.ShouldBe(version ?? 1);
        }
    }

    [Fact]
    public async Task A_workflow_defined_in_code_has_no_version_to_name_but_still_resumes_without_one()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .UseWorkflows()
            .AddWorkflow("approval-flow", static _ => ApprovalGraph.Build(), "Waits for a yes or no."));

        var runId = await StartAwaitingAsync(host);

        using var named = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{runId}/resume",
            new WorkflowResumeHttpRequest { DefinitionVersion = 1 });

        named.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemOf(named)).GetProperty("detail").GetString()!.ShouldContain("'approval-flow' is a workflow defined in code");

        using var plain = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{runId}/resume",
            new WorkflowResumeHttpRequest());

        plain.StatusCode.ShouldBe(HttpStatusCode.OK, await plain.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_response_naming_a_version_the_workflow_does_not_have_is_a_bad_request()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .UseWorkflows()
            .AddWorkflow("approval-flow", static _ => ApprovalGraph.Build(), "Waits for a yes or no."));

        var runId = await StartAwaitingAsync(host);
        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/runs/{runId}/requests");
        var requestId = (await TraconTestHost.ReadJsonAsync(listed))[0].GetProperty("requestId").GetString()!;

        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{runId}/respond",
            new { requestId, approved = true, definitionVersion = 3 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemOf(response)).GetProperty("detail").GetString()!.ShouldContain("'approval-flow' is a workflow defined in code");
    }

    // --- Helpers ---------------------------------------------------------

    private static Task<HttpResponseMessage> ResumeAsync(TraconTestHost host, Guid runId, int? definitionVersion)
        => host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{runId}/resume",
            new WorkflowResumeHttpRequest { DefinitionVersion = definitionVersion });

    private static async Task<int> CountRunsAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 200 })).Count;

    /// <summary>Writes the row again naming another version, the way a restored row or a lost history reads.</summary>
    private static async Task RewriteStampAsync(TraconTestHost host, RunRecord run, int version)
        => await host.Services.GetRequiredService<IRunStore>().StartRunAsync(new RunStartInfo
        {
            RunId = run.Id,
            AgentName = run.AgentName,
            Kind = run.Kind,
            WorkflowName = run.WorkflowName,
            Status = run.Status,
            StartedAt = run.StartedAt,
            TenantId = run.TenantId,
            SessionId = run.SessionId,
            AgentVersion = version,
        });

    private static async Task<Guid> StartAwaitingAsync(TraconTestHost host)
    {
        using var started = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/approval-flow/run",
            new WorkflowRunHttpRequest { Message = "the draft" });

        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await started.Content.ReadAsStreamAsync());

        return JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
    }

    /// <summary>The smallest graph that waits for a yes/no answer.</summary>
    private static class ApprovalGraph
    {
        public static Workflow Build()
        {
            var port = RequestPort.Create<string, bool>("gate-port").BindAsExecutor(allowWrappedRequests: false);

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
    }
}
