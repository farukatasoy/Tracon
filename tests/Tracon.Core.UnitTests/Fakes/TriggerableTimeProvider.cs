namespace Tracon.Core.UnitTests.Fakes;

/// <summary>A test clock that fires registered timers only on explicit request.</summary>
/// <remarks>
/// <para>
/// Timeout tests use this clock to prove the elapsed-time path without asking a
/// busy CI scheduler to deliver a short wall-clock timer by a fixed deadline.
/// </para>
/// <para>
/// A test that also needs a fixed wall clock (a quota period start, for example)
/// passes <c>utcNow</c>; the wall clock then stays there until <see cref="Advance"/>
/// moves it. Without it, <see cref="GetUtcNow"/> is the real clock.
/// </para>
/// </remarks>
internal sealed class TriggerableTimeProvider(DateTimeOffset? utcNow = null) : TimeProvider
{
    private readonly List<TriggerableTimer> _timers = [];
    private DateTimeOffset? _utcNow = utcNow;

    /// <summary>Gets how many timers are registered and not yet disposed.</summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_timers)
            {
                return _timers.Count;
            }
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _utcNow ?? base.GetUtcNow();

    /// <summary>Moves the fixed wall clock forward. Fires no timer; see <see cref="TriggerAll"/>.</summary>
    /// <param name="amount">How far to move.</param>
    /// <exception cref="InvalidOperationException">The clock was built without a fixed wall clock.</exception>
    public void Advance(TimeSpan amount)
        => _utcNow = (_utcNow ?? throw new InvalidOperationException("This clock follows the real wall clock."))
            + amount;

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
