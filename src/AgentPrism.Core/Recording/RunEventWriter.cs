using Microsoft.Extensions.Logging;

namespace AgentPrism;

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
/// </remarks>
public sealed class RunEventWriter
{
    private readonly IRunStore _store;
    private readonly AgentPrismRunRecordingOptions _options;
    private readonly ILogger _logger;
    private readonly IReadOnlyList<IRunEventSink> _sinks;
    private readonly bool[] _sinkDisabled;
    private long _sequence;

    /// <summary>Creates a new writer.</summary>
    /// <param name="store">The store the events are written to.</param>
    /// <param name="options">The recording detail settings.</param>
    /// <param name="logger">The logger write failures are reported to.</param>
    /// <param name="runId">The run identity.</param>
    /// <param name="sinks">
    /// The observers to fan every event out to, in addition to <paramref name="store"/>.
    /// <see langword="null"/> or empty runs the identical hot path as before this
    /// extension point existed (K1) — no allocation, no branching difference.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public RunEventWriter(
        IRunStore store,
        AgentPrismRunRecordingOptions options,
        ILogger logger,
        Guid runId,
        IReadOnlyList<IRunEventSink>? sinks = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _options = options;
        _logger = logger;
        RunId = runId;
        _sinks = sinks is { Count: > 0 } ? sinks : [];
        _sinkDisabled = new bool[_sinks.Count];
    }

    /// <summary>Gets the identity of the run this writer writes to.</summary>
    public Guid RunId { get; }

    /// <summary>
    /// Gets the tenant of the run this writer writes to. Stamped onto every
    /// sub-write as defense in depth.
    /// </summary>
    /// <remarks>
    /// 🚨 The value is taken from the <see cref="RunStartInfo.TenantId"/> field
    /// in <see cref="StartAsync"/> — NOT from the <em>ambient</em> tenant. This
    /// is the run's own tenant; it can deliberately override the ambient
    /// tenant (this is how workflows and job queues work). A writer used
    /// without calling <see cref="StartAsync"/> stays <see langword="null"/>
    /// and no tenant check is performed. Rationale: K-355.
    /// </remarks>
    public string? TenantId { get; private set; }

    /// <summary>
    /// Gets whether the writer was disabled because it hit a store failure.
    /// A disabled writer silently does nothing.
    /// </summary>
    public bool IsDisabled { get; private set; }

    /// <summary>Gets the number of events written.</summary>
    public long EventCount => Interlocked.Read(ref _sequence);

    /// <summary>Opens the run record and writes the first event.</summary>
    /// <param name="info">The start information.</param>
    /// <param name="query">
    /// The text of the first user message that triggered this run. It is the
    /// only source for production-to-eval case promotion (Phase 45, F-53):
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
            Disable(ex, "failed to open run record");
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
    public async ValueTask<RunEvent> AppendAsync(RunEventDraft draft, CancellationToken cancellationToken = default)
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
            // 🚨 A pending human request is a DELIBERATE exception to this
            // rule. Here the payload is not an observability detail, it is
            // the function itself: the UI reads pending requests only from
            // this event, and if the payload were suppressed the user would
            // never see the question they need to answer. The same
            // rationale was established in K-089 (a script that cannot be
            // written to the audit trail does not run).
            Payload = _options.RecordToolPayloads || draft.Type == RunEventType.WorkflowRequest
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
                Disable(ex, "failed to write run event");
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
            if (_sinkDisabled[i])
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
                _sinkDisabled[i] = true;

                _logger.LogWarning(
                    ex,
                    "AgentPrism run event sink {SinkType} was disabled for run {RunId} after a failure. The run continues normally.",
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
            Disable(ex, "failed to record tool invocation");
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
    /// binding (phase 62). <see langword="null"/> leaves the value
    /// <see cref="StartAsync"/> already wrote unchanged — the overwhelmingly
    /// common case.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    public async ValueTask CompleteAsync(
        RunStatus status,
        RunUsage? usage = null,
        RunError? error = null,
        RunCost? cost = null,
        string? modelId = null,
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

            // A run awaiting human input is neither finished nor failed;
            // writing RunFailed would show a red error in the UI.
            RunStatus.AwaitingInput => new RunEventDraft(RunEventType.RunAwaitingInput),
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

                    // Expected tenant stamp (K-355).
                    TenantId = TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disable(ex, "failed to close run record");
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

    private void Disable(Exception exception, string what)
    {
        IsDisabled = true;

        _logger.LogWarning(
            exception,
            "AgentPrism run recording was disabled ({Reason}). Run {RunId} continues normally.",
            what,
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
}
