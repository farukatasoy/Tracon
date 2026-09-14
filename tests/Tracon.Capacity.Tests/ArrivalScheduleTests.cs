using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>The open-loop send plan, which is what stops the driver lowering the load it claims.</summary>
public sealed class ArrivalScheduleTests
{
    [Fact]
    public void The_plan_is_fixed_before_the_window_opens()
    {
        var schedule = ArrivalSchedule.Fixed(ratePerSecond: 4, windowSeconds: 2);

        schedule.Planned.ShouldBe(8);
        schedule.DueSeconds(0).ShouldBe(0);
        schedule.DueSeconds(1).ShouldBe(0.25);
        schedule.DueSeconds(7).ShouldBe(1.75);
    }

    [Fact]
    public void A_fractional_tail_is_floored_rather_than_rounded_up()
    {
        // 3 requests/second over 2.5 seconds is 7.5; the eighth request would
        // be due after the window closed, so it is not planned at all.
        ArrivalSchedule.Fixed(3, 2.5).Planned.ShouldBe(7);
    }

    [Fact]
    public void A_zero_or_negative_rate_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ArrivalSchedule.Fixed(0, 10));
        Should.Throw<ArgumentOutOfRangeException>(() => ArrivalSchedule.Fixed(-1, 10));
        Should.Throw<ArgumentOutOfRangeException>(() => ArrivalSchedule.Fixed(1, 0));
    }

    [Fact]
    public void The_counters_close_both_identities_when_nothing_is_lost()
    {
        var tally = new ArrivalTally
        {
            Planned = 100,
            NotSent = 10,
            Sent = 90,
            Accepted = 70,
            Rejected = 12,
            Failed = 5,
            TimedOut = 3,
        };

        tally.Balances().ShouldBeTrue();
    }

    [Fact]
    public void A_dropped_request_breaks_the_identity_instead_of_disappearing()
    {
        // 🚨 This is the failure the apparatus exists to make impossible: a
        // request that was planned, never sent, and never counted as not sent.
        var tally = new ArrivalTally
        {
            Planned = 100,
            NotSent = 0,
            Sent = 90,
            Accepted = 90,
        };

        tally.Balances().ShouldBeFalse();
    }

    [Fact]
    public void Saturation_stays_inside_the_sent_identity_rather_than_being_forgotten()
    {
        // A 429 is a finding. It is counted as rejected, and rejected is part
        // of `sent`; it never quietly leaves the distribution.
        var tally = new ArrivalTally { Planned = 10, Sent = 10, Accepted = 4, Rejected = 6 };

        tally.Balances().ShouldBeTrue();
    }
}
