namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// A database-backed gauge refreshes on a <see cref="PeriodicTimer"/>, and that
/// timer accepts only a period from one millisecond to about 49.7 days. An
/// enabled gauge with any other interval must stop the host at startup with a
/// readable message, not fault its refresher after startup.
/// </summary>
public sealed class GaugeRefreshIntervalValidationTests
{
    [Theory]
    [MemberData(nameof(RejectedIntervals))]
    public void An_enabled_quota_gauge_rejects_an_interval_the_timer_cannot_take(TimeSpan interval)
    {
        var options = new TraconOptions();
        options.Observability.EnableQuotaUsageGauge = true;
        options.Observability.QuotaUsageRefreshInterval = interval;

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure =>
            failure.Contains(nameof(TraconObservabilityOptions.QuotaUsageRefreshInterval), StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RejectedIntervals))]
    public void An_enabled_queue_depth_gauge_rejects_an_interval_the_timer_cannot_take(TimeSpan interval)
    {
        var options = new TraconOptions();
        options.Observability.EnableJobQueueDepthGauge = true;
        options.Observability.JobQueueDepthRefreshInterval = interval;

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure =>
            failure.Contains(nameof(TraconObservabilityOptions.JobQueueDepthRefreshInterval), StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RejectedIntervals))]
    public void Every_rejected_interval_is_one_the_timer_itself_refuses(TimeSpan interval)
    {
        // The validator mirrors the timer's own range; if the runtime ever
        // widens that range, this test says so instead of the validator
        // silently refusing a value the timer would take.
        Should.Throw<ArgumentOutOfRangeException>(() => new PeriodicTimer(interval, TimeProvider.System).Dispose());
    }

    [Theory]
    [MemberData(nameof(AcceptedIntervals))]
    public void An_enabled_gauge_accepts_every_interval_the_timer_takes(TimeSpan interval)
    {
        var options = new TraconOptions();
        options.Observability.EnableQuotaUsageGauge = true;
        options.Observability.QuotaUsageRefreshInterval = interval;
        options.Observability.EnableJobQueueDepthGauge = true;
        options.Observability.JobQueueDepthRefreshInterval = interval;

        new TraconOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();

        using var timer = new PeriodicTimer(interval, TimeProvider.System);
    }

    [Fact]
    public void A_disabled_gauge_does_not_check_its_interval()
    {
        // Off creates no timer, so an unused zero must not break an existing setup.
        var options = new TraconOptions();
        options.Observability.QuotaUsageRefreshInterval = TimeSpan.Zero;
        options.Observability.JobQueueDepthRefreshInterval = TimeSpan.Zero;

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    public static TheoryData<TimeSpan> RejectedIntervals =>
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(-1),
        TimeSpan.FromMilliseconds(0.5),
        TimeSpan.FromDays(50),
    ];

    public static TheoryData<TimeSpan> AcceptedIntervals =>
    [
        TimeSpan.FromMilliseconds(1),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromDays(49),
    ];
}
