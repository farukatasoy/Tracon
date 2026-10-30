using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// Behaviour 2: <c>Tracon:RateLimit:Scope = Deployment</c> counts requests in the database both nodes share, so a limit
/// holds across nodes.
/// </summary>
/// <remarks>
/// A frozen clock keeps every request of a test inside one fixed window: the windows are cut from the Unix epoch, and a
/// test that crossed a boundary would see the counter restart for no reason of its own. The clock moves only the wall
/// clock and is shared by both hosts; timers stay real.
/// </remarks>
public abstract partial class TwoNodeAcceptanceContract
{
    private const string TriggerSecretKey = "Tracon:TriggerSecrets:Slack";
    private const string TriggerSecret = "whsec_test";
    private const int Limit = 5;

    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    // 30 s into a minute: every window these tests use (up to an hour) holds the moment.
    private static readonly DateTimeOffset FrozenNow = new(2030, 6, 1, 12, 0, 30, TimeSpan.Zero);

    [Fact]
    public async Task Two_nodes_admit_the_rate_limit_once()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartRateLimitPairAsync(clock);

        var statuses = new List<HttpStatusCode>();

        for (var request = 0; request < 2 * Limit; request++)
        {
            statuses.Add(await StatusAsync(request % 2 == 0 ? pair.A : pair.B));
        }

        statuses.Count(static status => status == HttpStatusCode.OK).ShouldBe(Limit);
        statuses.Count(static status => status == HttpStatusCode.TooManyRequests).ShouldBe(Limit);

        // The first five are the admitted ones: the counter is one stream, not two.
        statuses.Take(Limit).ShouldAllBe(static status => status == HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_tenants_limit_is_spent_on_both_nodes_and_another_tenant_has_a_window_of_its_own()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartRateLimitPairAsync(clock, limit: 2, tenancy: true);

        (await StatusAsync(pair.A, "tenant-a")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.B, "tenant-a")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.A, "tenant-a")).ShouldBe(HttpStatusCode.TooManyRequests);

        // Tenant A is spent on both nodes; tenant B has a window of its own on both.
        (await StatusAsync(pair.B, "tenant-a")).ShouldBe(HttpStatusCode.TooManyRequests);
        (await StatusAsync(pair.B, "tenant-b")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.A, "tenant-b")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.A, "tenant-b")).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_new_window_admits_requests_again_on_both_nodes()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartRateLimitPairAsync(clock, window: TimeSpan.FromMinutes(1));

        for (var request = 0; request < Limit; request++)
        {
            await StatusAsync(request % 2 == 0 ? pair.A : pair.B);
        }

        (await StatusAsync(pair.B)).ShouldBe(HttpStatusCode.TooManyRequests);

        // 30 s into the window; 31 s later the next window has opened for both nodes.
        clock.Advance(TimeSpan.FromSeconds(31));

        (await StatusAsync(pair.B)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.A)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_inbound_trigger_limit_is_counted_across_both_nodes()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await TwoNodeCluster.StartAsync(
            NewDatabase(),
            configure: static builder => builder.UseScheduling(static options => options.RunWorker = false),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder().AddInMemoryCollection([new(TriggerSecretKey, TriggerSecret)]).Build());
                services.Configure<TraconRateLimitOptions>(static options => options.Scope = RateLimitScope.Deployment);
                services.Configure<TraconInboundTriggerOptions>(static options => options.MaxRequestsPerMinute = 2);
            });

        await SaveTriggerAsync(pair.A);

        (await SendTriggerAsync(pair.A, "first", clock)).ShouldBe(HttpStatusCode.Accepted);
        (await SendTriggerAsync(pair.B, "second", clock)).ShouldBe(HttpStatusCode.Accepted);

        // Instance-scoped, the third request would be admitted by whichever node had seen only one.
        (await SendTriggerAsync(pair.A, "third", clock)).ShouldBe(HttpStatusCode.TooManyRequests);
        (await SendTriggerAsync(pair.B, "fourth", clock)).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private Task<TwoNodeCluster> StartRateLimitPairAsync(
        ManualTimeProvider clock,
        int limit = Limit,
        bool tenancy = false,
        TimeSpan? window = null)
        => TwoNodeCluster.StartAsync(
            NewDatabase(),
            configure: builder =>
            {
                builder.UseScheduling(static options => options.RunWorker = false);

                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }
            },
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<TraconRateLimitOptions>(options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = limit;
                    options.Window = window ?? TimeSpan.FromHours(1);
                    options.Scope = RateLimitScope.Deployment;
                });
            });

    private static async Task<HttpStatusCode> StatusAsync(TraconTestHost host, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Agents);

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);

        return response.StatusCode;
    }

    private static async Task SaveTriggerAsync(TraconTestHost host)
    {
        using var response = await host.Client.PutAsJsonAsync(
            "/tracon/api/triggers/slack",
            new { targetKind = "agent", targetName = "demo", signingSecretConfigurationName = TriggerSecretKey });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Sends a validly signed trigger request. Each label makes a body, so no two signatures repeat.</summary>
    private static async Task<HttpStatusCode> SendTriggerAsync(TraconTestHost host, string label, ManualTimeProvider clock)
    {
        var body = "{\"event\":{\"text\":\"" + label + "\"}}";
        var timestamp = clock.GetUtcNow();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/triggers/default/slack")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSigner.SignatureHeader, WebhookSigner.Sign(body, timestamp, TriggerSecret));

        using var response = await host.Client.SendAsync(request);

        return response.StatusCode;
    }
}
