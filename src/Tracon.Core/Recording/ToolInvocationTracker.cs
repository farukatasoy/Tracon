using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Matches <c>ToolInvoking</c> / <c>ToolInvoked</c> event pairs within a run
/// and produces a <see cref="ToolInvocationRecord"/>.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework does not offer a separate hook for tool calls;
/// the call and its result arrive as <see cref="FunctionCallContent"/> and
/// <see cref="FunctionResultContent"/> content. The single key that joins the
/// two is the <c>CallId</c> value (both share <see cref="ToolCallContent"/> as
/// their base).
/// </para>
/// <para>
/// <strong>A streaming run measures the duration here.</strong> In a
/// non-streaming run, all messages are seen at once, after the call has
/// finished; the actual duration between the two content pieces cannot be
/// read from there, and a near-zero duration would be incorrect data. The
/// duration of a call that passed through the tool chain comes from the
/// <see cref="ToolCallLedger"/> instead, which the chain wrote while the call ran;
/// a call outside the chain (a hosted tool, a tool of the framework) has none in a
/// non-streaming run, and the field is left empty.
/// </para>
/// <para>
/// <strong>Every row is written once.</strong> A call is known to the messages and
/// to the ledger, so each method here that produces a row first claims the call in
/// the ledger. The run's close step writes the calls nobody claimed only when the run failed or was cancelled.
/// </para>
/// <para>
/// This class is <strong>not thread-safe</strong>; each run has its own
/// instance and it is used from a single read loop. The ledger it reads is.
/// </para>
/// </remarks>
internal sealed class ToolInvocationTracker
{
    private readonly Dictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);
    private readonly Guid _runId;
    private readonly bool _measureDuration;
    private readonly TimeProvider _timeProvider;
    private readonly ToolUsageAccumulator? _usage;
    private readonly string? _tenantId;
    private readonly ToolAuthorizationAccumulator? _authorization;
    private readonly ToolExplainedFailureAccumulator? _explainedFailures;
    private readonly ToolCallLedger? _ledger;

    /// <summary>Creates a new tracker.</summary>
    /// <param name="runId">The run identity.</param>
    /// <param name="measureDuration">
    /// Whether to measure duration here. Only meaningful in a streaming run; a run that does not stream takes the
    /// duration from <paramref name="ledger"/>.
    /// </param>
    /// <param name="timeProvider">The time source.</param>
    /// <param name="usage">
    /// The non-token metrics reported by tools. If <see langword="null"/>,
    /// no metrics are collected.
    /// </param>
    /// <param name="tenantId">
    /// The EXPECTED tenant of the run. Stamped onto every produced record;
    /// if <see langword="null"/>, the store performs no tenant check.
    /// </param>
    /// <param name="authorization">
    /// The authorization decisions reported by tools within a run. If
    /// <see langword="null"/>, no denial is ever recorded — used for a run
    /// that never wraps a tool with <c>AuthorizingAIFunction</c>.
    /// </param>
    /// <param name="explainedFailures">
    /// The failures reported as results by <c>ExplainedFailureAIFunction</c>.
    /// If <see langword="null"/>, only an exception carried by the result
    /// itself marks a call as failed.
    /// </param>
    /// <param name="ledger">
    /// The calls the tool chain recorded. If <see langword="null"/>, a run that does not stream
    /// has no duration for a call, and the tracker claims nothing.
    /// </param>
    public ToolInvocationTracker(
        Guid runId,
        bool measureDuration,
        TimeProvider timeProvider,
        ToolUsageAccumulator? usage = null,
        string? tenantId = null,
        ToolAuthorizationAccumulator? authorization = null,
        ToolExplainedFailureAccumulator? explainedFailures = null,
        ToolCallLedger? ledger = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _runId = runId;
        _measureDuration = measureDuration;
        _timeProvider = timeProvider;
        _usage = usage;
        _tenantId = tenantId;
        _authorization = authorization;
        _explainedFailures = explainedFailures;
        _ledger = ledger;
    }

    /// <summary>Reports whether a call was recorded as started and has no result yet.</summary>
    /// <param name="callId">The call identity.</param>
    /// <returns><see langword="true"/> while the call waits for <see cref="OnResult"/> or <see cref="DrainUnfinished"/>.</returns>
    public bool IsPending(string callId) => _pending.ContainsKey(callId);

    /// <summary>Records that a tool call started.</summary>
    /// <param name="call">The call content.</param>
    /// <param name="source">The tool's source; <see langword="null"/> if defined in code.</param>
    /// <param name="arguments">The formatted arguments.</param>
    public void OnCall(FunctionCallContent call, string? source, string? arguments)
    {
        ArgumentNullException.ThrowIfNull(call);

        // If the same CallId arrives a second time (retry), it overwrites
        // the first record: the result always matches the latest call.
        _pending[call.CallId] = new PendingCall(
            call.Name,
            source,
            arguments,
            _timeProvider.GetTimestamp());
    }

    /// <summary>
    /// Records that a tool call finished and produces the persistent record.
    /// </summary>
    /// <param name="result">The result content.</param>
    /// <returns>
    /// The persistent record. A record is produced even if no matching call
    /// is found; in that case the tool name is written as <c>unknown</c>.
    /// </returns>
    public ToolInvocationRecord OnResult(FunctionResultContent result)
    {
        ArgumentNullException.ThrowIfNull(result);

        TimeSpan? duration = null;
        string toolName = "unknown";
        string? source = null;
        string? arguments = null;

        // The row is written now, so the ledger's copy of the call is spoken for. What it holds is read
        // below: null for a call that did not pass through the tool chain.
        var settled = _ledger?.Claim(result.CallId);

        if (_pending.Remove(result.CallId, out var call))
        {
            toolName = call.ToolName;
            source = call.Source;
            arguments = call.Arguments;

            if (_measureDuration)
            {
                duration = _timeProvider.GetElapsedTime(call.StartedAt);
            }
        }

        // A run that does not stream reads the result after the call is long over, so the pair of content
        // pieces measures nothing. The tool chain wrote the call while it ran: its time is the call's.
        var settledByLedger = !_measureDuration && settled is { IsSettled: true };

        if (settledByLedger)
        {
            duration = settled!.Duration;
        }

        // A call whose explanation became its RESULT carries no exception here:
        // that is the price of letting the model read why it failed. The record
        // must not read it as a success, so the failure travels beside the
        // result and is taken exactly once, by call identity.
        var failure = result.Exception ?? _explainedFailures?.TakeFailure(result.CallId);

        return new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = _runId,
            ToolName = toolName,
            ToolCallId = result.CallId,
            Source = source,
            Arguments = arguments,
            Result = failure is null && ToolResultText.TryGetText(result.Result, out var text) ? text : null,
            Duration = duration,
            Error = ToolFailureText.Get(failure),

            // The moment the call settled. A run that does not stream learns of the call after the run, so the
            // time the call really ended (the ledger's) is the one that puts the rows in the order they ran.
            CreatedAt = settledByLedger ? settled!.EndedAtUtc!.Value : _timeProvider.GetUtcNow(),

            // The tool may have reported its own metric under the call
            // identity. The vast majority of calls carry no metric and the
            // field stays empty.
            Usage = _usage?.Take(result.CallId),

            // A denial returns a normal (non-exceptional) result, so it can
            // only be told apart from an ordinary success through this marker.
            AuthorizationDenied = _authorization?.TakeDenied(result.CallId) ?? false,

            // A timeout is an exception with a stable, known identity.
            TimedOut = failure is TraconToolTimeoutException,

            // Expected tenant stamp (K-355).
            TenantId = _tenantId,
        };
    }

    /// <summary>
    /// Produces the record of a hosted tool's call. The provider ran the tool inside the model call,
    /// so the call is complete when its content arrives and nothing is left pending.
    /// </summary>
    /// <param name="callId">The call identity the provider assigned.</param>
    /// <param name="toolName">The registered tool name.</param>
    /// <returns>The persistent record. It carries the call's cost measurement when the cost ring reported one.</returns>
    /// <remarks>
    /// The duration is not measured: the call happened inside the model call, whose own span holds it. The
    /// arguments are not recorded: a search query is the user's text, and the content event never carried it
    /// either.
    /// </remarks>
    public ToolInvocationRecord OnHostedCall(string callId, string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(callId);
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        return new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = _runId,
            ToolName = toolName,
            ToolCallId = callId,
            CreatedAt = _timeProvider.GetUtcNow(),
            Usage = _usage?.Take(callId),

            // Expected tenant stamp (K-355).
            TenantId = _tenantId,
        };
    }

    /// <summary>
    /// Produces records for unfinished calls and drains the tracker.
    /// </summary>
    /// <remarks>
    /// If the run ends before a tool result arrives (cancellation, error,
    /// awaiting approval), the call record would never be written. This
    /// produces the UI state "the call started but its outcome is unknown";
    /// instead, it is closed with an explicit error message.
    /// </remarks>
    /// <param name="reason">Why the record was left unfinished.</param>
    /// <returns>The records for the unfinished calls.</returns>
    public IReadOnlyList<ToolInvocationRecord> DrainUnfinished(string reason)
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        var now = _timeProvider.GetUtcNow();
        var records = new List<ToolInvocationRecord>(_pending.Count);

        foreach (var (callId, pending) in _pending)
        {
            // The row is written now: the ledger's copy of the call is spoken for.
            var held = _ledger?.Claim(callId);

            records.Add(new ToolInvocationRecord
            {
                Id = TraconId.NewId(),
                RunId = _runId,
                ToolName = pending.ToolName,
                ToolCallId = callId,
                Source = pending.Source,
                Arguments = pending.Arguments,

                // Measured here in a streaming run. A run that does not stream knows when the call started only
                // from the ledger, and a call that never ended has run until now.
                Duration = _measureDuration
                    ? _timeProvider.GetElapsedTime(pending.StartedAt)
                    : held is null ? null : held.Duration ?? _timeProvider.GetElapsedTime(held.StartedAt),
                Error = reason,
                CreatedAt = now,

                // A call that reported its measurement and then never settled
                // (cancelled mid-flight, say) spent what it reported: the
                // image was generated before the attachment was written.
                Usage = _usage?.Take(callId),

                // Expected tenant stamp (K-355).
                TenantId = _tenantId,
            });
        }

        _pending.Clear();

        return records;
    }

    private readonly record struct PendingCall(
        string ToolName,
        string? Source,
        string? Arguments,
        long StartedAt);
}
