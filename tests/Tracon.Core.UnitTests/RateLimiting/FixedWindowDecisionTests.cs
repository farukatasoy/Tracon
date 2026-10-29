namespace Tracon.Core.UnitTests.RateLimiting;

/// <summary>Verifies the fixed-window arithmetic both rate limiters share.</summary>
public sealed class FixedWindowDecisionTests
{
    private static readonly DateTimeOffset Minute = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_moment_of_a_window_aligns_to_the_same_start()
    {
        var window = TimeSpan.FromMinutes(1);

        FixedWindowDecision.AlignWindowStart(Minute, window).ShouldBe(Minute);
        FixedWindowDecision.AlignWindowStart(Minute.AddSeconds(37.5), window).ShouldBe(Minute);
        FixedWindowDecision.AlignWindowStart(Minute.AddTicks(TimeSpan.TicksPerMinute - 1), window).ShouldBe(Minute);
        FixedWindowDecision.AlignWindowStart(Minute.AddMinutes(1), window).ShouldBe(Minute.AddMinutes(1));
    }

    [Fact]
    public void Two_instances_with_the_same_moment_in_different_offsets_agree()
    {
        var utc = Minute.AddSeconds(10);
        var local = utc.ToOffset(TimeSpan.FromHours(3));

        FixedWindowDecision.AlignWindowStart(local, TimeSpan.FromMinutes(1)).ShouldBe(
            FixedWindowDecision.AlignWindowStart(utc, TimeSpan.FromMinutes(1)));
        FixedWindowDecision.AlignWindowStart(local, TimeSpan.FromMinutes(1)).Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Windows_are_cut_from_the_unix_epoch()
    {
        var window = TimeSpan.FromSeconds(45);
        var start = FixedWindowDecision.AlignWindowStart(Minute.AddSeconds(100), window);

        ((start.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) % window.Ticks).ShouldBe(0);
        start.ShouldBeLessThanOrEqualTo(Minute.AddSeconds(100));
        (Minute.AddSeconds(100) - start).ShouldBeLessThan(window);
    }

    [Fact]
    public void A_moment_before_the_epoch_aligns_down_and_not_toward_zero()
    {
        var before = DateTimeOffset.UnixEpoch.AddSeconds(-10);

        FixedWindowDecision.AlignWindowStart(before, TimeSpan.FromMinutes(1)).ShouldBe(DateTimeOffset.UnixEpoch.AddMinutes(-1));
    }

    [Fact]
    public void A_count_up_to_the_limit_is_allowed_and_the_next_one_is_not()
    {
        var window = TimeSpan.FromMinutes(1);

        FixedWindowDecision.Decide(Counted(Minute, 5), 5, window, Minute.AddSeconds(10)).Allowed.ShouldBeTrue();
        FixedWindowDecision.Decide(Counted(Minute, 6), 5, window, Minute.AddSeconds(10)).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Retry_after_is_the_time_to_the_end_of_the_window_in_whole_seconds()
    {
        var decision = FixedWindowDecision.Decide(
            Counted(Minute, 6), 5, TimeSpan.FromMinutes(1), Minute.AddSeconds(10.2));

        // 49.8 s are left; the client is told 50.
        decision.RetryAfter.ShouldBe(TimeSpan.FromSeconds(50));
    }

    [Fact]
    public void Retry_after_is_at_least_one_second()
    {
        // The window ends in 100 ms (or has just ended, on a store that ran ahead): never "0".
        FixedWindowDecision.Decide(Counted(Minute, 6), 5, TimeSpan.FromMinutes(1), Minute.AddSeconds(59.9))
            .RetryAfter.ShouldBe(TimeSpan.FromSeconds(1));
        FixedWindowDecision.Decide(Counted(Minute, 6), 5, TimeSpan.FromMinutes(1), Minute.AddSeconds(70))
            .RetryAfter.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_stored_window_ahead_of_this_clock_lengthens_retry_after()
    {
        // This instance's clock is slow: the store holds the next window. The wait is measured from the window
        // the store returned, not from the one this instance computed.
        var decision = FixedWindowDecision.Decide(
            Counted(Minute.AddMinutes(1), 6), 5, TimeSpan.FromMinutes(1), Minute.AddSeconds(50));

        decision.RetryAfter.ShouldBe(TimeSpan.FromSeconds(70));
    }

    [Fact]
    public void An_allowed_decision_carries_no_wait()
    {
        FixedWindowDecision.Decide(Counted(Minute, 1), 5, TimeSpan.FromMinutes(1), Minute).RetryAfter.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_count_above_the_int_range_is_still_rejected()
    {
        FixedWindowDecision.Decide(Counted(Minute, long.MaxValue), int.MaxValue, TimeSpan.FromMinutes(1), Minute)
            .Allowed.ShouldBeFalse();
    }

    [Fact]
    public void A_permit_limit_below_one_is_one_and_a_window_that_is_not_positive_is_one_minute()
    {
        var options = new TraconRateLimitOptions { PermitLimit = -4, Window = TimeSpan.Zero };

        FixedWindowDecision.EffectivePermitLimit(options).ShouldBe(1);
        FixedWindowDecision.EffectiveWindow(options).ShouldBe(TimeSpan.FromMinutes(1));

        options.PermitLimit = 7;
        options.Window = TimeSpan.FromSeconds(5);

        FixedWindowDecision.EffectivePermitLimit(options).ShouldBe(7);
        FixedWindowDecision.EffectiveWindow(options).ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_very_large_window_does_not_overflow()
    {
        var window = TimeSpan.FromDays(365 * 100);

        var start = FixedWindowDecision.AlignWindowStart(Minute, window);

        start.ShouldBeLessThanOrEqualTo(Minute);
        (Minute - start).ShouldBeLessThan(window);
    }

    private static RateLimitWindowCount Counted(DateTimeOffset windowStart, long count)
        => new() { WindowStart = windowStart, Count = count };
}
