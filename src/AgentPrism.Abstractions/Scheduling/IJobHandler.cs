namespace AgentPrism;

/// <summary>
/// The extension point that provides job execution logic for a specific
/// <see cref="JobKind"/>.
/// </summary>
/// <remarks>
/// Registered with <c>AddJobHandler&lt;T&gt;()</c>. AgentPrism.Core provides
/// two implementations (<see cref="JobKind.AgentBatch"/>,
/// <see cref="JobKind.Workflow"/>); evaluation adds its own handler
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
    /// <para>
    /// <strong>Execution is at-least-once, not exactly-once.</strong> The
    /// same job — the same <see cref="JobContext.Job"/>, with the SAME
    /// unfiltered <see cref="JobContext.Items"/> list — can reach
    /// <see cref="ExecuteAsync"/> more than once: if the handler throws, the
    /// job is retried with <see cref="IJobStore.ReleaseForRetryAsync"/> (if
    /// the attempt limit is not exceeded) or marked as
    /// <see cref="JobStatus.Failed"/>; if the process crashes or the lease
    /// simply expires before the handler returns, another worker (or the
    /// same one) re-leases the SAME job and calls <see cref="ExecuteAsync"/>
    /// again from scratch. Neither case resets item progress.
    /// </para>
    /// <para>
    /// Because of this, a handler with side effects (sending an email,
    /// charging a payment, calling an external API) MUST be idempotent, or
    /// MUST check <see cref="JobItemRecord.Status"/> itself and skip any item
    /// that is not <see cref="JobItemStatus.Pending"/> — see
    /// <see cref="JobContext.Items"/>. The three handlers AgentPrism ships
    /// (<see cref="JobKind.AgentBatch"/>, <see cref="JobKind.Workflow"/>,
    /// <see cref="JobKind.Eval"/>) all do the latter, and the reusable
    /// <c>JobHandlerContract</c> in <c>AgentPrism.Testing.Contracts.Xunit</c>
    /// asserts it.
    /// </para>
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

    /// <summary>
    /// The job's items, by sequence number. Carries EVERY item, regardless of
    /// <see cref="JobItemRecord.Status"/> — NOT filtered down to
    /// <see cref="JobItemStatus.Pending"/> ones. On a retry (see
    /// <see cref="IJobHandler.ExecuteAsync"/>'s remarks), items already
    /// <see cref="JobItemStatus.Completed"/> or <see cref="JobItemStatus.Failed"/>
    /// from an earlier attempt are present here too; the handler is
    /// responsible for skipping them.
    /// </summary>
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
