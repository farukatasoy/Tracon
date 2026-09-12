using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Graph;

/// <summary>
/// The duration dimension <see cref="AgentRunBudget"/> gained in phase 128 —
/// <see cref="AgentRunBudgetTests"/> and <see cref="AgentRunBudgetCostTests"/>
/// cover the pre-existing token/count/cost dimensions.
/// </summary>
public sealed class AgentRunBudgetDurationTests
{
    [Fact]
    public void No_duration_limit_is_never_exhausted()
    {
        var clock = new ManualTimeProvider();
        var budget = new AgentRunBudget(maxDuration: null, clock);

        clock.Advance(TimeSpan.FromDays(365));

        budget.IsDurationBudgetExhausted.ShouldBeFalse();
        budget.IsExhausted.ShouldBeFalse();
    }

    [Fact]
    public void Deadline_is_computed_once_at_construction_from_the_given_TimeProvider()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);

        var budget = new AgentRunBudget(TimeSpan.FromMinutes(5), clock);

        budget.MaxDuration.ShouldBe(TimeSpan.FromMinutes(5));
        budget.Deadline.ShouldBe(start + TimeSpan.FromMinutes(5));

        // Advancing the clock after construction must not move the deadline:
        // it was computed ONCE, not re-derived from MaxDuration on every read.
        clock.Advance(TimeSpan.FromHours(1));
        budget.Deadline.ShouldBe(start + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Duration_limit_trips_once_the_deadline_passes()
    {
        var clock = new ManualTimeProvider();
        var budget = new AgentRunBudget(TimeSpan.FromMinutes(5), clock);

        clock.Advance(TimeSpan.FromMinutes(4));
        budget.IsDurationBudgetExhausted.ShouldBeFalse();
        budget.IsExhausted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(1));
        budget.IsDurationBudgetExhausted.ShouldBeTrue();
        budget.IsExhausted.ShouldBeTrue();
        budget.DescribeModelCallExhaustion().ShouldContain("time budget is exhausted", Case.Sensitive);
    }

    [Fact]
    public void TryReserveRun_also_refuses_once_the_deadline_has_passed()
    {
        // 128.2: "TryReserveRun (child run start) also checks the deadline: no
        // new child run starts in a tree whose time has run out."
        var clock = new ManualTimeProvider();
        var budget = new AgentRunBudget(TimeSpan.FromMinutes(5), clock);

        clock.Advance(TimeSpan.FromMinutes(5));

        budget.TryReserveRun().ShouldBeFalse();
        budget.DescribeExhaustion().ShouldContain("time budget is exhausted", Case.Sensitive);
    }

    [Fact]
    public void Zero_or_negative_MaxDuration_removes_the_limit_via_CreateBudget()
    {
        var clock = new ManualTimeProvider();

        var zero = new TraconAgentGraphOptions { MaxDuration = TimeSpan.Zero }.CreateBudget(clock);
        var negative = new TraconAgentGraphOptions { MaxDuration = TimeSpan.FromSeconds(-1) }.CreateBudget(clock);

        zero.MaxDuration.ShouldBeNull();
        zero.Deadline.ShouldBeNull();
        negative.MaxDuration.ShouldBeNull();
        negative.Deadline.ShouldBeNull();
    }

    [Fact]
    public void Positive_MaxDuration_carries_through_CreateBudget()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);

        var budget = new TraconAgentGraphOptions { MaxDuration = TimeSpan.FromMinutes(10) }.CreateBudget(clock);

        budget.MaxDuration.ShouldBe(TimeSpan.FromMinutes(10));
        budget.Deadline.ShouldBe(start + TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Zero_MaxDuration_given_directly_to_AgentRunBudget_is_immediately_exhausted()
    {
        // Unlike TraconAgentGraphOptions.CreateBudget (where zero/negative
        // means "no limit"), AgentRunBudget itself takes MaxDuration at face
        // value — the same way a raw MaxTotalTokens = 0 reads as "exhausted
        // from the start", not "unlimited".
        var clock = new ManualTimeProvider();
        var budget = new AgentRunBudget(TimeSpan.Zero, clock);

        budget.IsDurationBudgetExhausted.ShouldBeTrue();
    }

    [Fact]
    public void Negative_MaxDuration_given_directly_produces_an_already_passed_deadline()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);
        var budget = new AgentRunBudget(TimeSpan.FromSeconds(-5), clock);

        budget.Deadline.ShouldBe(start - TimeSpan.FromSeconds(5));
        budget.IsDurationBudgetExhausted.ShouldBeTrue();
    }

    [Fact]
    public void TimeSpan_MaxValue_does_not_overflow_DateTimeOffset()
    {
        var clock = new ManualTimeProvider();

        var budget = new AgentRunBudget(TimeSpan.MaxValue, clock);

        budget.Deadline.ShouldBe(DateTimeOffset.MaxValue);
        budget.IsDurationBudgetExhausted.ShouldBeFalse();
    }

    [Fact]
    public void Default_TimeProvider_is_the_system_clock()
    {
        var before = DateTimeOffset.UtcNow;
        var budget = new AgentRunBudget(TimeSpan.FromMinutes(1));
        var after = DateTimeOffset.UtcNow;

        budget.Deadline.ShouldNotBeNull();
        budget.Deadline!.Value.ShouldBeInRange(before + TimeSpan.FromMinutes(1), after + TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void DescribeModelCallExhaustion_and_IsExhausted_derive_from_the_SAME_deadline_check()
    {
        // K-483's class: the message-producing code and the exhaustion check
        // must read the same source, or a third dimension can drift out of
        // sync with the message that describes it.
        var clock = new ManualTimeProvider();
        var budget = new AgentRunBudget(TimeSpan.FromSeconds(30), clock);

        clock.Advance(TimeSpan.FromSeconds(29));
        budget.IsExhausted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        budget.IsExhausted.ShouldBeTrue();
        budget.DescribeModelCallExhaustion().ShouldContain("Tracon:AgentGraph:MaxDuration", Case.Sensitive);
    }
}
