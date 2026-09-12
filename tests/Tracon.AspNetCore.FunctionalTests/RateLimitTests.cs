using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Tests for the rate limit (Phase 21).</summary>
/// <remarks>
/// The rate limit is a mechanism <strong>separate from the quota</strong>: it
/// operates in memory, at second/minute granularity (K-158).
/// </remarks>
public sealed class RateLimitTests
{
    private static readonly Uri Meta = new("/tracon/api/meta", UriKind.Relative);
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    [Fact]
    public async Task Disabled_by_default()
    {
        // 🚨 An upgrading setup must not see an unexpected 429 (K-165).
        await using var host = await TraconTestHost.StartAsync();

        for (var index = 0; index < 50; index++)
        {
            using var response = await host.Client.GetAsync(Agents);

            response.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task Returns_429_when_enabled_and_limit_is_exceeded()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Configure<TraconRateLimitOptions>(
                static options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 3;
                    options.Window = TimeSpan.FromMinutes(5);
                }));

        for (var index = 0; index < 3; index++)
        {
            using var allowed = await host.Client.GetAsync(Agents);

            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        using var blocked = await host.Client.GetAsync(Agents);

        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter.ShouldNotBeNull();

        var problem = await TraconTestHost.ReadJsonAsync(blocked);
        problem.GetProperty("title").GetString().ShouldBe("Rate limit exceeded");
    }

    [Fact]
    public async Task Meta_endpoint_is_unaffected_by_the_rate_limit()
    {
        // The meta group is unfiltered: the UI must always be able to
        // discover the identity method.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Configure<TraconRateLimitOptions>(
                static options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 1;
                    options.Window = TimeSpan.FromMinutes(5);
                }));

        for (var index = 0; index < 10; index++)
        {
            using var response = await host.Client.GetAsync(Meta);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
