using System.Runtime.CompilerServices;

namespace Tracon;

/// <summary>
/// A reading of the time that a step of the wall clock does not move, for a store that decides when a lease ends on the
/// application's own clock.
/// </summary>
/// <remarks>
/// <para>
/// A lease is a span of time: it ends a given number of milliseconds after it was taken, whatever the calendar says in
/// between. A store that wrote the end of a lease as <c>wall clock + duration</c> and compared it with the wall clock again
/// read a live lease as over when a time service stepped the clock forward (or a resumed virtual machine caught up), and
/// the job ran twice at once; read a stepped-back clock as a lease that lasts longer than it was taken for.
/// </para>
/// <para>
/// <see cref="Now"/> is the wall clock as it read when the clock was first looked at, plus the monotonic time that has
/// passed since (<see cref="TimeProvider.GetElapsedTime(long)"/>). It starts on the calendar, so a lease end written by it
/// is still an instant a person can read, and it moves with the elapsed time only, so a step of the wall clock does not
/// reach it. A promise made on the calendar (a job scheduled for a time, a cron trigger) is the other way round and keeps
/// reading <see cref="TimeProvider.GetUtcNow"/>.
/// </para>
/// <para>
/// One reading is kept for each <see cref="TimeProvider"/>, so the stores of a process that share a clock agree about the
/// span between two readings. A lease row another process left behind is read on this process's line: when the wall clock
/// did not step between the two starts there is no difference; when it stepped ahead the old lease ends early (the job of a
/// dead process is taken sooner, the safe side), and when it stepped back it ends later by at most the step.
/// </para>
/// <para>
/// A <see cref="TimeProvider"/> that a test advances has to advance the monotonic reading
/// (<see cref="TimeProvider.GetTimestamp"/>) with the wall clock, as <c>Microsoft.Extensions.Time.Testing.FakeTimeProvider</c>
/// does: a fake that overrides only <see cref="TimeProvider.GetUtcNow"/> moves the calendar and leaves every lease alive.
/// </para>
/// <para>
/// A database server's clock (PostgreSQL, SQL Server) needs none of this: the lease statements read it themselves, and the
/// application's clock never reaches them.
/// </para>
/// </remarks>
internal sealed class SteadyClock
{
    private static readonly ConditionalWeakTable<TimeProvider, SteadyClock> Readings = new();

    private SteadyClock(TimeProvider clock)
    {
        Clock = clock;
        WallAtStart = clock.GetUtcNow();
        TimestampAtStart = clock.GetTimestamp();
    }

    /// <summary>Gets the time as the wall clock read when this clock started, plus the monotonic time that has passed since.</summary>
    public DateTimeOffset Now => WallAtStart + Clock.GetElapsedTime(TimestampAtStart);

    private TimeProvider Clock { get; }

    private DateTimeOffset WallAtStart { get; }

    private long TimestampAtStart { get; }

    /// <summary>Gets the reading that belongs to <paramref name="clock"/>, starting it on the first call.</summary>
    /// <param name="clock">The application clock.</param>
    /// <returns>The reading every store that shares <paramref name="clock"/> uses.</returns>
    public static SteadyClock For(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return Readings.GetValue(clock, static key => new SteadyClock(key));
    }
}
