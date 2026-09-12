namespace Tracon.Core.UnitTests.Graph;

/// <summary>
/// Fail-fast validation for <see cref="TraconAgentGraphOptions.ChildDeadline"/>/
/// <see cref="TraconAgentGraphOptions.WaitTimeout"/> — the tree-wide
/// defaults for a sub-agent's two-layer wait limit (144.1).
/// </summary>
public sealed class AgentGraphWaitLimitValidationTests
{
    [Fact]
    public void Default_options_are_accepted()
    {
        var result = new TraconOptionsValidator().Validate(null, new TraconOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_ChildDeadline_is_rejected(int seconds)
    {
        var options = new TraconOptions();
        options.AgentGraph.ChildDeadline = TimeSpan.FromSeconds(seconds);

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(
            failure => failure.Contains(nameof(TraconAgentGraphOptions.ChildDeadline), StringComparison.Ordinal));
    }

    [Fact]
    public void A_WaitTimeout_equal_to_ChildDeadline_is_rejected()
    {
        var options = new TraconOptions();
        options.AgentGraph.ChildDeadline = TimeSpan.FromSeconds(30);
        options.AgentGraph.WaitTimeout = TimeSpan.FromSeconds(30);

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(
            failure => failure.Contains(nameof(TraconAgentGraphOptions.WaitTimeout), StringComparison.Ordinal));
    }

    [Fact]
    public void A_WaitTimeout_below_ChildDeadline_is_rejected()
    {
        var options = new TraconOptions();
        options.AgentGraph.ChildDeadline = TimeSpan.FromSeconds(30);
        options.AgentGraph.WaitTimeout = TimeSpan.FromSeconds(10);

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Raising_ChildDeadline_alone_keeps_WaitTimeout_strictly_greater_by_construction()
    {
        // The WaitTimeout getter tracks ChildDeadline + a fixed pad when never
        // set explicitly - this is what guarantees the invariant without the
        // caller having to also update WaitTimeout.
        var options = new TraconOptions();
        options.AgentGraph.ChildDeadline = TimeSpan.FromMinutes(10);

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
        options.AgentGraph.WaitTimeout.ShouldBeGreaterThan(options.AgentGraph.ChildDeadline);
    }

    [Fact]
    public void Setting_WaitTimeout_pins_it_instead_of_tracking_ChildDeadline()
    {
        var options = new TraconAgentGraphOptions
        {
            ChildDeadline = TimeSpan.FromSeconds(10),
            WaitTimeout = TimeSpan.FromMinutes(5),
        };

        options.ChildDeadline = TimeSpan.FromSeconds(20);

        options.WaitTimeout.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void A_ChildDeadline_near_TimeSpanMaxValue_does_not_overflow_the_derived_WaitTimeout()
    {
        // TimeSpan + TimeSpan throws OverflowException past MaxValue; a naive
        // "ChildDeadline + pad" getter would crash on READ alone, before
        // validation even runs. The derived value clamps instead.
        var options = new TraconAgentGraphOptions { ChildDeadline = TimeSpan.MaxValue };

        var waitTimeout = options.WaitTimeout;

        waitTimeout.ShouldBe(TimeSpan.MaxValue);

        // The clamp makes WaitTimeout == ChildDeadline at this extreme, which
        // the validator correctly rejects rather than silently accepting a
        // hard cutoff that can never fire after the cooperative one.
        var result = new TraconOptionsValidator().Validate(null, new TraconOptions { AgentGraph = options });

        result.Succeeded.ShouldBeFalse();
    }
}
