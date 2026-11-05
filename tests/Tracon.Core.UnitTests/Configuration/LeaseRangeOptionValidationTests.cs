using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The options that become the duration a store call is given must stop the host at startup when the store would
/// refuse it, and not fail on every scan or every retry afterwards.
/// </summary>
/// <remarks>
/// The stores accept one millisecond to a little over 68 years for a threshold, and zero to the same for a retry delay.
/// The orphan scan and the webhook ladder read their durations from options, so the options carry the same range. The
/// numbers are written out here and not read from the product: a limit that moves must break these tests.
/// </remarks>
public sealed class LeaseRangeOptionValidationTests
{
    private const long LongestMilliseconds = 2_147_483_647_000L;

    public static TheoryData<TimeSpan> RefusedThresholds => new()
    {
        TimeSpan.MaxValue,
        TimeSpan.FromMilliseconds(LongestMilliseconds + 1),
        TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 2),
    };

    [Theory]
    [MemberData(nameof(RefusedThresholds))]
    public void An_orphan_threshold_outside_the_run_stores_range_is_rejected_and_named(TimeSpan threshold)
    {
        var result = new RunReconciliationOptionsValidator().Validate(
            name: null,
            new RunReconciliationOptions { OrphanThreshold = threshold });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(RunReconciliationOptions.OrphanThreshold));
        result.FailureMessage.ShouldContain("68 years");
    }

    [Fact]
    public void The_longest_orphan_threshold_the_store_accepts_is_accepted()
    {
        var result = new RunReconciliationOptionsValidator().Validate(
            name: null,
            new RunReconciliationOptions { OrphanThreshold = TimeSpan.FromMilliseconds(LongestMilliseconds) });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_webhook_retry_delay_longer_than_the_job_queue_accepts_is_rejected_and_named()
    {
        var options = new TraconWebhookOptions();
        options.RetryDelays.Clear();
        options.RetryDelays.Add(TimeSpan.FromMinutes(1));
        options.RetryDelays.Add(TimeSpan.FromMilliseconds(LongestMilliseconds + 1));

        var result = new TraconWebhookOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(TraconWebhookOptions.RetryDelays));
        result.FailureMessage.ShouldContain("68 years");
    }

    [Fact]
    public void The_longest_webhook_retry_delay_the_job_queue_accepts_is_accepted()
    {
        var options = new TraconWebhookOptions();
        options.RetryDelays.Clear();
        options.RetryDelays.Add(TimeSpan.FromMilliseconds(LongestMilliseconds));

        new TraconWebhookOptionsValidator().Validate(name: null, options).Succeeded.ShouldBeTrue();
    }
}
