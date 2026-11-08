using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The rate limit filter builds its local limiter only when a request needs it. A limiter runs a timer from the moment
/// it exists, so one built while the limit is off (the default) wakes the process about ten times a second for nothing.
/// </summary>
/// <remarks>
/// The filter is driven directly: whether it built its limiter is its own state, and the HTTP behaviour on top of it
/// (no 429 by default, 429 when the limit is on, the shared counter) is pinned by <see cref="RateLimitTests"/> and
/// <see cref="DeploymentRateLimitTests"/> against a running host.
/// </remarks>
public sealed class RateLimitFilterLazyTests
{
    private static readonly EndpointFilterDelegate Next = static _ => ValueTask.FromResult<object?>("handled");

    [Fact]
    public async Task A_limit_that_is_off_never_builds_its_limiter()
    {
        using var filter = Filter(new TraconRateLimitOptions { Enabled = false });

        for (var request = 0; request < 20; request++)
        {
            (await filter.InvokeAsync(Invocation(), Next)).ShouldBe("handled");
        }

        filter.HasLocalLimiter.ShouldBeFalse();
    }

    [Fact]
    public async Task A_limit_that_is_on_builds_its_limiter_at_the_first_request_and_rejects_past_the_limit()
    {
        using var filter = Filter(new TraconRateLimitOptions { Enabled = true, PermitLimit = 2, Window = TimeSpan.FromMinutes(5) });

        filter.HasLocalLimiter.ShouldBeFalse();

        (await filter.InvokeAsync(Invocation(), Next)).ShouldBe("handled");
        (await filter.InvokeAsync(Invocation(), Next)).ShouldBe("handled");
        var rejected = await filter.InvokeAsync(Invocation(), Next);

        filter.HasLocalLimiter.ShouldBeTrue();
        rejected.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task Parallel_first_requests_count_in_one_limiter()
    {
        // Two limiters built by a race would each admit the limit, so more than the limit would pass.
        const int Limit = 10;
        using var filter = Filter(new TraconRateLimitOptions { Enabled = true, PermitLimit = Limit, Window = TimeSpan.FromMinutes(5) });

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(async () => await filter.InvokeAsync(Invocation(), Next))));

        results.Count(static result => result is "handled").ShouldBe(Limit);
    }

    [Fact]
    public async Task A_failing_deployment_store_falls_back_to_the_local_limiter()
    {
        using var filter = Filter(
            new TraconRateLimitOptions
            {
                Enabled = true,
                Scope = RateLimitScope.Deployment,
                PermitLimit = 1,
                Window = TimeSpan.FromMinutes(5),
            },
            new FailingCounterStore());

        (await filter.InvokeAsync(Invocation(), Next)).ShouldBe("handled");
        var rejected = await filter.InvokeAsync(Invocation(), Next);

        filter.HasLocalLimiter.ShouldBeTrue();
        rejected.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public void Disposing_a_filter_that_never_limited_builds_no_limiter()
    {
        var filter = Filter(new TraconRateLimitOptions { Enabled = true });

        filter.Dispose();

        filter.HasLocalLimiter.ShouldBeFalse();
    }

    private static TraconRateLimitFilter Filter(TraconRateLimitOptions options, IRateLimitCounterStore? store = null)
        => new(
            RateLimitGroups.Management,
            new FixedOptionsMonitor<TraconRateLimitOptions>(options),
            new DeploymentRateLimiter(store ?? new InMemoryRateLimitCounterStore()));

    private static DefaultEndpointFilterInvocationContext Invocation()
        => new(new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() });

    private sealed class FailingCounterStore : IRateLimitCounterStore
    {
        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset windowStart,
            CancellationToken cancellationToken = default)
            => throw new IOException("the counter store is unreachable");
    }
}
