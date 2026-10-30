using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A caller that cancels while a bound step is in flight leaves no open row.
/// </summary>
/// <remarks>
/// A bound step does more before its model call than a catalog step
/// (a definition lookup, a compile outside the cache, the decorator chain), and a
/// parameterized run also writes a <c>run_inputs</c> row. A cancellation landing
/// anywhere in that stretch must still close the workflow row as
/// <c>Canceled</c> and close the step's own row; a row left
/// <c>Running</c> is reported minutes later as a failure that never happened.
/// </remarks>
public sealed class WorkflowParameterCancellationTests
{
    [Fact]
    public async Task Cancelling_while_a_bound_step_waits_for_the_model_closes_every_row()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(hanging)
                .AddAgent(WorkflowParameterBindingTests.Definition(
                    "writer",
                    "Write the script in a {{tone}} tone.",
                    WorkflowParameterBindingTests.Tone(required: true)) with
                {
                    Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" },
                })
                .UseWorkflows());

        await WorkflowParameterBindingTests.SaveWorkflowAsync(
            host,
            agentNames: ["writer"],
            workflowParameters: [WorkflowParameterBindingTests.Tone(required: true)]);

        var runner = host.Services.GetRequiredService<IWorkflowRunner>();
        using var cancellation = new CancellationTokenSource();

        var drain = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in runner.RunStreamingAsync(
                                   new WorkflowRunRequest
                                   {
                                       WorkflowName = "podcast",
                                       Message = "Episode 12",
                                       Parameters = WorkflowParameterBindingTests.Values(("tone", "formal")),
                                   },
                                   cancellation.Token))
                {
                    // Drain.
                }
            }
            catch (OperationCanceledException)
            {
                // The caller cancelled; the row must still be closed.
            }
        });

        // The step reached the model: the bound compile and the record are behind us.
        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30));
        await cancellation.CancelAsync();
        await drain.WaitAsync(TimeSpan.FromSeconds(30));

        var rows = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { Take = 50, OnlyRootRuns = false });

        rows.ShouldNotBeEmpty();
        rows.ShouldNotContain(static row => row.Status == RunStatus.Running, "a cancelled run left a row open");
        rows.Single(static row => row.Kind == RunKind.Workflow).Status.ShouldBe(RunStatus.Canceled);

        var workflowRow = rows.Single(static row => row.Kind == RunKind.Workflow);

        // The values were recorded before the step ran, so the cancelled run's
        // record is readable.
        var record = await host.Services.GetRequiredService<IRunInputStore>()
            .GetAsync(host.Services.GetRequiredService<ITenantContext>().TenantId, workflowRow.Id);

        record.ShouldNotBeNull().Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("tone", "formal");
    }
}
