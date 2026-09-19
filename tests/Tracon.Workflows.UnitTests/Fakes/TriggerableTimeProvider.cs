namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>A time provider whose registered timers fire only when the test asks.</summary>
/// <remarks>
/// A workflow timeout test must first prove that the intended node is running.
/// Advancing a wall-clock timeout while the graph is still starting makes the
/// asserted node scheduler-dependent on a loaded CI worker.
/// </remarks>
internal sealed class TriggerableTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<TriggerableTimer> _timers = [];

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TriggerableTimer(this, callback, state);

        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Signals every timer that is currently registered.</summary>
    public void TriggerAll()
    {
        TriggerableTimer[] timers;

        lock (_gate)
        {
            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.Trigger();
        }
    }

    private void Remove(TriggerableTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class TriggerableTimer(
        TriggerableTimeProvider owner,
        TimerCallback callback,
        object? state) : ITimer
    {
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Trigger()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }
    }
}
