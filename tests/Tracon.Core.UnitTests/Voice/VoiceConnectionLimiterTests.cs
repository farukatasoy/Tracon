namespace Tracon.Core.UnitTests.Voice;

/// <summary>
/// Validates the per-tenant concurrent voice connection limit.
/// </summary>
public sealed class VoiceConnectionLimiterTests
{
    [Fact]
    public void New_connection_is_rejected_once_the_limit_is_full()
    {
        var limiter = new VoiceConnectionLimiter(2);

        using var first = limiter.TryAcquire("tenant-a").ShouldNotBeNull();
        using var second = limiter.TryAcquire("tenant-a").ShouldNotBeNull();

        limiter.TryAcquire("tenant-a").ShouldBeNull();
        limiter.CountFor("tenant-a").ShouldBe(2);
    }

    [Fact]
    public void Limit_is_per_tenant()
    {
        var limiter = new VoiceConnectionLimiter(1);

        using var first = limiter.TryAcquire("tenant-a").ShouldNotBeNull();

        limiter.TryAcquire("tenant-b").ShouldNotBeNull().Dispose();
    }

    [Fact]
    public void Disposed_slot_is_freed()
    {
        var limiter = new VoiceConnectionLimiter(1);

        var lease = limiter.TryAcquire("tenant-a").ShouldNotBeNull();
        limiter.TryAcquire("tenant-a").ShouldBeNull();

        lease.Dispose();

        limiter.CountFor("tenant-a").ShouldBe(0);
        limiter.TryAcquire("tenant-a").ShouldNotBeNull().Dispose();
    }

    [Fact]
    public void Second_dispose_does_not_drive_the_counter_negative()
    {
        var limiter = new VoiceConnectionLimiter(1);
        var lease = limiter.TryAcquire("tenant-a").ShouldNotBeNull();

        lease.Dispose();
        lease.Dispose();

        limiter.CountFor("tenant-a").ShouldBe(0);
    }

    [Fact]
    public void Concurrent_requests_do_not_exceed_the_limit()
    {
        // 🚨 Without compare-and-swap, two requests could read the same value
        // and both see a result that does not exceed the limit.
        var limiter = new VoiceConnectionLimiter(10);
        var leases = new VoiceConnectionLease?[64];

        Parallel.For(0, leases.Length, index => leases[index] = limiter.TryAcquire("tenant-a"));

        leases.Count(static lease => lease is not null).ShouldBe(10);
        limiter.CountFor("tenant-a").ShouldBe(10);

        foreach (var lease in leases)
        {
            lease?.Dispose();
        }

        limiter.CountFor("tenant-a").ShouldBe(0);
    }

    [Fact]
    public void Zero_or_negative_limit_is_raised_to_at_least_one()
    {
        // A zero limit would silently disable the capability; misconfiguration
        // must not turn into an invisible outage.
        new VoiceConnectionLimiter(0).Limit.ShouldBe(1);
        new VoiceConnectionLimiter(-5).Limit.ShouldBe(1);
    }
}
