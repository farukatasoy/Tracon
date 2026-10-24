using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Writes the events of a single run into <see cref="IRunStore"/> and
/// produces the sequence number.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The sequence number is produced by a single writer, one event at a
/// time.</strong> Numbering an event, writing it to the store and handing it to
/// every <see cref="IRunEventSink"/> happen as one step, and the next event
/// waits for it. A run writes from more than one place at once (concurrent tool
/// calls, a child run writing into its parent's writer, a workflow's executors
/// next to its event pump), so without that the store and the sinks could see
/// event 6 before event 5. This keeps the event order deterministic and
/// guarantees that live streaming and replay produce the same result.
/// </para>
/// <para>
/// A store or a sink therefore must not call <see cref="AppendAsync"/> on the
/// writer whose event it is handling: that append would wait for the very event
/// it is part of. From a sink, such an append throws
/// <see cref="InvalidOperationException"/> instead of waiting, and the sink is
/// disabled for the run.
/// </para>
/// <para>
/// <strong>Store failures do not interrupt the run.</strong> Observability
/// must not break functionality. Every write failure is logged and
/// swallowed; once the writer hits a failure, it moves into the
/// <see cref="IsDisabled"/> state and attempts no further writes for that run.
/// </para>
/// <para>
/// <strong>The loss is counted, not only logged.</strong> Every lost write
/// increments <see cref="TraconDiagnostics.RunRecordingFailureCounterName"/>,
/// tagged with the tenant and the stage it was lost at, so that a hole in a
/// run's evidence is something a dashboard shows rather than something a
/// reader of logs eventually notices.
/// </para>
/// <para>
/// Tracon creates one writer for each run. Code that runs inside a run, such as a
/// tool, reads it from <see cref="AgentRunScope.Writer"/> of <see cref="TraconRunContext.Current"/>
/// and appends its own events with <see cref="AppendAsync"/>.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The append gate is a SemaphoreSlim whose wait handle is never created; it holds no unmanaged resource, and making the per-run writer disposable would put a Dispose obligation on every caller of a public type.")]
public sealed class RunEventWriter
{
    private readonly IRunStore _store;
    private readonly TraconRunRecordingOptions _options;
    private readonly ILogger _logger;
    private readonly IReadOnlyList<IRunEventSink> _sinks;
    // Claimed with Interlocked, for the same reason _disabled is: two events can
    // reach the same sink at once (concurrent tool calls, and a child run writing
    // into the root run's writer), and both would otherwise see the sink as live,
    // both fail, and both count. A plain bool[] write also carries no barrier, so a
    // second thread could keep calling a sink that is already known to be broken.
    private readonly int[] _sinkDisabled;
    private readonly TraconMetrics? _metrics;

    // F-257: numbering, the store write and the sink dispatch of ONE event form
    // a single step. Without it two writers took 5 and 6, the second one's store
    // write and dispatch overtook the first one's, and a sink saw 6 before 5 -
    // against IRunEventSink's documented "in sequence order". Uncontended,
    // WaitAsync completes synchronously and allocates nothing, so the hot path
    // (one writer, one event at a time) pays one interlocked operation. No
    // kernel handle is ever created (AvailableWaitHandle is not read), so the
    // semaphore needs no disposal.
    private readonly SemaphoreSlim _appendGate = new(1, 1);

    // The writer whose sinks are being called on this flow. A sink that appends to
    // the run it is handling - directly, or by running an agent inline that writes
    // into the ambient run - would wait for itself on _appendGate and hang the run
    // for good; the append throws instead, and the sink is disabled like any
    // failing sink. Written only around a sink call (no cost without sinks); it
    // flows into the awaited sink and never back to the caller.
    private static readonly AsyncLocal<RunEventWriter?> DispatchingWriter = new();

    private long _sequence;

    // Backs IsDisabled. An int rather than a bool so that the transition can be
    // claimed exactly once: two writes racing into the same failure must log
    // twice (they are two different diagnostics) but count once, because the
    // counter measures runs that lost their record, not exceptions observed.
    private int _disabled;

    /// <summary>Creates a new writer.</summary>
    /// <param name="store">The store the events are written to.</param>
    /// <param name="options">The recording detail settings.</param>
    /// <param name="logger">The logger write failures are reported to.</param>
    /// <param name="runId">The run identity.</param>
    /// <param name="metrics">
    /// The metric set the lost writes are counted on.
    /// <see langword="null"/> disables counting and changes nothing else.
    /// </param>
    /// <param name="sinks">
    /// The observers to fan every event out to, in addition to <paramref name="store"/>.
    /// <see langword="null"/> or empty runs the identical hot path as before this
    /// extension point existed — no allocation, no branching difference.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    internal RunEventWriter(
        IRunStore store,
        TraconRunRecordingOptions options,
        ILogger logger,
        Guid runId,
        TraconMetrics? metrics,
        IReadOnlyList<IRunEventSink>? sinks = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _options = options;
        _logger = logger;
        RunId = runId;
        _metrics = metrics;
        _sinks = sinks is { Count: > 0 } ? sinks : [];
        _sinkDisabled = new int[_sinks.Count];
    }

    /// <summary>Gets the identity of the run this writer writes to.</summary>
    public Guid RunId { get; }

    /// <summary>
    /// Gets the tenant of the run this writer writes to. Stamped onto every sub-write
    /// as defense in depth.
    /// </summary>
    /// <remarks>
    /// The value is the run's own tenant, which Tracon stamps when it starts the
    /// run (<see cref="RunStartInfo.TenantId"/>) — NOT the <em>ambient</em> tenant.
    /// It can deliberately override the ambient tenant (this is how workflows and
    /// job queues work). A writer whose run was never started stays
    /// <see langword="null"/> and no tenant check is performed.
    /// </remarks>
    public string? TenantId { get; private set; }

    /// <summary>
    /// Gets whether the writer was disabled because it hit a store failure.
    /// A disabled writer silently does nothing.
    /// </summary>
    public bool IsDisabled => Volatile.Read(ref _disabled) != 0;

    /// <summary>Gets the number of events written.</summary>
    public long EventCount => Interlocked.Read(ref _sequence);

    /// <summary>Opens the run record and writes the first event.</summary>
    /// <param name="info">The start information.</param>
    /// <param name="query">
    /// The text of the first user message that triggered this run. It is the
    /// only source for production-to-eval case promotion:
    /// outside <c>run_events</c> the input text is not persisted anywhere,
    /// and the session is recorded only at the end of a SUCCESSFUL run (see
    /// <c>AgentEndpoints.AgentRunStream</c>) — so the query of a failed run
    /// can ONLY be read from here.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    internal async ValueTask StartAsync(RunStartInfo info, string? query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        // The run's own tenant. Every subsequent sub-write carries this (K-355).
        TenantId = info.TenantId;

        if (IsDisabled)
        {
            return;
        }

        try
        {
            await _store.StartRunAsync(info, cancellationToken).ConfigureAwait(false);

            // 🚨 A retried run keeps its identity: StartRunAsync UPSERTS the row
            // rather than opening a second one. The event stream has no such
            // upsert - it is append-only and (run_id, seq) is unique - so a second
            // attempt that restarted its numbering at zero collided with the first
            // attempt's events. The collision disabled this writer for the rest of
            // the run, and CompleteAsync then skipped CompleteRunAsync silently:
            // the work finished, the job said Completed, and the run stayed in
            // Running forever with no usage and no cost (HATA-S3-004). That is not
            // an edge case - it was the deterministic outcome of EVERY lease
            // takeover, which is the entire point of the feature.
            var lastSequence = await _store
                .GetLastEventSequenceAsync(info.RunId, cancellationToken)
                .ConfigureAwait(false);

            if (lastSequence is { } written)
            {
                _sequence = written + 1;
            }
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            Disable(ex, RunRecordingStages.Start);
            return;
        }

        await AppendAsync(new RunEventDraft(RunEventType.RunStarted) { Text = query }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a run that <see cref="StartAsync"/> opened <see cref="RunStatus.Queued"/> to the status in
    /// <paramref name="running"/>, once the process has what the run waited for.
    /// </summary>
    /// <param name="running">
    /// The start information the run was opened with, with <see cref="RunStartInfo.Status"/> changed.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// Writes the row again (<see cref="IRunStore.StartRunAsync"/> is an upsert) and appends no event: the
    /// stream already says the run started. A store failure is logged and ignored, and the writer stays
    /// usable: the row only shows <see cref="RunStatus.Queued"/> for a run that is already going, and the
    /// closing write at the end of the run still lands. Disabling the writer here would cost a finished run its
    /// closing write and leave its row to be closed as an orphan.
    /// </remarks>
    internal async ValueTask PromoteAsync(RunStartInfo running, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(running);

        if (IsDisabled)
        {
            return;
        }

        try
        {
            await _store.StartRunAsync(running, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            _logger.LogWarning(
                ex,
                "Run {RunId} has its concurrency slot but its record could not be moved from Queued to Running; the run continues and its record is closed at the end.",
                RunId);
        }
    }

    /// <summary>
    /// Writes this run's "still here" mark now, instead of at the next heartbeat cycle.
    /// </summary>
    /// <param name="at">The mark time (UTC).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// For a run opened <see cref="RunStatus.Queued"/> in the process that will run it: the reconciliation
    /// scan closes a <c>Queued</c> row only when it has a heartbeat, so a process that stops before its first
    /// cycle would otherwise leave the row queued for good. A failure is logged and ignored; the cycle
    /// that follows writes the mark again, and the writer stays usable.
    /// </remarks>
    internal async ValueTask TouchHeartbeatAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (IsDisabled)
        {
            return;
        }

        try
        {
            await _store.TouchHeartbeatAsync([RunId], at, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "The first heartbeat of run {RunId} could not be written; the next cycle writes it.", RunId);
        }
    }

    /// <summary>Appends an event to the stream and assigns its sequence number.</summary>
    /// <param name="draft">The event draft.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The event with its sequence number and timestamp assigned. The event
    /// is <strong>produced</strong> even if the store fails or the writer is
    /// disabled.
    /// </returns>
    /// <remarks>
    /// Returning the event is for workflow execution: the streaming endpoint
    /// must both write the same event to the store and send it to the client,
    /// and building it a second time would split the sequence number in two.
    /// Returning a value from a disabled writer is also deliberate — closing
    /// observability must not interrupt the response streaming to the client.
    /// </remarks>
    public ValueTask<RunEvent> AppendAsync(RunEventDraft draft, CancellationToken cancellationToken = default)
        => AppendCoreAsync(draft, allowReserved: false, cancellationToken);

    /// <summary>
    /// Appends an event whose <see cref="RunEventDraft.CustomType"/> falls
    /// under <see cref="RunEventCustomTypes.ReservedPrefix"/> — the one check
    /// <see cref="AppendAsync"/> exists to enforce against every OTHER
    /// caller. <c>internal</c> so only Tracon's own code (today: the
    /// quota threshold notice) can reach it; a consumer only ever sees the
    /// public, reserved-rejecting <see cref="AppendAsync"/>.
    /// </summary>
    /// <param name="draft">The event draft. <see cref="RunEventDraft.CustomType"/> must still be a valid shape.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The event with its sequence number and timestamp assigned.</returns>
    internal ValueTask<RunEvent> AppendReservedAsync(RunEventDraft draft, CancellationToken cancellationToken = default)
        => AppendCoreAsync(draft, allowReserved: true, cancellationToken);

    private async ValueTask<RunEvent> AppendCoreAsync(RunEventDraft draft, bool allowReserved, CancellationToken cancellationToken)
    {
        ValidateCustomType(draft, allowReserved);

        if (ReferenceEquals(DispatchingWriter.Value, this))
        {
            throw new InvalidOperationException(
                "An IRunEventSink cannot append to the run whose event it is handling: the run's events are " +
                "written one at a time, and this append would wait for the event the sink is still handling.");
        }

        // The caller's token, so a cancelled caller stops waiting just as its store
        // write would have stopped. A closing event after a cancellation is written
        // on CancellationToken.None and still takes its turn.
        await _appendGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await AppendInOrderAsync(draft, allowReserved, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _appendGate.Release();
        }
    }

    /// <summary>Numbers, stores and dispatches one event; runs under the append gate only.</summary>
    private async ValueTask<RunEvent> AppendInOrderAsync(RunEventDraft draft, bool allowReserved, CancellationToken cancellationToken)
    {
        var runEvent = new RunEvent
        {
            RunId = RunId,
            Sequence = Interlocked.Increment(ref _sequence) - 1,
            Type = draft.Type,
            Timestamp = DateTimeOffset.UtcNow,
            Text = Truncate(draft.Text),
            ToolName = draft.ToolName,
            ToolCallId = draft.ToolCallId,
            CustomType = draft.CustomType,
            // 🚨 A pending human request is a DELIBERATE exception to this
            // rule. Here the payload is not an observability detail, it is
            // the function itself: the UI reads pending requests only from
            // this event, and if the payload were suppressed the user would
            // never see the question they need to answer. The same
            // rationale was established in K-089 (a script that cannot be
            // written to the audit trail does not run). The quota threshold
            // notice (phase 146) is the SAME exception: NoticeId is the
            // dedup key a client reads to suppress a repeated warning, not
            // an observability detail, and it exists only inside this
            // reserved-prefix event's own payload -- suppressing it would
            // leave the client an unlabeled "custom" frame it cannot act on.
            //
            // The request payload is still CUT at MaxPayloadLength like any
            // other, so its reader must not need the whole JSON: the request
            // id is read from Text, and the prompt - written last - is the only
            // field a cut can reach (F-327, WorkflowRequestDescriptor.Parse).
            //
            // Phase 208: an answer's run names the run it answered only here
            // (WorkflowResponseApplied), so that link is the function too. It
            // carries two ids, never the answer itself.
            //
            // Phase 223: a reserved type is exempt only when it is named in
            // KeepsPayloadWithPayloadsOff. The two content notices carry
            // provider text and workflow data, which are observability
            // details, so they follow the option.
            Payload = _options.RecordToolPayloads ||
                      draft.Type == RunEventType.WorkflowRequest ||
                      draft.Type == RunEventType.WorkflowResponseApplied ||
                      (allowReserved && draft.Type == RunEventType.Custom && KeepsPayloadWithPayloadsOff(draft.CustomType))
                ? Truncate(draft.Payload)
                : null,

            // Expected tenant stamp (K-355).
            TenantId = TenantId,
        };

        if (!IsDisabled)
        {
            try
            {
                await _store.AppendEventAsync(runEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
            {
                Disable(ex, RunRecordingStages.Event);
            }
        }

        // Sink dispatch is INDEPENDENT of the store: a sink failure never
        // disables the store, and a store failure never skips the sinks —
        // observability targets do not share a single point of failure.
        await DispatchToSinksAsync(runEvent, cancellationToken).ConfigureAwait(false);

        return runEvent;
    }

    /// <summary>
    /// The reserved <c>Custom</c> types whose payload is written with
    /// <c>RecordToolPayloads</c> off: the quota threshold notice (its
    /// <c>NoticeId</c> is the dedup key a client acts on), the output
    /// truncation notice (provider, cause and limit, never the answer) and the
    /// tool input decline notice (server, tool and a reason code, never a field). A new
    /// reserved type is NOT exempt until it is named here.
    /// </summary>
    private static bool KeepsPayloadWithPayloadsOff(string? customType)
        => customType is RunEventCustomTypes.QuotaThreshold or RunEventCustomTypes.OutputTruncated or RunEventCustomTypes.ToolInputDeclined;

    /// <summary>Fans the event out to every registered, not-yet-failed sink.</summary>
    private async ValueTask DispatchToSinksAsync(RunEvent runEvent, CancellationToken cancellationToken)
    {
        // K1: no sink registered ⇒ this is the only cost paid on the hot path.
        if (_sinks.Count == 0)
        {
            return;
        }

        // Flows into every awaited sink call below; undone when this method returns.
        DispatchingWriter.Value = this;

        for (var i = 0; i < _sinks.Count; i++)
        {
            if (Volatile.Read(ref _sinkDisabled[i]) != 0)
            {
                continue;
            }

            try
            {
                await _sinks[i].OnEventAsync(runEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
            {
                // Disabled for THIS run only — the sink instance is shared
                // across every concurrent run, and the field lives on this
                // (per-run) writer instance.
                //
                // Counted separately from the store: a sink that drops events loses
                // the same evidence. Only the caller that claims the transition
                // counts and logs, which is what keeps it to one measurement per
                // sink per run even when several events fail into it at once.
                if (Interlocked.Exchange(ref _sinkDisabled[i], 1) != 0)
                {
                    continue;
                }

                _metrics?.RecordRunRecordingFailure(TenantId, RunRecordingStages.Sink);

                _logger.LogWarning(
                    ex,
                    "Tracon run event sink {SinkType} was disabled for run {RunId} after a failure. The run continues normally.",
                    _sinks[i].GetType().Name,
                    RunId);
            }
        }
    }

    /// <summary>
    /// Records a completed tool invocation.
    /// </summary>
    /// <param name="invocation">The invocation summary.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// Separate from the event stream: events <em>narrate</em> the
    /// invocation, this record <em>measures</em> it. Its failure is swallowed
    /// the same way as an event write.
    /// </remarks>
    internal async ValueTask RecordToolInvocationAsync(
        ToolInvocationRecord invocation,
        CancellationToken cancellationToken = default)
    {
        if (IsDisabled)
        {
            return;
        }

        try
        {
            await _store.RecordToolInvocationAsync(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            Disable(ex, RunRecordingStages.ToolInvocation);
        }
    }

    /// <summary>
    /// Records one non-token item a workflow function node reported.
    /// </summary>
    /// <param name="record">The item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// Written the moment the node reports it and not cancelled with the run: the work
    /// was done and the provider billed it, so a run that is cancelled one line later
    /// keeps the item. Its failure is swallowed the same way as an event write.
    /// </remarks>
    internal async ValueTask RecordNonTokenUsageAsync(
        NonTokenUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        if (IsDisabled)
        {
            return;
        }

        try
        {
            await _store.RecordNonTokenUsageAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            Disable(ex, RunRecordingStages.NonTokenUsage);
        }
    }

    /// <summary>Logs an item a function node reported that cannot be recorded.</summary>
    /// <param name="nodeName">The reporting node, or <see langword="null"/> when unknown.</param>
    /// <param name="reason">Why the item was refused.</param>
    internal void LogNonTokenUsageRefused(string? nodeName, string reason)
        => _logger.LogWarning(
            "Run {RunId} refused a non-token usage item reported by node {Node}: {Reason} The item is not recorded and not counted.",
            RunId,
            nodeName ?? "(none)",
            reason);

    /// <summary>Logs an item a function node reported after its run's total was already read.</summary>
    /// <param name="nodeName">The reporting node, or <see langword="null"/> when unknown.</param>
    internal void LogNonTokenUsageLate(string? nodeName)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Node {Node} of run {RunId} reported a non-token usage item after the run's total was written. " +
                "The item is kept in the run's detail; the run's total, its quota entry and its budget do not include it.",
                nodeName ?? "(none)",
                RunId);
        }
    }

    /// <summary>
    /// Writes the settled outcome of a call whose timeout had already been
    /// reported onto that call's existing record.
    /// </summary>
    /// <param name="completion">The settled outcome.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when a record was updated.</returns>
    /// <remarks>
    /// This runs AFTER the run has been completed, on the continuation of a
    /// call the timeout stopped waiting for. It takes no cancellation from the
    /// run: the run's own token is already cancelled by the time a late call
    /// settles, and honouring it would throw the measurement away exactly when
    /// it matters. Its failure is swallowed like every other recording write.
    /// </remarks>
    internal async ValueTask<bool> CompleteLateToolInvocationAsync(
        LateToolCompletion completion,
        CancellationToken cancellationToken = default)
    {
        if (IsDisabled)
        {
            return false;
        }

        try
        {
            return await _store
                .CompleteLateToolInvocationAsync(completion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            Disable(ex, RunRecordingStages.ToolInvocation);

            return false;
        }
    }

    /// <summary>Terminates the run.</summary>
    /// <param name="status">The final status.</param>
    /// <param name="usage">The token usage.</param>
    /// <param name="error">The error information.</param>
    /// <param name="cost">The computed cost. <see langword="null"/> if the model is unknown.</param>
    /// <param name="nonTokenCost">
    /// The non-token cost the run reported. <see langword="null"/> if it reported none.
    /// </param>
    /// <param name="modelId">
    /// The model that actually answered, overriding <c>runs.model_id</c> when
    /// a <see cref="ModelBinding.Fallbacks"/> link stood in for the primary
    /// binding. <see langword="null"/> leaves the value
    /// <see cref="StartAsync"/> already wrote unchanged — the overwhelmingly
    /// common case.
    /// </param>
    /// <param name="modelProvider">
    /// The provider of <paramref name="modelId"/>, overriding
    /// <c>runs.model_provider</c> the same way <paramref name="modelId"/>
    /// overrides <c>runs.model_id</c>. <see langword="null"/> leaves the
    /// stored value unchanged.
    /// </param>
    /// <param name="closingEventPayload">
    /// The <c>Payload</c> written on the closing event when <paramref name="status"/> is
    /// <see cref="RunStatus.AwaitingInput"/> or <see cref="RunStatus.AwaitingApproval"/>.
    /// Ignored for every other status. See <see cref="RunEventType.RunAwaitingInput"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    internal async ValueTask CompleteAsync(
        RunStatus status,
        RunUsage? usage = null,
        RunError? error = null,
        RunCost? cost = null,
        RunNonTokenCost? nonTokenCost = null,
        string? modelId = null,
        string? modelProvider = null,
        string? closingEventPayload = null,
        CancellationToken cancellationToken = default)
    {
        // 🚨 NOT gated on IsDisabled here (unlike every other method on this
        // type): IsDisabled tracks the STORE only, and AppendAsync below still
        // dispatches the closing event to every registered sink even when the
        // store already failed earlier in this run — a sink must never miss
        // the terminal event just because the store did (docs/70).
        var closingEvent = status switch
        {
            RunStatus.Completed => new RunEventDraft(RunEventType.RunCompleted),
            RunStatus.Failed => new RunEventDraft(RunEventType.RunFailed) { Text = error?.Message },

            // A run awaiting human input or approval is neither finished nor
            // failed; writing RunFailed would show a red error in the UI.
            // AwaitingApproval was previously missing from this pattern and
            // fell to the default branch below, which announced every
            // approval-required run as canceled.
            RunStatus.AwaitingInput or RunStatus.AwaitingApproval
                => new RunEventDraft(RunEventType.RunAwaitingInput) { Payload = closingEventPayload },
            _ => new RunEventDraft(RunEventType.RunFailed) { Text = "The run was canceled." },
        };

        await AppendAsync(closingEvent, cancellationToken).ConfigureAwait(false);

        if (IsDisabled)
        {
            return;
        }

        try
        {
            await _store.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = RunId,
                    Status = status,
                    CompletedAt = DateTimeOffset.UtcNow,
                    EventCount = EventCount,
                    Usage = usage,
                    Error = error,
                    Cost = cost,
                    NonTokenCost = nonTokenCost,
                    ModelId = modelId,
                    ModelProvider = modelProvider,

                    // Expected tenant stamp (K-355).
                    TenantId = TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            Disable(ex, RunRecordingStages.Completion);
        }
    }

    /// <summary>
    /// Enforces the two-way rule between <see cref="RunEventDraft.Type"/> and
    /// <see cref="RunEventDraft.CustomType"/>: required and valid when
    /// <see cref="RunEventType.Custom"/>, <see langword="null"/> otherwise.
    /// </summary>
    /// <exception cref="ArgumentException">The rule is violated.</exception>
    /// <remarks>
    /// A one-way check would let a caller write <c>CustomType</c> on a
    /// non-<see cref="RunEventType.Custom"/> event and believe it was
    /// carried — every store silently drops it, since only <see
    /// cref="RunEventType.Custom"/> gives it any meaning.
    /// </remarks>
    private static void ValidateCustomType(RunEventDraft draft, bool allowReserved)
    {
        if (draft.Type != RunEventType.Custom)
        {
            if (draft.CustomType is not null)
            {
                throw new ArgumentException(
                    "RunEventDraft.CustomType must be null unless Type is RunEventType.Custom.",
                    nameof(draft));
            }

            return;
        }

        if (!RunEventCustomTypes.IsValidType(draft.CustomType))
        {
            throw new ArgumentException(
                "RunEventDraft.CustomType is required when Type is RunEventType.Custom, and must be " +
                "1-128 characters of lowercase ASCII letters, digits, '.', '_', or '-'.",
                nameof(draft));
        }

        if (!allowReserved && RunEventCustomTypes.IsReserved(draft.CustomType))
        {
            throw new ArgumentException(
                $"RunEventDraft.CustomType cannot start with the reserved prefix " +
                $"'{RunEventCustomTypes.ReservedPrefix}'.",
                nameof(draft));
        }
    }

    /// <summary>
    /// What an event text or payload cut at
    /// <see cref="TraconRunRecordingOptions.MaxPayloadLength"/> ends with.
    /// </summary>
    /// <remarks>
    /// A reader that takes an event text as content checks for it rather than
    /// using a cut text as if it were whole: promotion reads the query from the
    /// <c>RunStarted</c> text when no input was recorded.
    /// </remarks>
    internal const string TruncationMarker = "…[truncated]";

    private string? Truncate(string? value)
    {
        if (value is null || _options.MaxPayloadLength <= 0 || value.Length <= _options.MaxPayloadLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, _options.MaxPayloadLength), TruncationMarker);
    }

    /// <summary>Gives up on the store for this run and records that the evidence was lost.</summary>
    /// <param name="exception">The failure that ended recording.</param>
    /// <param name="stage">The <see cref="RunRecordingStages"/> value the write was lost at.</param>
    /// <remarks>
    /// Nothing in here may throw. This runs inside the catch block whose entire
    /// purpose is that a recording failure does not interrupt the run, so counting
    /// and logging are the only work done.
    /// </remarks>
    private void Disable(Exception exception, string stage)
    {
        // Only the first caller counts; see the _disabled field.
        if (Interlocked.Exchange(ref _disabled, 1) == 0)
        {
            _metrics?.RecordRunRecordingFailure(TenantId, stage);
        }

        _logger.LogWarning(
            exception,
            "Tracon run recording was disabled ({Reason}). Run {RunId} continues normally.",
            RunRecordingStages.Describe(stage),
            RunId);
    }
}

/// <summary>
/// Represents the information an event carries before its sequence number and
/// timestamp are assigned.
/// </summary>
/// <param name="Type">The event type.</param>
public readonly record struct RunEventDraft(RunEventType Type)
{
    /// <summary>Gets the text content.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the tool name.</summary>
    public string? ToolName { get; init; }

    /// <summary>Gets the tool call identity.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>Gets the free-form JSON payload.</summary>
    public string? Payload { get; init; }

    /// <summary>
    /// Gets the namespaced type that qualifies a <see cref="RunEventType.Custom"/>
    /// event.
    /// </summary>
    /// <remarks>
    /// Required when <see cref="Type"/> is <see cref="RunEventType.Custom"/>;
    /// must be <see langword="null"/> otherwise. 1-128 characters: lowercase
    /// ASCII letters, digits, <c>.</c>, <c>_</c>, or <c>-</c>, starting with a
    /// letter or digit — the same shape as a job handler key. The
    /// <c>"tracon."</c> prefix is reserved; pick your own namespace
    /// (e.g. <c>"contoso.preview-ready"</c>). <see cref="RunEventWriter.AppendAsync"/>
    /// throws <see cref="ArgumentException"/> for a draft that violates either rule.
    /// </remarks>
    public string? CustomType { get; init; }
}
