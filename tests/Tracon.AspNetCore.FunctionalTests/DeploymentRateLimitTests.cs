using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>Tracon:RateLimit:Scope = Deployment</c> counts requests in the shared counter store, so a
/// limit holds across instances.
/// </summary>
/// <remarks>
/// <para>
/// Two hosts share one SQLite file (<see cref="TwoHostFixture"/>), so the counter store is the real
/// <c>SqlRateLimitCounterStore</c> and the two hosts see one row per partition. A frozen clock keeps every request of a
/// test inside one fixed window: the windows are cut from the Unix epoch, and a test that crossed a boundary would
/// see a counter restart for no reason of its own.
/// </para>
/// <para>
/// The clock is shared by both hosts and moves only the wall clock; timers stay real.
/// </para>
/// </remarks>
public sealed class DeploymentRateLimitTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string TriggerSecretKey = "Tracon:TriggerSecrets:Slack";
    private const string TriggerSecret = "whsec_test";
    private const int Limit = 5;

    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    // 30 s into a minute: every window these tests use (up to an hour) holds the moment.
    private static readonly DateTimeOffset FrozenNow = new(2030, 6, 1, 12, 0, 30, TimeSpan.Zero);

    [Fact]
    public async Task Two_instances_admit_the_limit_once()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartPairAsync(clock, RateLimitScope.Deployment);

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
    public async Task The_rejected_request_tells_the_time_to_the_end_of_the_shared_window()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartPairAsync(clock, RateLimitScope.Deployment, window: TimeSpan.FromMinutes(5));

        for (var request = 0; request < Limit; request++)
        {
            (await StatusAsync(pair.A)).ShouldBe(HttpStatusCode.OK);
        }

        // The other instance answers the sixth request: 30 s into a window of five minutes, 270 s are left.
        using var rejected = await pair.B.Client.GetAsync(Agents);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(270));

        var problem = await TraconTestHost.ReadJsonAsync(rejected);
        problem.GetProperty("title").GetString().ShouldBe("Rate limit exceeded");
    }

    [Fact]
    public async Task A_new_window_admits_requests_again_on_both_instances()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartPairAsync(clock, RateLimitScope.Deployment, window: TimeSpan.FromMinutes(1));

        for (var request = 0; request < Limit; request++)
        {
            await StatusAsync(request % 2 == 0 ? pair.A : pair.B);
        }

        (await StatusAsync(pair.B)).ShouldBe(HttpStatusCode.TooManyRequests);

        // 30 s into the window; 31 s later the next window has opened for both instances.
        clock.Advance(TimeSpan.FromSeconds(31));

        (await StatusAsync(pair.B)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.A)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_instance_scope_still_admits_the_limit_per_instance()
    {
        // 🚨 The default does not change: two instances admit twice the limit.
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartPairAsync(clock, RateLimitScope.Instance);

        for (var request = 0; request < 2 * Limit; request++)
        {
            (await StatusAsync(request % 2 == 0 ? pair.A : pair.B)).ShouldBe(HttpStatusCode.OK);
        }

        (await StatusAsync(pair.A)).ShouldBe(HttpStatusCode.TooManyRequests);
        (await StatusAsync(pair.B)).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_instance_scope_does_not_touch_the_database()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        var counting = new CountingStore();
        await using var host = await StartSingleAsync(clock, RateLimitScope.Instance, counting);

        for (var request = 0; request < 3; request++)
        {
            (await StatusAsync(host)).ShouldBe(HttpStatusCode.OK);
        }

        counting.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_disabled_limit_never_calls_the_store_whatever_the_scope()
    {
        var counting = new CountingStore();
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.Services.AddSingleton<IRateLimitCounterStore>(counting),
            configureServices: static services => services.Configure<TraconRateLimitOptions>(
                static options => options.Scope = RateLimitScope.Deployment));

        for (var request = 0; request < 3; request++)
        {
            (await StatusAsync(host)).ShouldBe(HttpStatusCode.OK);
        }

        counting.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_tenant_partition_is_per_tenant_across_instances()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartPairAsync(clock, RateLimitScope.Deployment, limit: 2, tenancy: true);

        (await StatusAsync(pair.A, "tenant-a")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.B, "tenant-a")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.A, "tenant-a")).ShouldBe(HttpStatusCode.TooManyRequests);

        // Tenant A is spent on both instances; tenant B has a window of its own on both.
        (await StatusAsync(pair.B, "tenant-a")).ShouldBe(HttpStatusCode.TooManyRequests);
        (await StatusAsync(pair.B, "tenant-b")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.A, "tenant-b")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.A, "tenant-b")).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_global_partition_is_one_window_for_every_tenant_across_instances()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartPairAsync(
            clock, RateLimitScope.Deployment, limit: 2, tenancy: true, partition: RateLimitPartitionKind.Global);

        (await StatusAsync(pair.A, "tenant-a")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.B, "tenant-b")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(pair.A, "tenant-c")).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_store_failure_applies_the_local_limit_counts_the_failure_and_warns_once()
    {
        // 🚨 Neither fail-open (the limit would vanish with the database) nor fail-closed (every caller would
        // get 429). The instance applies its own limit, built from the same settings.
        using var meters = new Infrastructure.TestMeterFactory();
        using var collector = new MeterInstanceCollector(meters.Meter);

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.Services.AddSingleton<IRateLimitCounterStore>(new FailingStore()),
            configureServices: services =>
            {
                services.AddSingleton<IMeterFactory>(meters);
                services.Configure<TraconRateLimitOptions>(options => Configure(options, RateLimitScope.Deployment, limit: 3));
            });

        var statuses = new List<HttpStatusCode>();

        for (var request = 0; request < 5; request++)
        {
            statuses.Add(await StatusAsync(host));
        }

        statuses.ShouldBe(
        [
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
        ]);

        collector.Snapshot(TraconDiagnostics.RateLimitStoreFailureCounterName).Count.ShouldBe(5);
        collector.Snapshot(TraconDiagnostics.RateLimitStoreFailureCounterName)
            .ShouldAllBe(static measurement => (string?)measurement.Tags[TraconDiagnostics.Tags.Limiter] == "endpoint");

        host.Logs.Entries
            .Count(static entry => entry.Contains("rate-limit counter store failed", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_store_that_recovers_takes_the_counting_back()
    {
        var store = new FailingStore();
        var clock = new ManualTimeProvider(FrozenNow);
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.Services.AddSingleton<IRateLimitCounterStore>(store),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<TraconRateLimitOptions>(options => Configure(options, RateLimitScope.Deployment, limit: 2));
            });

        (await StatusAsync(host)).ShouldBe(HttpStatusCode.OK);

        store.Recover();

        // The store counts now: its own count starts at one, so two more requests pass before it says no.
        (await StatusAsync(host)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(host)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(host)).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_scope_is_read_from_configuration()
    {
        // 🚨 BindRateLimit is written by hand: a key it does not read is a key that does nothing. The proof is the
        // store being called, which only the Deployment scope does.
        var counting = new CountingStore();
        await using var host = await StartFromConfigurationAsync(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:RateLimit:Enabled"] = "true",
                ["Tracon:RateLimit:PermitLimit"] = "2",
                ["Tracon:RateLimit:Window"] = "01:00:00",
                ["Tracon:RateLimit:Scope"] = "Deployment",
            },
            counting);

        (await StatusAsync(host)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(host)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(host)).ShouldBe(HttpStatusCode.TooManyRequests);

        counting.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task Without_the_scope_key_the_configuration_keeps_the_instance_scope()
    {
        var counting = new CountingStore();
        await using var host = await StartFromConfigurationAsync(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:RateLimit:Enabled"] = "true",
                ["Tracon:RateLimit:PermitLimit"] = "2",
            },
            counting);

        (await StatusAsync(host)).ShouldBe(HttpStatusCode.OK);

        counting.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_queue_with_the_deployment_scope_stops_the_host_at_start()
    {
        var failure = await Should.ThrowAsync<OptionsValidationException>(async () =>
            await StartFromConfigurationAsync(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Tracon:RateLimit:Enabled"] = "true",
                    ["Tracon:RateLimit:QueueLimit"] = "5",
                    ["Tracon:RateLimit:Scope"] = "Deployment",
                },
                new CountingStore()));

        failure.Message.ShouldContain("QueueLimit");
    }

    [Fact]
    public async Task A_deployment_scope_with_no_shared_store_warns_once_at_start()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Configure<TraconRateLimitOptions>(
                static options => Configure(options, RateLimitScope.Deployment, limit: 3)));

        host.Logs.Entries
            .Count(static entry => entry.Contains("no shared rate-limit counter store is registered", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_deployment_scope_over_a_sql_provider_gives_no_warning()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await StartPairAsync(clock, RateLimitScope.Deployment);

        pair.A.Logs.Entries
            .Any(static entry => entry.Contains("no shared rate-limit counter store is registered", StringComparison.Ordinal))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task The_trigger_limit_is_shared_across_instances()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await TwoHostFixture.StartAsync(
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

        // A request that does not carry a valid signature never reaches the counter (the signature is checked
        // first), so it cannot spend the sender's budget on either instance.
        using (var forged = await SendWithSignatureAsync(pair.A, "forged-1", clock.GetUtcNow(), "sha256=deadbeef"))
        {
            forged.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await SendTriggerAsync(pair.A, "first", clock)).ShouldBe(HttpStatusCode.Accepted);
        (await SendTriggerAsync(pair.B, "second", clock)).ShouldBe(HttpStatusCode.Accepted);

        // Instance-scoped, the third request would be admitted by whichever instance had seen only one.
        (await SendTriggerAsync(pair.A, "third", clock)).ShouldBe(HttpStatusCode.TooManyRequests);
        (await SendTriggerAsync(pair.B, "fourth", clock)).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_trigger_limit_stays_per_instance_under_the_instance_scope()
    {
        var clock = new ManualTimeProvider(FrozenNow);
        await using var pair = await TwoHostFixture.StartAsync(
            configure: static builder => builder.UseScheduling(static options => options.RunWorker = false),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder().AddInMemoryCollection([new(TriggerSecretKey, TriggerSecret)]).Build());
                services.Configure<TraconInboundTriggerOptions>(static options => options.MaxRequestsPerMinute = 1);
            });

        await SaveTriggerAsync(pair.A);

        (await SendTriggerAsync(pair.A, "first", clock)).ShouldBe(HttpStatusCode.Accepted);
        (await SendTriggerAsync(pair.B, "second", clock)).ShouldBe(HttpStatusCode.Accepted);
        (await SendTriggerAsync(pair.A, "third", clock)).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private static void Configure(
        TraconRateLimitOptions options,
        RateLimitScope scope,
        int limit = Limit,
        RateLimitPartitionKind partition = RateLimitPartitionKind.Tenant,
        TimeSpan? window = null)
    {
        options.Enabled = true;
        options.PermitLimit = limit;
        options.Window = window ?? TimeSpan.FromHours(1);
        options.Scope = scope;
        options.Partition = partition;
    }

    private static Task<TwoHostFixture> StartPairAsync(
        ManualTimeProvider clock,
        RateLimitScope scope,
        int limit = Limit,
        bool tenancy = false,
        RateLimitPartitionKind partition = RateLimitPartitionKind.Tenant,
        TimeSpan? window = null)
        => TwoHostFixture.StartAsync(
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
                services.Configure<TraconRateLimitOptions>(options => Configure(options, scope, limit, partition, window));
            });

    private static Task<TraconTestHost> StartSingleAsync(ManualTimeProvider clock, RateLimitScope scope, IRateLimitCounterStore store)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder.Services.AddSingleton(store),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<TraconRateLimitOptions>(options => Configure(options, scope));
            });

    /// <summary>Starts a host whose rate limit comes from configuration, the way an application's appsettings does.</summary>
    private static async Task<StartedHost> StartFromConfigurationAsync(
        Dictionary<string, string?> configuration,
        IRateLimitCounterStore store)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddTracon();
        builder.Services.AddSingleton(store);

        var app = builder.Build();
        app.MapTracon();

        await app.StartAsync();

        return new StartedHost(app);
    }

    private static async Task<HttpStatusCode> StatusAsync(TraconTestHost host, string? tenant = null)
        => await StatusAsync(host.Client, tenant);

    private static async Task<HttpStatusCode> StatusAsync(StartedHost host)
        => await StatusAsync(host.Client, tenant: null);

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Agents);

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await client.SendAsync(request);

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
        var body = BodyOf(label);
        var timestamp = clock.GetUtcNow();

        using var response = await SendSignedAsync(host, body, timestamp, WebhookSigner.Sign(body, timestamp, TriggerSecret));

        return response.StatusCode;
    }

    private static Task<HttpResponseMessage> SendWithSignatureAsync(TraconTestHost host, string label, DateTimeOffset timestamp, string signature)
        => SendSignedAsync(host, BodyOf(label), timestamp, signature);

    private static string BodyOf(string label) => "{\"event\":{\"text\":\"" + label + "\"}}";

    private static async Task<HttpResponseMessage> SendSignedAsync(TraconTestHost host, string body, DateTimeOffset timestamp, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/triggers/default/slack")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSigner.SignatureHeader, signature);

        return await host.Client.SendAsync(request);
    }

    private sealed class StartedHost(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary>A store that counts the calls and keeps the real windows in memory.</summary>
    private sealed class CountingStore : IRateLimitCounterStore
    {
        private readonly InMemoryRateLimitCounterStore _inner = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset windowStart,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return _inner.IncrementAsync(partitionKey, windowStart, cancellationToken);
        }
    }

    /// <summary>A store that throws until <see cref="Recover"/> is called.</summary>
    private sealed class FailingStore : IRateLimitCounterStore
    {
        private readonly InMemoryRateLimitCounterStore _inner = new();
        private volatile bool _failing = true;

        public void Recover() => _failing = false;

        public ValueTask<RateLimitWindowCount> IncrementAsync(
            string partitionKey,
            DateTimeOffset windowStart,
            CancellationToken cancellationToken = default)
            => _failing
                ? throw new InvalidOperationException("The database is down.")
                : _inner.IncrementAsync(partitionKey, windowStart, cancellationToken);
    }
}
