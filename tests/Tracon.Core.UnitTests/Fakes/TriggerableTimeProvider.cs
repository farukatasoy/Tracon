namespace Tracon.Core.UnitTests.Fakes;

/// <summary>A test clock that fires registered timers only on explicit request.</summary>
/// <remarks>
/// Timeout tests use this clock to prove the elapsed-time path without asking a
/// busy CI scheduler to deliver a short wall-clock timer by a fixed deadline.
/// </remarks>
internal sealed class TriggerableTimeProvider : TimeProvider
{
    private readonly List<TriggerableTimer> _timers = [];

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TriggerableTimer(this, callback, state);

        lock (_timers)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Signals every timer that is currently registered.</summary>
    public void TriggerAll()
    {
        TriggerableTimer[] timers;

        lock (_timers)
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
        lock (_timers)
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
