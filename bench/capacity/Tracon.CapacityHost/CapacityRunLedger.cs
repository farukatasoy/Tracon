using System.Collections.Concurrent;

namespace Tracon.CapacityHost;

/// <summary>What one run has done so far, in the process that is serving it.</summary>
/// <remarks>
/// 🚨 Per run, keyed by correlation - never a process-wide counter. Under load
/// several runs are in flight at once in the same process, and a shared counter
/// would let one run's second turn be decided by another run's first. That
/// failure would look like a capacity finding and would really be a fixture
/// bug, which is the exact class of mistake this apparatus must not make.
/// </remarks>
public sealed class CapacityRunState
{
    /// <summary>When the run's first model turn began, as UTC ticks.</summary>
    public long StartedUtcTicks { get; } = DateTime.UtcNow.Ticks;

    private int _toolCalls;

    /// <summary>How many model turns it has taken.</summary>
    public int Turns { get; set; }

    /// <summary>How long the provider boundary has spent, in milliseconds.</summary>
    public double ModelMilliseconds { get; set; }

    /// <summary>How many times the code tool ran.</summary>
    public int ToolCalls => Volatile.Read(ref _toolCalls);

    /// <summary>Whether the run is being streamed.</summary>
    public bool Streaming { get; set; }

    /// <summary>Counts one tool invocation, from whichever thread served it.</summary>
    public void CountToolCall() => Interlocked.Increment(ref _toolCalls);
}

/// <summary>The per-run state the provider and the code tool share.</summary>
public static class CapacityRunLedger
{
    private static readonly ConcurrentDictionary<string, CapacityRunState> States = new(StringComparer.Ordinal);

    /// <summary>Gets or starts the state of one run.</summary>
    /// <param name="correlation">The run's correlation value.</param>
    /// <returns>Its state.</returns>
    public static CapacityRunState GetOrStart(string correlation)
        => States.GetOrAdd(correlation, static _ => new CapacityRunState());

    /// <summary>Takes the state of a finished run out of the ledger.</summary>
    /// <param name="correlation">The run's correlation value.</param>
    /// <returns>Its state, or <see langword="null"/> when it was never started.</returns>
    public static CapacityRunState? Finish(string correlation)
        => States.TryRemove(correlation, out var state) ? state : null;

    /// <summary>Records that the code tool ran inside a run.</summary>
    /// <param name="correlation">The run's correlation value.</param>
    public static void RecordToolCall(string correlation)
    {
        if (States.TryGetValue(correlation, out var state))
        {
            state.CountToolCall();
        }
    }

    /// <summary>How many runs are in flight in this process right now.</summary>
    public static int InFlight => States.Count;
}
