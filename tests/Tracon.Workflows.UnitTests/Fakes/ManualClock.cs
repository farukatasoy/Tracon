namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// A time provider whose clock moves only when the test advances it, and whose timers
/// fire when the clock passes their due time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TriggerableTimeProvider"/> fires every timer at once, so it cannot tell a
/// two-second timeout from a ten-minute one. The timeout tests of phase 196 assert WHICH
/// limit applied (a node's own over its registration's, a definition's run timeout over
/// the installation's, the host's ceiling), so the due time has to matter.
/// </para>
/// <para>
/// A timer re-armed with <see cref="ITimer.Change"/> is due from the moment of the
/// change: this is how <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> moves
/// a deadline that was created with a time provider. Callbacks run outside the lock, on
/// the advancing thread.
/// </para>
/// </remarks>
internal sealed class ManualClock : TimeProvider
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _elapsedTicks;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => Interlocked.Read(ref _elapsedTicks);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Start + TimeSpan.FromTicks(Interlocked.Read(ref _elapsedTicks));

    /// <summary>Gets the due times, from now, of the timers that are armed.</summary>
    public IReadOnlyList<TimeSpan> Pending
    {
        get
        {
            lock (_gate)
            {
                var now = _elapsedTicks;

                return [.. _timers
                    .Where(static timer => timer.DueAt is not null)
                    .Select(timer => TimeSpan.FromTicks(timer.DueAt!.Value - now))
                    .Order()];
            }
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);

        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);

        return timer;
    }

    /// <summary>Moves the clock forward and fires every timer whose due time it passes.</summary>
    /// <param name="amount">How far to move.</param>
    public void Advance(TimeSpan amount)
    {
        List<ManualTimer> due;

        lock (_gate)
        {
            _elapsedTicks += amount.Ticks;
            var now = _elapsedTicks;

            due = [.. _timers.Where(timer => timer.DueAt is { } at && at <= now).OrderBy(static timer => timer.DueAt)];

            foreach (var timer in due)
            {
                timer.DueAt = null;
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private void SetDue(ManualTimer timer, TimeSpan dueTime)
    {
        lock (_gate)
        {
            timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _elapsedTicks + dueTime.Ticks;
        }
    }

    private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        /// <summary>The clock tick the timer fires at; <see langword="null"/> while disarmed.</summary>
        public long? DueAt { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                return false;
            }

            // One-shot timers only: every timer the code under test arms here (a CTS
            // deadline, Task.Delay) passes an infinite period.
            owner.SetDue(this, dueTime);

            return true;
        }

        public void Fire()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DueAt = null;
            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
