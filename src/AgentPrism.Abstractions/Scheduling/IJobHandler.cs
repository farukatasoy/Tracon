namespace AgentPrism;

/// <summary>
/// The extension point that provides job execution logic for a specific
/// <see cref="JobKind"/>.
/// </summary>
/// <remarks>
/// Registered with <c>AddJobHandler&lt;T&gt;()</c>. AgentPrism.Core provides
/// two implementations (<see cref="JobKind.AgentBatch"/>,
/// <see cref="JobKind.Workflow"/>); Phase 18 (eval) adds its own handler
/// (<see cref="JobKind.Eval"/>) the same way. The background worker picks
/// among the registered handlers by the <see cref="Kind"/> field.
/// </remarks>
public interface IJobHandler
{
    /// <summary>The job kind this handler can execute.</summary>
    JobKind Kind { get; }

    /// <summary>Executes the job.</summary>
    /// <param name="context">The job context: record, items, reporting, and cancellation check.</param>
    /// <param name="cancellationToken">The cancellation token (triggered when the worker shuts down).</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// If the handler throws, the job is retried with
    /// <see cref="IJobStore.ReleaseForRetryAsync"/> (if the attempt limit is
    /// not exceeded) or marked as <see cref="JobStatus.Failed"/>.
    /// </remarks>
    ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// The context available during an <see cref="IJobHandler"/> execution.
/// </summary>
/// <remarks>
/// The handler does not access <see cref="IJobStore"/> directly; this keeps
/// the handler focused only on execution logic and removes the need to set
/// up a fake store in a unit test.
/// </remarks>
public sealed class JobContext
{
    /// <summary>The record of the job being executed.</summary>
    public required JobRecord Job { get; init; }

    /// <summary>The job's items, by sequence number.</summary>
    public required IReadOnlyList<JobItemRecord> Items { get; init; }

    /// <summary>
    /// Reports an item's processing result. The handler must call this as
    /// each item is processed; the result is reflected into the persistent
    /// store and the job's counters.
    /// </summary>
    public required Func<JobItemResult, CancellationToken, ValueTask> ReportItemAsync { get; init; }

    /// <summary>
    /// Checks whether the job was cancelled by an external request
    /// (<c>POST .../cancel</c>). The handler supports cooperative
    /// cancellation by checking this between items.
    /// </summary>
    public required Func<CancellationToken, ValueTask<bool>> IsCancelledAsync { get; init; }
}
