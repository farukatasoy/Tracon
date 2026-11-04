using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The poll loop of <see cref="JobWorkerBackgroundService"/> on a manual clock: a tick happens when the test advances
/// the clock, so the number of lease calls the store sees is exactly the number of ticks the test gave.
/// </summary>
/// <remarks>
/// <para>
/// The same questions <c>HostedTickLoopTests</c> asks of the core package's other loops: the loop outlives a failing
/// call, makes the call again only when its timer fires (no hot spin), and ends in bounded time when the host stops.
/// A poll that waited on the system clock could not be asked any of them without waiting for real time.
/// </para>
/// <para>
/// Added with them: the interval is read on every tick, so a change of <c>PollInterval</c> applies from the next
/// tick and not from the next start.
/// </para>
/// </remarks>
public sealed class JobWorkerTickLoopTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_failing_lease_does_not_end_the_loop_and_is_made_again_only_on_the_next_tick()
    {
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore());
        var calls = 0;
        var fail = true;

        store.BeforeLease = _ =>
        {
            Interlocked.Increment(ref calls);

            return fail ? throw new IOException("the job store is unreachable (probe)") : Task.CompletedTask;
        };

        var (worker, host) = Worker(store, new TokenProbeHandler(), o => o.PollInterval = Interval, clock: clock);

        using (host)
        using (worker)
        {
            await worker.StartAsync(TestContext.Current.CancellationToken);
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(Interval), "the worker to arm its poll timer");

            // Nothing advanced the clock: a loop that polled on the system clock, or spun, would show a call here.
            Volatile.Read(ref calls).ShouldBe(0, "the worker leased before its first tick");

            for (var tick = 1; tick <= 3; tick++)
            {
                clock.Advance(Interval);
                await WaitUntil.TrueAsync(() => Volatile.Read(ref calls) >= tick, $"the worker to lease on tick {tick}");
            }

            // A hot spin shows here: nothing advanced the clock, so no call may follow the third one.
            await Task.Delay(150, TestContext.Current.CancellationToken); // delay: negative
            Volatile.Read(ref calls).ShouldBe(3, "the worker leased without a tick");

            // The loop is alive: once the store answers, the next tick reaches it.
            fail = false;
            clock.Advance(Interval);
            await WaitUntil.TrueAsync(() => Volatile.Read(ref calls) >= 4, "the worker to lease from the recovered store");

            await worker.StopAsync(TestContext.Current.CancellationToken).WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_lease_that_never_answers_does_not_keep_the_host_from_stopping()
    {
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore());
        var calls = 0;

        store.BeforeLease = async token =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false); // delay: simulated
        };

        var (worker, host) = Worker(store, new TokenProbeHandler(), o => o.PollInterval = Interval, clock: clock);

        using (host)
        using (worker)
        {
            await worker.StartAsync(TestContext.Current.CancellationToken);
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(Interval), "the worker to arm its poll timer");

            clock.Advance(Interval);
            await WaitUntil.TrueAsync(() => Volatile.Read(ref calls) >= 1, "the worker to reach the lease that hangs");

            await worker.StopAsync(CancellationToken.None).WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_change_of_the_poll_interval_applies_from_the_next_tick()
    {
        var shorter = TimeSpan.FromSeconds(10);
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore());
        var calls = 0;
        TraconSchedulingOptions? options = null;

        store.BeforeLease = _ =>
        {
            Interlocked.Increment(ref calls);

            return Task.CompletedTask;
        };

        var (worker, host) = Worker(
            store,
            new TokenProbeHandler(),
            configured =>
            {
                configured.PollInterval = Interval;
                options = configured;
            },
            clock: clock);

        using (host)
        using (worker)
        {
            await worker.StartAsync(TestContext.Current.CancellationToken);
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(Interval), "the worker to arm its poll timer");

            // A value a timer cannot take is not applied: the worker keeps the period it has.
            options!.PollInterval = TimeSpan.FromTicks(1);
            clock.Advance(Interval);
            await WaitUntil.TrueAsync(() => Volatile.Read(ref calls) >= 1, "the first tick");
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(Interval), "the timer to be armed again");

            options.PollInterval = shorter;
            clock.Advance(Interval);
            await WaitUntil.TrueAsync(() => Volatile.Read(ref calls) >= 2, "the second tick");

            // The second tick found the new interval, so the third comes after it and not after the old one.
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(shorter), "the timer to follow the new interval");

            clock.Advance(shorter);
            await WaitUntil.TrueAsync(() => Volatile.Read(ref calls) >= 3, "the third tick, one new interval after the second");

            await worker.StopAsync(TestContext.Current.CancellationToken).WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        }
    }
}
