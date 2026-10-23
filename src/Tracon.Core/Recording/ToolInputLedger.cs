namespace Tracon;

/// <summary>
/// Collects the tool input requests (MCP elicitation) that suspended a call of one run, so the
/// run recorder closes the run <see cref="RunStatus.AwaitingInput"/> and the endpoint announces them.
/// </summary>
/// <remarks>
/// Same ambient-write/scoped-read pattern as <see cref="ToolExplainedFailureAccumulator"/>: the tool
/// wrapper writes through <see cref="TraconRunContext.Current"/>, the recorder reads from the scope it
/// opened. The ledger is the only source — a tool result that merely looks like a pending request is
/// not one.
/// </remarks>
internal sealed class ToolInputLedger
{
    // Locks on the list itself: net8.0 has no System.Threading.Lock, and a separate
    // object field would trigger MA0158 on net9.0+ (the InMemoryJobStore pattern).
    private readonly List<PendingToolInput> _pending = [];

    /// <summary>Gets whether any call was suspended.</summary>
    public bool HasPending
    {
        get
        {
            lock (_pending)
            {
                return _pending.Count > 0;
            }
        }
    }

    /// <summary>Records a suspended call.</summary>
    /// <param name="input">The request that suspended it.</param>
    public void Add(PendingToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_pending)
        {
            _pending.Add(input);
        }
    }

    /// <summary>Returns the requests recorded so far, in the order they were suspended.</summary>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<PendingToolInput> Snapshot()
    {
        lock (_pending)
        {
            return [.. _pending];
        }
    }
}
