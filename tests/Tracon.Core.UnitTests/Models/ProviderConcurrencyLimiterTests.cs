using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>Verifies the per-provider outgoing concurrency limit (phase 62, F-44).</summary>
public sealed class ProviderConcurrencyLimiterTests
{
    [Fact]
    public async Task Unlimited_by_default_never_waits()
    {
        var limiter = new ProviderConcurrencyLimiter(new StaticOptionsMonitor<TraconOptions>(new TraconOptions()));

        var lease = await limiter.AcquireAsync("openai", TestContext.Current.CancellationToken);

        lease.ShouldBeNull();
    }

    [Fact]
    public async Task A_limit_of_one_serializes_two_concurrent_callers()
    {
        var limiter = LimiterWithLimit(1);

        var first = await limiter.AcquireAsync("openai", TestContext.Current.CancellationToken);
        first.ShouldNotBeNull();

        var secondTask = limiter.AcquireAsync("openai", TestContext.Current.CancellationToken).AsTask();

        // The second caller must be genuinely BLOCKED, not merely slow: give it
        // a short window and confirm it has not completed.
        var completedEarly = await Task.WhenAny(secondTask, Task.Delay(100, TestContext.Current.CancellationToken)); // delay: negative
        completedEarly.ShouldNotBe(secondTask);

        first!.Dispose();

        var second = await secondTask;
        second.ShouldNotBeNull();
        second!.Dispose();
    }

    [Fact]
    public async Task A_second_caller_can_still_acquire_after_the_first_releases()
    {
        var limiter = LimiterWithLimit(1);

        var first = await limiter.AcquireAsync("openai", TestContext.Current.CancellationToken);
        first!.Dispose();

        var second = await limiter.AcquireAsync("openai", TestContext.Current.CancellationToken);

        second.ShouldNotBeNull();
        second!.Dispose();
    }

    [Fact]
    public async Task A_canceled_wait_does_not_leak_the_held_slot()
    {
        var limiter = LimiterWithLimit(1);

        var first = await limiter.AcquireAsync("openai", TestContext.Current.CancellationToken);

        using var waiterCancellation = new CancellationTokenSource();
        var waiterTask = limiter.AcquireAsync("openai", waiterCancellation.Token).AsTask();

        waiterCancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await waiterTask);

        first!.Dispose();

        // If the canceled wait had consumed a permit on the way out, this
        // would hang; the test's own timeout would catch that.
        var third = await limiter.AcquireAsync("openai", TestContext.Current.CancellationToken);
        third.ShouldNotBeNull();
        third!.Dispose();
    }

    [Fact]
    public async Task Different_providers_have_independent_limits()
    {
        var limiter = LimiterWithLimit(1);

        var openai = await limiter.AcquireAsync("openai", TestContext.Current.CancellationToken);
        var anthropic = await limiter.AcquireAsync("anthropic", TestContext.Current.CancellationToken);

        openai.ShouldNotBeNull();
        anthropic.ShouldNotBeNull();

        openai!.Dispose();
        anthropic!.Dispose();
    }

    private static ProviderConcurrencyLimiter LimiterWithLimit(int limit)
        => new(new StaticOptionsMonitor<TraconOptions>(
            new TraconOptions { ModelConcurrency = new TraconModelConcurrencyOptions { MaxConcurrentCallsPerProvider = limit } }));
}
