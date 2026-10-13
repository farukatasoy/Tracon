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
            _ => (messages, _, _) =>
            {
                Log(settings, WorkerHarnessContract.FirstStepStage);

                return new ValueTask<List<ChatMessage>>(messages);
            });

        tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
            WorkerHarnessContract.SecondStep,
            _ => async (messages, _, cancellationToken) =>
            {
                Log(settings, WorkerHarnessContract.SecondStepStage);

                await Task.Delay(settings.WorkDuration, cancellationToken).ConfigureAwait(false); // delay: simulated

                return messages;
            });
    }

    /// <summary>Writes one line with the run the step runs in.</summary>
    private static void Log(WorkerHarnessSettings settings, string stage)
        => HarnessExecutionLog.Append(
            settings.ExecutionLogPath,
            stage,
            settings.Name,
            TraconRunContext.Current?.RunId ?? Guid.Empty,
            attempt: 0);
}
