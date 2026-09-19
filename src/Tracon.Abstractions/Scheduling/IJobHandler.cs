namespace Tracon;

/// <summary>
/// The extension point that provides job execution logic for one handler key.
/// </summary>
/// <remarks>
/// <para>
/// Registered with <c>AddJobHandler&lt;T&gt;(handlerKey)</c>: the key lives on
/// the registration, not on the type, so the same type can serve two keys and
/// a duplicate key is caught at startup. The background worker looks the key
/// up by exact, ordinal match, so <strong>registration order never decides
/// which handler runs</strong> — a consumer handler cannot shadow a built-in
/// one, and a built-in one cannot shadow a consumer's.
/// </para>
/// <para>
/// <strong>Lifetime:</strong> the handler is registered <c>scoped</c> and
/// resolved from a fresh dependency-injection scope per execution. Scoped
/// dependencies are therefore safe to take in the constructor: two jobs
/// running in parallel, and two attempts of the same job, never share an
/// instance.
/// </para>
/// <para>
/// <strong>Tenant:</strong> runs under the <c>ambient tenant</c> — the worker
/// opens the job's own tenant scope before calling
/// <see cref="ExecuteAsync"/>, so an <c>ITenantContext</c> the handler
/// resolves reports <see cref="JobRecord.TenantId"/>, not the process default.
/// The record is also on <see cref="JobContext.Job"/> for a handler that
/// prefers to read it explicitly.
/// </para>
/// <example>
/// <code>
/// builder.Services.AddJobHandler&lt;NightlyReportJobHandler&gt;("contoso.nightly-report");
/// </code>
/// </example>
/// </remarks>
public interface IJobHandler
{
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
    /// <see cref="JobStatus.Failed"/>; if the process crashes or its lease
    /// lapses, another worker (or the same one) re-leases the SAME job and
    /// calls <see cref="ExecuteAsync"/> again from scratch. Neither case
    /// resets item progress.
    /// </para>
    /// <para>
    /// <strong>A running handler is never interrupted by its own lease.</strong>
    /// The worker renews the lease on a timer for as long as
    /// <see cref="ExecuteAsync"/> has not returned, and it renews without a
    /// limit: there is no setting that caps how long one job may run. A
    /// handler that blocks forever therefore holds its worker slot forever,
    /// and that slot counts against the worker's concurrency limit, so no
    /// replica can take the work instead. Give any handler that calls out to
    /// the network its own timeout — the token this method receives is
    /// cancelled only when the worker shuts down.
    /// </para>
    /// <para>
    /// Because of this, a handler with side effects (sending an email,
    /// charging a payment, calling an external API) MUST be idempotent, or
    /// MUST check <see cref="JobItemRecord.Status"/> itself and skip any item
    /// that is not <see cref="JobItemStatus.Pending"/> — see
    /// <see cref="JobContext.Items"/>. The handlers Tracon ships all do
    /// the latter, and the reusable <c>JobHandlerContract</c> in
    /// <c>Tracon.Testing.Contracts.Xunit</c> asserts it.
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
/// up a fake store in a unit test. It carries no service provider either:
/// the handler is built by the container from its own per-execution scope, so
/// its dependencies arrive through its constructor.
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
