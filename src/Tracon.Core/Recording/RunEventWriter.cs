using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Writes the events of a single run into <see cref="IRunStore"/> and
/// produces the sequence number.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The sequence number is produced by a single writer.</strong> This
/// keeps the event order deterministic and guarantees that live streaming and
/// replay produce the same result.
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
/// </remarks>
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
    public RunEventWriter(
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
    /// The value is taken from the <see cref="RunStartInfo.TenantId"/> field in <see
    /// cref="StartAsync"/> — NOT from the <em>ambient</em> tenant. This is the run's
    /// own tenant; it can deliberately override the ambient tenant (this is how
    /// workflows and job queues work). A writer used without calling <see
    /// cref="StartAsync"/> stays <see langword="null"/> and no tenant check is
    /// performed.
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
    public async ValueTask StartAsync(RunStartInfo info, string? query, CancellationToken cancellationToken = default)
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disable(ex, RunRecordingStages.Start);
            return;
        }

        await AppendAsync(new RunEventDraft(RunEventType.RunStarted) { Text = query }, cancellationToken).ConfigureAwait(false);
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
            Payload = _options.RecordToolPayloads ||
                      draft.Type == RunEventType.WorkflowRequest ||
                      (allowReserved && draft.Type == RunEventType.Custom)
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
            catch (Exception ex) when (ex is not OperationCanceledException)
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

    /// <summary>Fans the event out to every registered, not-yet-failed sink.</summary>
    private async ValueTask DispatchToSinksAsync(RunEvent runEvent, CancellationToken cancellationToken)
    {
        // K1: no sink registered ⇒ this is the only cost paid on the hot path.
        if (_sinks.Count == 0)
        {
            return;
        }

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
            catch (Exception ex) when (ex is not OperationCanceledException)
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
    public async ValueTask RecordToolInvocationAsync(
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disable(ex, RunRecordingStages.ToolInvocation);
        }
    }

    /// <summary>Terminates the run.</summary>
    /// <param name="status">The final status.</param>
    /// <param name="usage">The token usage.</param>
    /// <param name="error">The error information.</param>
    /// <param name="cost">The computed cost. <see langword="null"/> if the model is unknown.</param>
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
    public async ValueTask CompleteAsync(
        RunStatus status,
        RunUsage? usage = null,
        RunError? error = null,
        RunCost? cost = null,
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
                    ModelId = modelId,
                    ModelProvider = modelProvider,

                    // Expected tenant stamp (K-355).
                    TenantId = TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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

    private string? Truncate(string? value)
    {
        if (value is null || _options.MaxPayloadLength <= 0 || value.Length <= _options.MaxPayloadLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, _options.MaxPayloadLength), "…[truncated]");
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
