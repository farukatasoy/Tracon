using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// How long the circuit breaker stays open when the wall clock is stepped while it is open.
/// </summary>
/// <remarks>
/// A break is a duration, so it is measured on the monotonic clock. Measured on the wall clock, a clock that is
/// corrected backwards (a time sync step, a resumed virtual machine) kept the circuit open for the whole step on top of
/// the break: a provider that had recovered stayed refused for as long as the clock had gone back.
/// </remarks>
public sealed class ModelProviderCircuitBreakerClockTests
{
    private const string Provider = "clock-provider";

    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    [Fact]
    public void A_wall_clock_stepped_back_does_not_keep_the_circuit_open_longer_than_the_break()
    {
        var time = new ManualClock();
        var breaker = Breaker(time);

        breaker.RecordFailure(Provider);
        Should.Throw<TraconProviderUnavailableException>(() => breaker.EnsureRequestAllowed(Provider));

        // The wall clock goes back an hour; then the break runs out in real elapsed time.
        time.StepWallClock(TimeSpan.FromHours(-1));
        time.Advance(BreakDuration + TimeSpan.FromSeconds(1));

        Should.NotThrow(
            () => breaker.EnsureRequestAllowed(Provider),
            "31 seconds really passed since the circuit opened; the break is 30");
    }

    [Fact]
    public void A_wall_clock_stepped_forward_does_not_close_the_circuit_early()
    {
        var time = new ManualClock();
        var breaker = Breaker(time);

        breaker.RecordFailure(Provider);

        // The wall clock jumps an hour ahead; ten seconds really pass.
        time.StepWallClock(TimeSpan.FromHours(1));
        time.Advance(TimeSpan.FromSeconds(10));

        Should.Throw<TraconProviderUnavailableException>(
            () => breaker.EnsureRequestAllowed(Provider),
            "ten seconds of a thirty-second break have passed");
    }

    [Fact]
    public void The_remaining_break_the_health_report_gives_follows_the_elapsed_time_not_the_wall_clock()
    {
        var time = new ManualClock();
        var breaker = Breaker(time);

        breaker.RecordFailure(Provider);
        time.StepWallClock(TimeSpan.FromHours(-1));
        time.Advance(TimeSpan.FromSeconds(10));

        breaker.IsOpen(Provider, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldNotBeNull().ShouldBe(TimeSpan.FromSeconds(20));
    }

    private static ModelProviderCircuitBreaker Breaker(TimeProvider time)
    {
        var services = new ServiceCollection();
        services.Configure<TraconOptions>(options =>
        {
            options.CircuitBreaker.FailureThreshold = 1;
            options.CircuitBreaker.BreakDuration = BreakDuration;
        });

        return new ModelProviderCircuitBreaker(
            services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<TraconOptions>>(),
            time);
    }
}
