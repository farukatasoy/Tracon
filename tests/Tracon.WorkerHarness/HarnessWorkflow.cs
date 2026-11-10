using Microsoft.Extensions.AI;
using Tracon.Tests.Common;

namespace Tracon.WorkerHarness;

/// <summary>
/// Registers the workflow engine, with recovery on, and the two function steps of
/// the workflow the two-process resume proof kills halfway through.
/// </summary>
/// <remarks>
/// The definition itself is stored by the test (a Sequential workflow of the two
/// steps), so both processes read the same graph from the database, the way two
/// nodes of a real deployment do.
/// </remarks>
internal static class HarnessWorkflow
{
    /// <summary>Registers the engine and the steps.</summary>
    /// <param name="tracon">The Tracon chain.</param>
    /// <param name="settings">The harness settings.</param>
    public static void Register(ITraconBuilder tracon, WorkerHarnessSettings settings)
    {
        tracon.UseWorkflows(static options =>
        {
            options.ResumeInterruptedRuns = true;
            options.MaxResumeAttempts = 1;
        });

        tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
            WorkerHarnessContract.FirstStep,
            _ => async (messages, _, _) =>
            {
                await LogAsync(settings, WorkerHarnessContract.FirstStepStage).ConfigureAwait(false);

                return messages;
            });

        tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
            WorkerHarnessContract.SecondStep,
            _ => async (messages, _, cancellationToken) =>
            {
                await LogAsync(settings, WorkerHarnessContract.SecondStepStage).ConfigureAwait(false);

                // The harness is a worker process the test kills in the middle of this step: how long the step lasts is how
                // long the window to kill it stays open, and a process has no signal from the test to wait for but a span.
                await Task.Delay(settings.WorkDuration, cancellationToken).ConfigureAwait(false); // delay: product

                return messages;
            });
    }

    /// <summary>Writes one line with the run the step runs in.</summary>
    private static Task LogAsync(WorkerHarnessSettings settings, string stage)
        => HarnessExecutionLog.AppendAsync(
            settings.ExecutionLogPath,
            stage,
            settings.Name,
            TraconRunContext.Current?.RunId ?? Guid.Empty,
            attempt: 0);
}
