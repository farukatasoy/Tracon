using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Observes a tool call whose timeout was already reported and writes its
/// eventual outcome onto that call's existing record.
/// </summary>
/// <remarks>
/// <para>
/// A timeout bounds the <em>wait</em>, and the cancellation it then requests is
/// cooperative. A tool body that does not read its token keeps running, and it
/// can still succeed and still spend real money — an image generation that
/// needs 35 seconds against a 30 second limit produces a billed image after the
/// model has already been told the call failed.
/// </para>
/// <para>
/// Without this, that spend had nowhere to land: the record said the call timed
/// out, its result and usage columns stayed empty, and the charge left no trace
/// in any cost report. The measurement the tool reported through
/// <c>TraconToolUsage.Report</c> did reach the run's accumulator; nothing ever
/// took it back out.
/// </para>
/// <para>
/// Everything this needs is captured <strong>synchronously, at call time</strong>.
/// The scope and the call identity both live in an <c>AsyncLocal</c>, and the
/// continuation that runs when the call finally settles cannot be relied on to
/// still see either.
/// </para>
/// </remarks>
internal static class LateToolCompletionRecorder
{
    /// <summary>
    /// Attaches the observer to a call the timeout stopped waiting for.
    /// </summary>
    /// <param name="invocation">The call that is still running.</param>
    /// <param name="scope">The run scope captured before the call started, or <see langword="null"/> outside a recorded run.</param>
    /// <param name="callId">The model's call identity, captured before the call started.</param>
    /// <param name="toolName">The tool's name, for the log message.</param>
    /// <param name="timeout">The timeout that was reported, for the log message.</param>
    /// <param name="startedAt">The timestamp the call started at, for the real duration.</param>
    /// <param name="timeProvider">The time source.</param>
    /// <param name="logger">The logger for the boundary.</param>
    /// <remarks>
    /// Observing the task is itself required: an unobserved faulted task would
    /// otherwise surface as an unhandled exception on the finalizer thread.
    /// </remarks>
    public static void Observe(
        Task<object?> invocation,
        AgentRunScope? scope,
        string? callId,
        string toolName,
        TimeSpan timeout,
        long startedAt,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _ = invocation.ContinueWith(
            task => OnSettled(task, scope, callId, toolName, timeout, startedAt, timeProvider, logger),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void OnSettled(
        Task<object?> task,
        AgentRunScope? scope,
        string? callId,
        string toolName,
        TimeSpan timeout,
        long startedAt,
        TimeProvider timeProvider,
        ILogger logger)
    {
        var elapsed = timeProvider.GetElapsedTime(startedAt);

        if (task.IsFaulted)
        {
            // A late FAILURE costs nothing and changes no accounting: the model
            // was already told the call failed, and it did.
            logger.LogWarning(
                task.Exception,
                "Tool '{ToolName}' faulted after {ElapsedSeconds:F1}s, past the {TimeoutSeconds:F1}s timeout already reported to the model.",
                toolName,
                elapsed.TotalSeconds,
                timeout.TotalSeconds);

            return;
        }

        if (task.IsCanceled)
        {
            // The cooperative path: the tool honoured the cancellation the
            // timeout requested and stopped. Nothing was produced, so there is
            // nothing to book.
            logger.LogDebug(
                "Tool '{ToolName}' stopped on the cancellation its {TimeoutSeconds:F1}s timeout requested.",
                toolName,
                timeout.TotalSeconds);

            return;
        }

        logger.LogInformation(
            "Tool '{ToolName}' finished after {ElapsedSeconds:F1}s, past the {TimeoutSeconds:F1}s timeout already reported to the model. " +
            "Its result and any usage it reported are recorded against the original call.",
            toolName,
            elapsed.TotalSeconds,
            timeout.TotalSeconds);

        if (scope is not { Writer: { } writer } || callId is not { Length: > 0 })
        {
            // A tool invoked outside a recorded run has no row to write onto.
            return;
        }

        // The accumulator entry was written by the tool's OWN body, after the
        // timeout, so ToolInvocationTracker.OnResult never saw it.
        var usage = scope.ToolUsage?.Take(callId);
        var result = ToolResultText.TryGetText(task.Result, out var text) ? text : null;

        if (usage is null && result is null)
        {
            // Nothing new to say. Writing the timestamp alone would claim a
            // measurement that does not exist.
            return;
        }

        _ = WriteAsync(
            writer,
            new LateToolCompletion
            {
                RunId = scope.RunId,
                ToolCallId = callId,
                LateCompletedAt = timeProvider.GetUtcNow(),
                Result = result,
                Usage = usage,
                Duration = elapsed,
                TenantId = scope.TenantId,
            },
            toolName,
            logger);
    }

    /// <summary>
    /// Performs the write and reports its outcome. Fire-and-forget by design:
    /// nothing is waiting on this call any more.
    /// </summary>
    private static async Task WriteAsync(
        RunEventWriter writer,
        LateToolCompletion completion,
        string toolName,
        ILogger logger)
    {
        // Observability never breaks functionality. The writer already
        // swallows a store failure; this guards the await itself.
        try
        {
            if (!await writer.CompleteLateToolInvocationAsync(completion).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "Tool '{ToolName}' finished late but its call record in run {RunId} was not found, so its usage was not recorded. " +
                    "The run may have been removed by retention before the call settled.",
                    toolName,
                    completion.RunId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Tool '{ToolName}' finished late and its usage could not be recorded against run {RunId}.",
                toolName,
                completion.RunId);
        }
    }
}
