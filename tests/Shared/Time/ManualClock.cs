namespace Tracon.Tests.Common;

/// <summary>
/// A time provider whose clock moves only when the test advances it, and whose timers
/// fire when the clock passes their due time.
/// </summary>
/// <remarks>
/// <para>
/// <c>TriggerableTimeProvider</c> fires every timer at once, so it cannot tell a
/// two-second timeout from a ten-minute one. The timeout tests of assert WHICH
/// limit applied (a node's own over its registration's, a definition's run timeout over
/// the installation's, the host's ceiling, a job's poll interval), so the due time has to
/// matter.
/// </para>
/// <para>
/// A timer re-armed with <see cref="ITimer.Change"/> is due from the moment of the
/// change: this is how <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> moves
/// a deadline that was created with a time provider. A periodic timer (a
/// <see cref="PeriodicTimer"/> built on this clock) is due again one period after it
/// fired. Callbacks run outside the lock, on the advancing thread.
/// </para>
/// <para>
/// Linked into the test projects that need it, not copied.
/// </para>
/// </remarks>
internal sealed class ManualClock : TimeProvider
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<TimeSpan> _steps = [];
    private long _elapsedTicks;
    private long _wallStepTicks;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => Interlocked.Read(ref _elapsedTicks);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
        => Start + TimeSpan.FromTicks(Interlocked.Read(ref _elapsedTicks) + Interlocked.Read(ref _wallStepTicks));

    /// <summary>
    /// Steps only the wall clock, in either direction, as a time sync correction or a resumed virtual machine does: no time
    /// passes on the monotonic clock and no timer fires.
    /// </summary>
    /// <param name="amount">How far to step the wall clock.</param>
    public void StepWallClock(TimeSpan amount) => Interlocked.Add(ref _wallStepTicks, amount.Ticks);

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

    /// <summary>
    /// Moves the clock forward and fires every timer whose due time it passes, each
    /// once; a periodic timer is then due one period after now.
    /// </summary>
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
                timer.DueAt = timer.Period is { } period ? now + period.Ticks : null;
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    /// <summary>
    /// Gets the amounts <see cref="AdvanceUntilAsync"/> moved the clock by, in order: one per timer it fired. A test that
    /// asserts how long a wait was reads it here instead of timing the wait.
    /// </summary>
    public IReadOnlyList<TimeSpan> Steps
    {
        get
        {
            lock (_gate)
            {
                return [.. _steps];
            }
        }
    }

    /// <summary>
    /// Moves the clock to the earliest armed timer, again and again, until <paramref name="work"/> ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Advance"/> fires only the timers that are armed when it runs. Work that waits, wakes and then waits
    /// again (an HTTP client retrying with backoff) arms its next timer only after the previous one fired, so one
    /// <see cref="Advance"/> cannot carry it to the end. This steps through them one by one, the way the real clock
    /// would, without waiting for any of them.
    /// </para>
    /// <para>
    /// A timer due further away than <paramref name="within"/> is left alone: it is a deadline or a heartbeat of the
    /// system under test, not the wait this call stands in for, and firing it would end the work by timeout.
    /// </para>
    /// </remarks>
    /// <param name="work">The work that waits on this clock.</param>
    /// <param name="within">The furthest timer the call may fire; a minute when omitted, the ceiling a provider's retry header is honored up to.</param>
    /// <param name="timeout">The failure bound for <paramref name="work"/> to end; <see cref="WaitUntil.DefaultTimeout"/> when omitted.</param>
    /// <returns>A task that ends when <paramref name="work"/> has ended; it rethrows what <paramref name="work"/> threw.</returns>
    public async Task AdvanceUntilAsync(Task work, TimeSpan? within = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        var reach = within ?? TimeSpan.FromMinutes(1);

        await WaitUntil.TrueAsync(
            async () =>
            {
                if (work.IsCompleted)
                {
                    return true;
                }

                var armed = Pending;

                if (armed.Count > 0 && armed[0] <= reach)
                {
                    var step = armed[0] < TimeSpan.Zero ? TimeSpan.Zero : armed[0];

                    lock (_gate)
                    {
                        _steps.Add(step);
                    }

                    Advance(step);
                    await Task.Yield();
                }

                return work.IsCompleted;
            },
            "the work that waits on the manual clock to end",
            timeout).ConfigureAwait(false);

        await work.ConfigureAwait(false);
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private void Set(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _elapsedTicks + dueTime.Ticks;
            timer.Period = period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero ? null : period;
        }
    }

    private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        /// <summary>The clock tick the timer fires at; <see langword="null"/> while disarmed.</summary>
        public long? DueAt { get; set; }

        /// <summary>The period of a periodic timer; <see langword="null"/> for a one-shot timer.</summary>
        public TimeSpan? Period { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                return false;
            }

            owner.Set(this, dueTime, period);

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
