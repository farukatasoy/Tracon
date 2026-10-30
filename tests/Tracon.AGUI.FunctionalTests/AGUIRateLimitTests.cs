using System.Net;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The AG-UI group keeps a rate-limit counter of its own: a request on it does not spend the management API's window,
/// and the other way round.
/// </summary>
/// <remarks>
/// The two groups were always two separate limiters; the behaviour was not written down. Moving the count into the
/// shared store (<see cref="RateLimitScope.Deployment"/>) must not merge them, so both scopes are pinned.
/// </remarks>
public sealed class AGUIRateLimitTests
{
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    // 30 s into a minute: the one-hour window these tests use holds the moment.
    private static readonly DateTimeOffset FrozenNow = new(2030, 6, 1, 12, 0, 30, TimeSpan.Zero);

    [Theory]
    [InlineData(RateLimitScope.Instance)]
    [InlineData(RateLimitScope.Deployment)]
    public async Task The_ag_ui_group_counts_its_own_window_and_not_the_management_apis(RateLimitScope scope)
    {
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(new FrozenClock(FrozenNow));
                services.Configure<TraconRateLimitOptions>(options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 2;
                    options.Window = TimeSpan.FromHours(1);
                    options.Scope = scope;
                });
            });

        // The management API spends its two permits ...
        (await ManagementStatusAsync(host)).ShouldBe(HttpStatusCode.OK);
        (await ManagementStatusAsync(host)).ShouldBe(HttpStatusCode.OK);
        (await ManagementStatusAsync(host)).ShouldBe(HttpStatusCode.TooManyRequests);

        // ... and the AG-UI group still has both of its own.
        (await AgUiStatusAsync(host, "t-1")).ShouldBe(HttpStatusCode.OK);
        (await AgUiStatusAsync(host, "t-2")).ShouldBe(HttpStatusCode.OK);

        var rejected = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-3", "hi"));

        rejected.Status.ShouldBe(HttpStatusCode.TooManyRequests, rejected.Body);
        rejected.Problem.GetProperty("title").GetString().ShouldBe("Rate limit exceeded");
    }

    [Theory]
    [InlineData(RateLimitScope.Instance)]
    [InlineData(RateLimitScope.Deployment)]
    public async Task A_spent_ag_ui_group_leaves_the_management_api_its_own_window(RateLimitScope scope)
    {
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(new FrozenClock(FrozenNow));
                services.Configure<TraconRateLimitOptions>(options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 1;
                    options.Window = TimeSpan.FromHours(1);
                    options.Scope = scope;
                });
            });

        (await AgUiStatusAsync(host, "t-1")).ShouldBe(HttpStatusCode.OK);
        (await AgUiStatusAsync(host, "t-2")).ShouldBe(HttpStatusCode.TooManyRequests);

        (await ManagementStatusAsync(host)).ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HttpStatusCode> ManagementStatusAsync(AGUITestHost host)
    {
        using var response = await host.Client.GetAsync(Agents, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> AgUiStatusAsync(AGUITestHost host, string thread)
        => (await host.RunAsync(TestAgents.Echo, AGUIInputs.User(thread, "hi"))).Status;

    /// <summary>A wall clock that stands still. Timers stay real.</summary>
    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
