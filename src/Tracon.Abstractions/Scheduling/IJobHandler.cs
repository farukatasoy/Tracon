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
/// builder.AddTracon()
///        .AddJobHandler&lt;NightlyReportJobHandler&gt;("contoso.nightly-report");
/// </code>
/// </example>
/// </remarks>
public interface IJobHandler
{
    /// <summary>Executes the job.</summary>
    /// <param name="context">The job context: record, items, reporting, and cancellation check.</param>
    /// <param name="cancellationToken">
    /// The cancellation token. Cancelled when the worker shuts down, when the job is
    /// cancelled (<c>POST /api/jobs/{id}/cancel</c>), and when the attempt reaches the
    /// job's time limit.
    /// </param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Stop when the token is cancelled.</strong> The worker cancels it for
    /// three reasons and tells them apart itself:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// The worker is shutting down: the job is left to its lease and is leased again
    /// later, as after a crash.
    /// </item>
    /// <item>
    /// The job was cancelled: the worker reads the job's status every
    /// <c>TraconSchedulingOptions.PollInterval</c> (ten seconds by default) while the
    /// handler runs, and cancels the token when it finds the job
    /// <see cref="JobStatus.Cancelled"/> or gone. The job stays cancelled whatever the
    /// handler does next.
    /// </item>
    /// <item>
    /// The attempt ran past <see cref="JobRecord.MaxDuration"/> (or
    /// <c>TraconSchedulingOptions.MaxDurationByHandlerKey</c>): a handler that throws
    /// then fails the attempt with <see cref="JobErrorCodes.MaxDurationExceeded"/>,
    /// and the job is leased again while attempts are left. One that returns normally
    /// completes the job.
    /// </item>
    /// </list>
    /// <para>
    /// The token is cooperative: a handler that never reads it is not stopped. Its
    /// worker waits for it, and its slot stays taken until it returns.
    /// </para>
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
    /// resets item progress. A store error after <see cref="ExecuteAsync"/>
    /// has returned is not a handler failure: the worker tries the write of
    /// the outcome again while it still holds the lease. Only a write that
    /// keeps failing until then leaves the job to the lease-lapse case above.
    /// </para>
    /// <para>
    /// <strong>A running handler is never interrupted by its own lease.</strong>
    /// The worker renews the lease on a timer for as long as
    /// <see cref="ExecuteAsync"/> has not returned, and it renews without a
    /// limit. A job with no time limit (<see cref="JobRecord.MaxDuration"/>
    /// unset, and no entry for its key in
    /// <c>TraconSchedulingOptions.MaxDurationByHandlerKey</c>) can therefore run
    /// for ever, and a handler that ignores its token holds its worker slot even
    /// past its limit. That slot counts against the worker's concurrency limit,
    /// so no replica can take the work instead. Give any handler that calls out
    /// to the network its own timeout as well.
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
    /// <remarks>
    /// The built-in worker also cancels the handler's token within one
    /// <c>TraconSchedulingOptions.PollInterval</c> of the cancel request, so a
    /// handler that reads its token stops without asking. This check is the
    /// immediate answer, read from the store.
    /// </remarks>
    public required Func<CancellationToken, ValueTask<bool>> IsCancelledAsync { get; init; }

    /// <summary>
    /// Reads the time left on the lease the worker can prove it holds, less
    /// the worker's safety margin. Zero or negative once that point is reached.
    /// </summary>
    /// <remarks>
    /// Internal on purpose: the built-in worker sets it for Tracon's own
    /// handlers, so a store write that belongs to work already done (a queued
    /// run's session) can be tried again without outliving the lease.
    /// <see langword="null"/> when the worker did not build the context; such a
    /// write is then tried once.
    /// </remarks>
    internal Func<TimeSpan>? ProvableLeaseRemaining { get; init; }

    /// <summary>
    /// Says why the handler's token was cancelled: the worker stopping, the job
    /// cancelled, or the attempt's time limit.
    /// </summary>
    /// <remarks>
    /// Internal on purpose, like <see cref="ProvableLeaseRemaining"/>: the built-in
    /// workflow handlers end the work differently for each reason. A consumer's handler
    /// needs no answer, because the worker writes the job's outcome for each reason
    /// itself. <see langword="null"/> when the worker did not build the context; a
    /// cancelled token then means the worker is stopping, as it always did.
    /// </remarks>
    internal JobAttemptSignals? Signals { get; init; }
}
