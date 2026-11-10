using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.TestInfrastructure;

/// <summary>
/// The standard fake clock: <c>AdvanceUntilAsync</c> carries work that waits more than once to its end without waiting,
/// and the rest of its surface stands in for the fake clocks that were written one for each test project.
/// </summary>
/// <remarks>
/// <c>AdvanceUntilAsync</c> exists because work that retries with backoff arms its next timer only after the previous one
/// fired, so one <c>Advance</c> cannot reach the end of it.
/// </remarks>
public sealed class ManualClockTests
{
    [Fact]
    public async Task Work_that_waits_again_after_each_wake_is_carried_to_its_end()
    {
        var clock = new ManualClock();
        var work = WaitThreeTimesAsync(clock);

        await clock.AdvanceUntilAsync(work);

        clock.Steps.ShouldBe([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)]);
    }

    [Fact]
    public async Task A_single_advance_fires_only_the_timer_that_is_armed()
    {
        var clock = new ManualClock();
        var secondArmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = WaitTwiceAsync(clock, secondArmed);

        clock.Advance(TimeSpan.FromHours(1));

        // The work arms its second timer in the continuation of the first wake and says so; the test waits for that signal
        // and not for the continuation to happen to have run.
        await secondArmed.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        clock.Pending.ShouldBe([TimeSpan.FromSeconds(2)], "the second wait was armed after the first fired, so the one advance never reached it");
        work.IsCompleted.ShouldBeFalse();

        await clock.AdvanceUntilAsync(work);

        clock.Steps.ShouldBe([TimeSpan.FromSeconds(2)]);
    }

    [Fact]
    public async Task A_timer_beyond_the_reach_is_left_alone_when_it_is_the_only_one_armed()
    {
        var clock = new ManualClock();
        var deadlineFired = 0;
        var never = new TaskCompletionSource();

        using var deadline = clock.CreateTimer(_ => Interlocked.Increment(ref deadlineFired), null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);

        // The work cannot end by itself, so nothing but firing the deadline could carry it: with the reach honored the call
        // ends by its failure bound, with the clock where it was.
        await Should.ThrowAsync<TimeoutException>(
            () => clock.AdvanceUntilAsync(never.Task, within: TimeSpan.FromSeconds(30), timeout: TimeSpan.FromMilliseconds(200)));

        Volatile.Read(ref deadlineFired).ShouldBe(0, "a deadline of the system under test is not the wait the call stands in for");
        clock.Steps.ShouldBeEmpty();
        clock.Pending.ShouldBe([TimeSpan.FromMinutes(5)]);
    }

    [Fact]
    public async Task What_the_work_throws_is_thrown_to_the_caller()
    {
        var clock = new ManualClock();
        var work = FailAfterAsync(clock);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => clock.AdvanceUntilAsync(work));

        thrown.Message.ShouldBe("the wait ended and the work failed");
        clock.Steps.ShouldBe([TimeSpan.FromSeconds(3)]);
    }

    [Fact]
    public async Task Work_that_never_arms_a_timer_ends_by_the_failure_bound()
    {
        var clock = new ManualClock();
        var never = new TaskCompletionSource();

        await Should.ThrowAsync<TimeoutException>(
            () => clock.AdvanceUntilAsync(never.Task, timeout: TimeSpan.FromMilliseconds(200)));

        clock.Steps.ShouldBeEmpty();
    }

    [Fact]
    public void A_clock_reads_the_instant_it_starts_at_until_it_moves()
    {
        var start = new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.FromHours(2));
        var clock = new ManualClock(start);

        clock.GetUtcNow().ShouldBe(start);

        clock.Advance(TimeSpan.FromMinutes(3));
        clock.StepWallClock(TimeSpan.FromSeconds(-10));

        clock.GetUtcNow().ShouldBe(start.AddMinutes(3).AddSeconds(-10));
        clock.GetTimestamp().ShouldBe(TimeSpan.FromMinutes(3).Ticks);
    }

    [Fact]
    public void A_monotonic_step_moves_the_timestamp_and_the_timers_but_not_the_wall_clock()
    {
        var clock = new ManualClock();
        var wall = clock.GetUtcNow();
        var fired = 0;

        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

        clock.StepMonotonic(TimeSpan.FromSeconds(45));

        clock.GetUtcNow().ShouldBe(wall);
        clock.GetTimestamp().ShouldBe(TimeSpan.FromSeconds(45).Ticks);
        fired.ShouldBe(1, "a timer that fell due in the span fires, as it does when a suspended process resumes");
    }

    [Fact]
    public void A_monotonic_step_back_is_refused()
    {
        var clock = new ManualClock();

        Should.Throw<ArgumentOutOfRangeException>(() => clock.StepMonotonic(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void The_timer_counters_tell_created_timers_from_the_ones_nobody_disposed()
    {
        var clock = new ManualClock();

        var first = clock.CreateTimer(static _ => { }, null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
        var idle = clock.CreateTimer(static _ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        clock.CreatedTimers.ShouldBe(2);
        clock.RunningTimers.ShouldBe(2, "a timer that is not armed still runs until it is disposed");

        first.Dispose();
        first.Dispose();

        clock.CreatedTimers.ShouldBe(2);
        clock.RunningTimers.ShouldBe(1);

        idle.Dispose();

        clock.RunningTimers.ShouldBe(0);
    }

    [Fact]
    public void A_fired_one_shot_timer_still_runs_until_it_is_disposed()
    {
        var clock = new ManualClock();

        using var timer = clock.CreateTimer(static _ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(1));

        clock.Pending.ShouldBeEmpty();
        clock.RunningTimers.ShouldBe(1);
    }

    [Fact]
    public void Fire_all_fires_every_armed_timer_whatever_its_due_time_and_leaves_the_clock_alone()
    {
        var clock = new ManualClock();
        var wall = clock.GetUtcNow();
        var fired = new List<string>();

        using var soon = clock.CreateTimer(_ => fired.Add("soon"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        using var late = clock.CreateTimer(_ => fired.Add("late"), null, TimeSpan.FromHours(10), Timeout.InfiniteTimeSpan);
        using var idle = clock.CreateTimer(_ => fired.Add("idle"), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        using var disposed = clock.CreateTimer(_ => fired.Add("disposed"), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        disposed.Dispose();
        clock.FireAll();

        fired.ShouldBe(["soon", "late"]);
        clock.GetUtcNow().ShouldBe(wall);
        clock.GetTimestamp().ShouldBe(0);
        clock.Pending.ShouldBeEmpty("a one-shot timer that fired is no longer armed");
    }

    [Fact]
    public void Fire_all_rearms_a_periodic_timer_one_period_from_now()
    {
        var clock = new ManualClock();
        var fired = 0;

        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromHours(1), TimeSpan.FromSeconds(5));

        clock.FireAll();

        fired.ShouldBe(1);
        clock.Pending.ShouldBe([TimeSpan.FromSeconds(5)]);

        clock.Advance(TimeSpan.FromSeconds(5));

        fired.ShouldBe(2);
    }

    [Fact]
    public void A_timer_a_callback_arms_while_all_fire_waits_for_the_next_call()
    {
        var clock = new ManualClock();
        ITimer? second = null;

        using var first = clock.CreateTimer(
            _ => second = clock.CreateTimer(static _ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan),
            null,
            TimeSpan.FromHours(1),
            Timeout.InfiniteTimeSpan);

        clock.FireAll();

        second.ShouldNotBeNull();
        clock.Pending.ShouldBe([TimeSpan.FromSeconds(1)]);

        second.Dispose();
    }

    [Fact]
    public void Timer_origins_are_kept_only_when_the_clock_is_asked_to()
    {
        var plain = new ManualClock();
        var watched = new ManualClock { CaptureTimerOrigins = true };

        using var unnamed = plain.CreateTimer(static _ => { }, null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan);
        using var named = watched.CreateTimer(static _ => { }, null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan);

        plain.PendingOrigins.ShouldBeEmpty();
        watched.PendingOrigins.ShouldContain(nameof(Timer_origins_are_kept_only_when_the_clock_is_asked_to));
    }

    [Fact]
    public async Task A_timer_within_the_auto_fire_reach_fires_when_it_is_armed_and_a_timer_beyond_it_stays_armed()
    {
        var clock = new ManualClock { AutoFireWithin = TimeSpan.FromSeconds(1) };
        var wall = clock.GetUtcNow();
        var shortFired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var longFired = 0;

        using var shortTimer = clock.CreateTimer(_ => shortFired.TrySetResult(), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using var longTimer = clock.CreateTimer(_ => Interlocked.Increment(ref longFired), null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan);

        await shortFired.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        clock.Pending.ShouldBe([TimeSpan.FromHours(1)], "the short timer fired and is no longer armed; the long one is");
        Volatile.Read(ref longFired).ShouldBe(0);
        clock.GetUtcNow().ShouldBe(wall, "firing a timer does not move the clock");
    }

    [Fact]
    public async Task A_timer_that_is_re_armed_within_the_auto_fire_reach_fires_again()
    {
        var clock = new ManualClock { AutoFireWithin = TimeSpan.FromSeconds(1) };
        var fired = new SemaphoreSlim(0);

        using var timer = clock.CreateTimer(_ => fired.Release(), null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan);

        timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        (await fired.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken)).ShouldBeTrue();

        timer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        (await fired.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken)).ShouldBeTrue();

        clock.Pending.ShouldBeEmpty();
    }

    [Fact]
    public void A_clock_with_system_timers_makes_no_virtual_timer_and_fires_nothing_when_it_advances()
    {
        var start = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = ManualClock.WithSystemTimers(start);
        var fired = 0;

        using var timer = clock.CreateTimer(_ => Interlocked.Increment(ref fired), null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromHours(2));
        clock.FireAll();

        Volatile.Read(ref fired).ShouldBe(0, "an hour on the system's timer is an hour on the test's, whatever the wall clock reads");
        clock.Pending.ShouldBeEmpty();
        clock.CreatedTimers.ShouldBe(0);
        clock.RunningTimers.ShouldBe(0);
        clock.GetUtcNow().ShouldBe(start.AddHours(2));
    }

    [Fact]
    public async Task A_clock_with_system_timers_fires_a_timer_that_is_due_without_being_advanced()
    {
        var clock = ManualClock.WithSystemTimers(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timer = clock.CreateTimer(_ => fired.TrySetResult(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        await fired.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_timer_within_the_system_reach_runs_on_real_time_and_a_timer_beyond_it_stays_virtual()
    {
        var clock = new ManualClock { SystemTimersWithin = TimeSpan.FromMilliseconds(100) };
        var shortFired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var longFired = 0;

        using var shortTimer = clock.CreateTimer(_ => shortFired.TrySetResult(), null, TimeSpan.FromMilliseconds(20), Timeout.InfiniteTimeSpan);
        using var longTimer = clock.CreateTimer(_ => Interlocked.Increment(ref longFired), null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);

        await shortFired.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        Volatile.Read(ref longFired).ShouldBe(0, "five minutes are not passed by waiting");
        clock.Pending.ShouldBe([TimeSpan.FromMinutes(5)]);
        clock.CreatedTimers.ShouldBe(1, "the short timer is the system's and the clock does not count it");

        clock.Advance(TimeSpan.FromMinutes(5));

        Volatile.Read(ref longFired).ShouldBe(1);
    }

    [Fact]
    public async Task A_periodic_timer_within_the_system_reach_keeps_ticking_without_the_clock_being_advanced()
    {
        var clock = new ManualClock { SystemTimersWithin = TimeSpan.FromMilliseconds(100) };
        using var poll = new PeriodicTimer(TimeSpan.FromMilliseconds(10), clock);

        // A failure bound: a periodic timer that stayed virtual would never tick and the wait would never end.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(WaitUntil.DefaultTimeout); // cancel: bound

        for (var tick = 0; tick < 3; tick++)
        {
            (await poll.WaitForNextTickAsync(bound.Token)).ShouldBeTrue();
        }

        clock.CreatedTimers.ShouldBe(0);
        clock.Pending.ShouldBeEmpty();
    }

    [Fact]
    public void A_period_beyond_the_system_reach_makes_a_virtual_timer_even_when_its_first_wait_is_short()
    {
        var clock = new ManualClock { SystemTimersWithin = TimeSpan.FromMilliseconds(100) };
        var ticks = 0;

        using var timer = clock.CreateTimer(_ => Interlocked.Increment(ref ticks), null, TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(30));

        Volatile.Read(ref ticks).ShouldBe(0);
        clock.Pending.ShouldBe([TimeSpan.FromMilliseconds(20)]);

        clock.Advance(TimeSpan.FromMilliseconds(20));

        Volatile.Read(ref ticks).ShouldBe(1);
        clock.Pending.ShouldBe([TimeSpan.FromSeconds(30)]);
    }

    [Fact]
    public void A_clock_with_system_timers_moves_the_timestamp_only_when_it_is_told_to()
    {
        var manual = ManualClock.WithSystemTimers(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), systemTimestamp: false);
        var system = ManualClock.WithSystemTimers(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        manual.Advance(TimeSpan.FromSeconds(7));
        manual.StepMonotonic(TimeSpan.FromSeconds(3));

        manual.GetTimestamp().ShouldBe(TimeSpan.FromSeconds(10).Ticks);
        manual.TimestampFrequency.ShouldBe(TimeSpan.TicksPerSecond);

        system.Advance(TimeSpan.FromHours(1));

        system.TimestampFrequency.ShouldBe(TimeProvider.System.TimestampFrequency);
        system.GetTimestamp().ShouldBeLessThan(
            TimeProvider.System.GetTimestamp() + TimeProvider.System.TimestampFrequency * 3600,
            "the timestamp is the system's and the advance did not move it");
        system.GetUtcNow().ShouldBe(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(3600)]
    [InlineData(-3600)]
    [InlineData(0)]
    public void A_clock_over_the_system_clock_reads_the_system_wall_clock_off_by_its_offset(int offsetSeconds)
    {
        var clock = ManualClock.OverSystemClock(TimeSpan.FromSeconds(offsetSeconds));

        var skew = clock.GetUtcNow() - TimeProvider.System.GetUtcNow();

        skew.ShouldBeInRange(TimeSpan.FromSeconds(offsetSeconds - 60), TimeSpan.FromSeconds(offsetSeconds + 60));

        clock.StepWallClock(TimeSpan.FromHours(1));

        (clock.GetUtcNow() - TimeProvider.System.GetUtcNow())
            .ShouldBeInRange(TimeSpan.FromSeconds(offsetSeconds + 3540), TimeSpan.FromSeconds(offsetSeconds + 3660));
    }

    private static async Task WaitThreeTimesAsync(ManualClock clock)
    {
        await WaitOnAsync(clock, TimeSpan.FromSeconds(1));
        await WaitOnAsync(clock, TimeSpan.FromSeconds(2));
        await WaitOnAsync(clock, TimeSpan.FromSeconds(4));
    }

    // Waits one second, arms a second wait and says so, then waits two seconds more.
    private static async Task WaitTwiceAsync(ManualClock clock, TaskCompletionSource secondArmed)
    {
        await WaitOnAsync(clock, TimeSpan.FromSeconds(1));

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timer = clock.CreateTimer(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            done,
            TimeSpan.FromSeconds(2),
            Timeout.InfiniteTimeSpan);

        secondArmed.TrySetResult();

        await done.Task.ConfigureAwait(false);
    }

    private static async Task FailAfterAsync(ManualClock clock)
    {
        await WaitOnAsync(clock, TimeSpan.FromSeconds(3));

        throw new InvalidOperationException("the wait ended and the work failed");
    }

    /// <summary>Waits on the clock's own timer; the scan for real waits has nothing to count here.</summary>
    private static async Task WaitOnAsync(ManualClock clock, TimeSpan span)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timer = clock.CreateTimer(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            done,
            span,
            Timeout.InfiniteTimeSpan);

        await done.Task.ConfigureAwait(false);
    }
}
