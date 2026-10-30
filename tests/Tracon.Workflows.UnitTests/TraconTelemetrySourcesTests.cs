using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Agents.AI.Workflows;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The run trace listens to the framework's workflow source by name; the name must be the one the framework emits.
/// </summary>
public sealed class TraconTelemetrySourcesTests
{
    [Fact]
    public async Task The_framework_workflow_source_has_the_name_the_run_trace_listens_to()
    {
        // A rename in a framework upgrade would not break anything that compiles: the listener would just stop
        // seeing the spans of a graph built in code. The probe asks the framework which source it writes to.
        const string executorId = "telemetry-source-probe";
        var sources = new ConcurrentBag<string>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.GetTagItem("executor.id")?.ToString(), executorId, StringComparison.Ordinal))
                {
                    sources.Add(activity.Source.Name);
                }
            },
        };

        ActivitySource.AddActivityListener(listener);

        var step = ExecutorBindingExtensions.BindAsExecutor<string, string>(static text => text, id: executorId);
        var workflow = new WorkflowBuilder(step)
            .WithOutputFrom(step)
            .WithOpenTelemetry()
            .Build();

        await using var run = await InProcessExecution.RunAsync(workflow, "probe", cancellationToken: TestContext.Current.CancellationToken);

        sources.Distinct(StringComparer.Ordinal).ShouldBe([TraconTelemetrySources.MafWorkflows]);
    }
}
