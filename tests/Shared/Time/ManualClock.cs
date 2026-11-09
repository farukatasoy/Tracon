namespace Tracon.Tests.Common;

/// <summary>
/// A time provider whose clock moves only when the test advances it, and whose timers
/// fire when the clock passes their due time.
/// </summary>
/// <remarks>
/// <para>
/// A fake that fires every timer at once cannot tell a two-second timeout from a
/// ten-minute one. The timeout tests assert WHICH limit applied (a node's own over its
/// registration's, a definition's run timeout over the installation's, the host's ceiling,
/// a job's poll interval), so the due time has to matter. <see cref="FireAll"/> is for the
/// test that does not care which.
/// </para>
/// <para>
/// A timer re-armed with <see cref="ITimer.Change"/> is due from the moment of the
/// change: this is how <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> moves
/// a deadline that was created with a time provider. A periodic timer (a
/// <see cref="PeriodicTimer"/> built on this clock) is due again one period after it
/// fired. Callbacks run outside the lock, on the advancing thread.
/// </para>
/// <para>
/// This is the one fake clock of the test tree; a new need extends it. It has two parts. The clock itself is virtual: the
/// wall clock and the monotonic clock move only through <see cref="Advance"/>, <see cref="StepWallClock"/> and
/// <see cref="StepMonotonic"/>. The timers are virtual too, unless the clock is made with <see cref="WithSystemTimers"/> or
/// <see cref="OverSystemClock"/>, which are migration aids and not the goal: a host whose background loops still tick on
/// the system's timers (a 20 ms job-worker poll) cannot run on virtual ones, because nothing would ever advance them.
/// A test that can run on virtual timers should.
/// </para>
/// <para>
/// Linked into the test projects that need it, not copied.
/// </para>
/// </remarks>
internal sealed class ManualClock : TimeProvider
{
    private static readonly DateTimeOffset DefaultStart = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<TimeSpan> _steps = [];
    private readonly DateTimeOffset? _start;
    private readonly TimeSpan _systemOffset;
    private readonly bool _systemTimers;
    private readonly bool _systemTimestamp;
    private long _elapsedTicks;
    private long _wallStepTicks;
    private int _created;

    /// <summary>Creates a clock that starts at a fixed instant, with virtual timers.</summary>
    public ManualClock()
        : this(DefaultStart)
    {
    }

    /// <summary>Creates a clock that starts at <paramref name="start"/>, with virtual timers.</summary>
    /// <param name="start">What <see cref="GetUtcNow"/> reads before the clock moves.</param>
    public ManualClock(DateTimeOffset start)
        : this(start, TimeSpan.Zero, systemTimers: false, systemTimestamp: false)
    {
    }

    private ManualClock(DateTimeOffset? start, TimeSpan systemOffset, bool systemTimers, bool systemTimestamp)
    {
        _start = start;
        _systemOffset = systemOffset;
        _systemTimers = systemTimers;
        _systemTimestamp = systemTimestamp;
    }

    /// <summary>
    /// Creates a clock whose timers are the system's, for a host whose background loops keep ticking on real time while the
    /// test controls the wall clock. The wall clock stands still at <paramref name="start"/> until the test moves it;
    /// <see cref="Advance"/> moves the clocks and fires no timer.
    /// </summary>
    /// <remarks>
    /// A migration aid: a test that can wait on virtual timers should use <see cref="ManualClock()"/>. Pending timers, the
    /// timer counters, <see cref="FireAll"/> and <see cref="AdvanceUntilAsync"/> see no timer of this clock.
    /// </remarks>
    /// <param name="start">What the wall clock reads before it moves; the system's wall clock at this call when omitted.</param>
    /// <param name="systemTimestamp">
    /// <see langword="true"/> to leave the monotonic clock to the system, so that a duration the host measures keeps running
    /// and <see cref="Advance"/> moves the wall clock alone; <see langword="false"/> to move it with <see cref="Advance"/>
    /// only, so that a test can make a duration exact.
    /// </param>
    /// <returns>The clock.</returns>
    public static ManualClock WithSystemTimers(DateTimeOffset? start = null, bool systemTimestamp = true)
        => new(start ?? TimeProvider.System.GetUtcNow(), TimeSpan.Zero, systemTimers: true, systemTimestamp);

    /// <summary>
    /// Creates a clock that is the system's, wrong by a fixed distance: the wall clock keeps running and reads
    /// <paramref name="offset"/> from the system's, and the timers are the system's. It is the clock of a node whose time is
    /// off, run against a database or a process whose clock the test cannot move.
    /// </summary>
    /// <remarks>
    /// <see cref="StepWallClock"/> and <see cref="Advance"/> move the wall clock further. The same limits as
    /// <see cref="WithSystemTimers"/> apply.
    /// </remarks>
    /// <param name="offset">How far the wall clock is ahead of the system's (negative: behind).</param>
    /// <param name="systemTimestamp">As for <see cref="WithSystemTimers"/>.</param>
    /// <returns>The clock.</returns>
    public static ManualClock OverSystemClock(TimeSpan offset, bool systemTimestamp = true)
        => new(start: null, offset, systemTimers: true, systemTimestamp);

    /// <summary>
    /// Gets or initializes whether each timer keeps the stack that made it, for <see cref="PendingOrigins"/>. A stack costs
    /// time to take, so it is off unless a test asks where a leaked timer came from.
    /// </summary>
    public bool CaptureTimerOrigins { get; init; }

    /// <summary>
    /// Gets or initializes how far away a timer may be and still fire the moment it is armed, on a thread-pool thread and
    /// with the clock left where it is; a timer further away stays armed until the test advances to it or disposes it.
    /// </summary>
    /// <remarks>
    /// For a system under test whose short pauses (the wait before a write is asked again) must pass without the test
    /// driving them, and whose long timers (a run's deadline) must stay armed so that the test can count what a run left
    /// behind. A test that needs to say which wait ended and when advances the clock instead.
    /// </remarks>
    public TimeSpan? AutoFireWithin { get; init; }

    /// <inheritdoc />
    public override long TimestampFrequency => _systemTimestamp ? TimeProvider.System.TimestampFrequency : TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp()
    {
        if (_systemTimestamp)
        {
            return TimeProvider.System.GetTimestamp();
        }

        lock (_gate)
        {
            return _elapsedTicks;
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        var origin = _start ?? TimeProvider.System.GetUtcNow() + _systemOffset;

        lock (_gate)
        {
            return origin + TimeSpan.FromTicks(_elapsedTicks + _wallStepTicks);
        }
    }

    /// <summary>
    /// Steps only the wall clock, in either direction, as a time sync correction or a resumed virtual machine does: no time
    /// passes on the monotonic clock and no timer fires.
    /// </summary>
    /// <param name="amount">How far to step the wall clock.</param>
    public void StepWallClock(TimeSpan amount)
    {
        lock (_gate)
        {
            _wallStepTicks += amount.Ticks;
        }
    }

    /// <summary>
    /// Steps only the monotonic clock forward, the wall clock staying where it is: a process that was suspended for the
    /// span and resumed on a platform whose monotonic counter counts the suspension (the wall clock is then set back by the
    /// time service, or was never stepped). The timers that fall due in the span fire, as they do on resume.
    /// </summary>
    /// <param name="amount">How far to move; not negative.</param>
    public void StepMonotonic(TimeSpan amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);

        FireDueAfter(amount, holdWall: true);
    }

    /// <summary>Gets how many timers this clock made, including the ones since disposed.</summary>
    public int CreatedTimers => Volatile.Read(ref _created);

    /// <summary>Gets how many timers this clock made and nobody has disposed, whether or not they are armed.</summary>
    public int RunningTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

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

    /// <summary>
    /// Gets where the timers that are armed were made, one stack for each; empty unless <see cref="CaptureTimerOrigins"/> is
    /// on. A test that asserts nothing is left armed reads it to say who left the timer.
    /// </summary>
    public string PendingOrigins
    {
        get
        {
            lock (_gate)
            {
                return string.Join(" || ", _timers
                    .Where(static timer => timer.DueAt is not null && timer.Origin is not null)
                    .Select(static timer => string.Join(" < ", timer.Origin!
                        .Split('\n')
                        .Select(static line => line.Trim())
                        .Where(static line => line.Contains("Tracon", StringComparison.Ordinal) && !line.Contains(typeof(ManualClock).FullName!, StringComparison.Ordinal))
                        .Take(5))));
            }
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (_systemTimers)
        {
            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
        }

        var timer = new ManualTimer(this, callback, state)
        {
            Origin = CaptureTimerOrigins ? Environment.StackTrace : null,
        };

        Interlocked.Increment(ref _created);

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
    public void Advance(TimeSpan amount) => FireDueAfter(amount, holdWall: false);

    /// <summary>
    /// Fires every armed timer now, whatever its due time, and leaves the clock where it is; a periodic timer is then due
    /// one period after now. It is for a test that has first to see the timer armed and then to end the wait it stands for,
    /// without saying how long the wait was.
    /// </summary>
    /// <remarks>
    /// <see cref="Advance"/> is the better tool when the due time matters: a test that asserts WHICH limit applied advances
    /// past the one and not past the other.
    /// </remarks>
    public void FireAll()
    {
        List<ManualTimer> armed;

        lock (_gate)
        {
            armed = [.. _timers.Where(static timer => timer.DueAt is not null).OrderBy(static timer => timer.DueAt)];

            Rearm(armed, _elapsedTicks);
        }

        foreach (var timer in armed)
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

    private void FireDueAfter(TimeSpan amount, bool holdWall)
    {
        List<ManualTimer> due;

        lock (_gate)
        {
            _elapsedTicks += amount.Ticks;

            if (holdWall)
            {
                _wallStepTicks -= amount.Ticks;
            }

            var now = _elapsedTicks;

            due = [.. _timers.Where(timer => timer.DueAt is { } at && at <= now).OrderBy(static timer => timer.DueAt)];

            Rearm(due, now);
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private static void Rearm(List<ManualTimer> fired, long now)
    {
        foreach (var timer in fired)
        {
            timer.DueAt = timer.Period is { } period ? now + period.Ticks : null;
        }
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
        var fireNow = false;

        lock (_gate)
        {
            timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _elapsedTicks + dueTime.Ticks;
            timer.Period = period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero ? null : period;

            if (timer.DueAt is not null && dueTime >= TimeSpan.Zero && dueTime <= AutoFireWithin)
            {
                fireNow = true;
                Rearm([timer], _elapsedTicks);
            }
        }

        if (fireNow)
        {
            _ = Task.Run(timer.Fire, CancellationToken.None);
        }
    }

    private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        /// <summary>The clock tick the timer fires at; <see langword="null"/> while disarmed.</summary>
        public long? DueAt { get; set; }

        /// <summary>The period of a periodic timer; <see langword="null"/> for a one-shot timer.</summary>
        public TimeSpan? Period { get; set; }

        /// <summary>The stack that made the timer; <see langword="null"/> unless the clock captures origins.</summary>
        public string? Origin { get; init; }

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
