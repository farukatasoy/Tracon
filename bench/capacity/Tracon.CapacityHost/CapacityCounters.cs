namespace Tracon.CapacityHost;

/// <summary>The host's own counters, read by the driver over the apparatus surface.</summary>
/// <remarks>
/// 🚨 <see cref="RecordingFailures"/> exists because Tracon's own rule is that
/// observability must not break functionality: a store that refuses a write
/// leaves the run green. That is correct product behaviour, and it is exactly
/// why a capacity run needs the host to say out loud how many writes it lost.
/// A non-zero value makes the cell invalid.
/// </remarks>
public sealed class CapacityCounters
{
    private long _modelCalls;
    private long _modelTurns;
    private long _toolInvocations;
    private long _recordingFailures;

    /// <summary>How many runs the provider answered.</summary>
    public long ModelCalls => Interlocked.Read(ref _modelCalls);

    /// <summary>How many model turns those runs took.</summary>
    public long ModelTurns => Interlocked.Read(ref _modelTurns);

    /// <summary>How many times the code tool ran.</summary>
    public long ToolInvocations => Interlocked.Read(ref _toolInvocations);

    /// <summary>How many recorded writes the host failed to make.</summary>
    public long RecordingFailures => Interlocked.Read(ref _recordingFailures);

    /// <summary>Counts one completed run.</summary>
    public void ModelCall() => Interlocked.Increment(ref _modelCalls);

    /// <summary>Counts one model turn.</summary>
    public void ModelTurn() => Interlocked.Increment(ref _modelTurns);

    /// <summary>Counts one tool invocation.</summary>
    public void ToolInvocation() => Interlocked.Increment(ref _toolInvocations);

    /// <summary>Counts one failed recording write.</summary>
    public void RecordingFailure() => Interlocked.Increment(ref _recordingFailures);

    /// <summary>Clears every counter, so a warm-up never leaks into the measured window.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _modelCalls, 0);
        Interlocked.Exchange(ref _modelTurns, 0);
        Interlocked.Exchange(ref _toolInvocations, 0);
        Interlocked.Exchange(ref _recordingFailures, 0);
    }
}
