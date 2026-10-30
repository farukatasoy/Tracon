using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A resume from a checkpoint runs the SAME bound steps under the SAME values.
/// </summary>
/// <remarks>
/// MAF derives an executor's identity from the agent instance, and a
/// checkpoint is refused when the identity differs. A bound step compiles a new
/// agent every call, so the checkpoint only survives because the executor is the
/// workflow's own wrapper; this proves it in one process, with real compiled
/// agents. It also proves the values travel: the resumed run re-runs a step
/// from an EARLIER checkpoint, and that step must carry the recorded value, not
/// the agent's default.
/// </remarks>
public sealed class WorkflowParameterResumeTests
{
    [Fact]
    public async Task A_resume_from_the_first_checkpoint_reruns_steps_under_the_recorded_values()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        var run = await WorkflowParameterBindingTests.RunAsync(host, "podcast", new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = WorkflowParameterBindingTests.Values(("tone", "formal")),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        using var checkpoints = await host.Client.GetAsync($"/tracon/api/workflows/runs/{run.Id}/checkpoints");
        var first = (await TraconTestHost.ReadJsonAsync(checkpoints)).EnumerateArray().First();
        var checkpointId = first.GetProperty("checkpointId").GetString();
        var callsBefore = provider.Requests.Count;

        using var resumed = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{run.Id}/resume",
            new WorkflowResumeHttpRequest { CheckpointId = checkpointId });

        var frames = await SseReader.ReadAllAsync(await resumed.Content.ReadAsStreamAsync());

        // No 'error' frame: the checkpoint was compatible with the rebuilt graph.
        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        frames[^1].Event.ShouldBe("done");

        var newRunId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
        var newRun = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(newRunId))!;

        newRun.Status.ShouldBe(RunStatus.Completed, newRun.Error?.Message);

        // Whatever the resumed run called, it called under the recorded value -
        // never under the editor's own default ('casual').
        var rerun = provider.Requests.Skip(callsBefore).Select(static request => request.Options!.Instructions).ToList();

        rerun.ShouldNotBeEmpty("resuming from the first checkpoint re-runs at least one step");
        rerun.ShouldNotContain(static text => text!.Contains("casual", StringComparison.Ordinal));
        rerun.ShouldAllBe(static text => text!.Contains("formal", StringComparison.Ordinal));

        var record = await host.Services.GetRequiredService<IRunInputStore>()
            .GetAsync(host.Services.GetRequiredService<ITenantContext>().TenantId, newRunId);

        record.ShouldNotBeNull().Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("tone", "formal");
    }

    [Fact]
    public async Task A_resume_of_a_run_that_recorded_nothing_is_refused_with_the_reason()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(WorkflowParameterBindingTests.Definition(
                    "writer",
                    "Write the script in a {{tone}} tone.",
                    WorkflowParameterBindingTests.Tone(required: true)));
                builder.UseWorkflows();
            },
            configureServices: services => services.Configure<TraconOptions>(
                options => options.RunRecording.RecordRunInput = false));

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host, agentNames: ["writer"], workflowParameters: [WorkflowParameterBindingTests.Tone(required: true)]);

        var run = await WorkflowParameterBindingTests.RunAsync(host, "podcast", new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = WorkflowParameterBindingTests.Values(("tone", "formal")),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        using var resumed = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{run.Id}/resume",
            new WorkflowResumeHttpRequest());

        var frames = await SseReader.ReadAllAsync(await resumed.Content.ReadAsStreamAsync());
        var error = frames.Single(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
        var message = JsonDocument.Parse(error.Data).RootElement.GetProperty("message").GetString();

        message.ShouldNotBeNull().ShouldContain("did not record the values", Case.Sensitive);
        message.ShouldContain("RecordRunInput", Case.Sensitive);

        // No second workflow row was opened by the refusal.
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { Take = 50, OnlyRootRuns = true }))
            .Count(static row => row.Kind == RunKind.Workflow).ShouldBe(1);
    }
}
