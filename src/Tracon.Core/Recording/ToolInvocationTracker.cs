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
/// <strong>Duration is measured only in a streaming run.</strong> In a
/// non-streaming run, all messages are seen at once, after the call has
/// finished; the actual duration between the two content pieces cannot be
/// read from there. Writing a near-zero duration would produce incorrect
/// data, so the field is left empty.
/// </para>
/// <para>
/// This class is <strong>not thread-safe</strong>; each run has its own
/// instance and it is used from a single read loop.
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

    /// <summary>Creates a new tracker.</summary>
    /// <param name="runId">The run identity.</param>
    /// <param name="measureDuration">Whether to measure duration. Only meaningful in a streaming run.</param>
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
    public ToolInvocationTracker(
        Guid runId,
        bool measureDuration,
        TimeProvider timeProvider,
        ToolUsageAccumulator? usage = null,
        string? tenantId = null,
        ToolAuthorizationAccumulator? authorization = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _runId = runId;
        _measureDuration = measureDuration;
        _timeProvider = timeProvider;
        _usage = usage;
        _tenantId = tenantId;
        _authorization = authorization;
    }

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

        return new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = _runId,
            ToolName = toolName,
            ToolCallId = result.CallId,
            Source = source,
            Arguments = arguments,
            Result = result.Exception is null && ToolResultText.TryGetText(result.Result, out var text) ? text : null,
            Duration = duration,
            Error = ToolFailureText.Get(result.Exception),
            CreatedAt = _timeProvider.GetUtcNow(),

            // The tool may have reported its own metric under the call
            // identity. The vast majority of calls carry no metric and the
            // field stays empty.
            Usage = _usage?.Take(result.CallId),

            // A denial returns a normal (non-exceptional) result, so it can
            // only be told apart from an ordinary success through this marker.
            AuthorizationDenied = _authorization?.TakeDenied(result.CallId) ?? false,

            // A timeout is an exception with a stable, known identity.
            TimedOut = result.Exception is TraconToolTimeoutException,

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

        var records = _pending
            .Select(pair => new ToolInvocationRecord
            {
                Id = TraconId.NewId(),
                RunId = _runId,
                ToolName = pair.Value.ToolName,
                ToolCallId = pair.Key,
                Source = pair.Value.Source,
                Arguments = pair.Value.Arguments,
                Duration = _measureDuration ? _timeProvider.GetElapsedTime(pair.Value.StartedAt) : null,
                Error = reason,
                CreatedAt = now,

                // Expected tenant stamp (K-355).
                TenantId = _tenantId,
            })
            .ToList();

        _pending.Clear();

        return records;
    }

    private readonly record struct PendingCall(
        string ToolName,
        string? Source,
        string? Arguments,
        long StartedAt);
}
