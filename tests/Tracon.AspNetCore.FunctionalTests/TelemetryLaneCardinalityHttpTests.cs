using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tenant administrator who binds a provider endpoint of their own chooses its host and port, and the telemetry lanes are
/// never disposed: the limit on lanes has to hold for the endpoints a real client chain reports, and the runs over the
/// limit have to keep their spans.
/// </summary>
/// <remarks>
/// <para>
/// The provider is the real OpenAI-compatible client in front of a local HTTP server for each endpoint, so the metadata the
/// lanes are keyed by is what the client writes about itself and not what a fake says. A fake that reports no endpoint
/// cannot show a lane that keeps one: the span of an overflowed run must name the provider and nothing the tenant
/// chose, and only the stored trace of a real run can say so.
/// </para>
/// <para>
/// The ports are the identity here. Every server listens on the loopback address, so a host name alone would make them one
/// identity; the port is part of the key and tells them apart.
/// </para>
/// </remarks>
public sealed class TelemetryLaneCardinalityHttpTests
{
    private const string Provider = "lane-limit";
    private const string AgentName = "lane-limit-agent";
    private const string Model = "fake-local-model";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string OptionName = "Tracon:Observability:MaxTelemetryLaneCardinality";

    [Fact]
    public async Task Tenants_whose_endpoints_come_after_the_limit_keep_their_spans_and_lose_the_endpoint_labels()
    {
        await using var setup = await FakeOpenAiCompatibleServer.StartAsync();
        await using var first = await FakeOpenAiCompatibleServer.StartAsync();
        await using var second = await FakeOpenAiCompatibleServer.StartAsync();
        await using var third = await FakeOpenAiCompatibleServer.StartAsync();
        var logs = new RecordingLoggerProvider();
        string[] tenants = ["tenant-one", "tenant-two", "tenant-three"];

        await using var host = await StartAsync(setup, limit: 2, logs, tenants);
        var servers = new[] { first, second, third };

        for (var index = 0; index < tenants.Length; index++)
        {
            await BindAsync(host, tenants[index], servers[index]);
        }

        var traces = new List<RunTrace>();

        for (var index = 0; index < tenants.Length; index++)
        {
            traces.Add(await RunAsync(host, tenants[index], $"hello from {tenants[index]}"));
        }

        for (var index = 0; index < tenants.Length; index++)
        {
            servers[index].ChatCompletionCallCount.ShouldBe(1, $"the run of {tenants[index]} reached its own endpoint whatever lane its telemetry used");
        }

        var chat = traces.Select(static trace => RunTraceReader.Operations(trace, "chat").ShouldHaveSingleItem()).ToList();
        var agent = traces.Select(static trace => RunTraceReader.Operations(trace, "invoke_agent").ShouldHaveSingleItem()).ToList();

        foreach (var span in chat.Concat(agent))
        {
            span.Attributes["gen_ai.provider.name"].ShouldNotBeNullOrWhiteSpace("an overflowed span still names the provider");
        }

        foreach (var index in new[] { 0, 1 })
        {
            var port = servers[index].BaseAddress.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

            chat[index].Attributes["server.address"].ShouldBe("127.0.0.1");
            chat[index].Attributes["server.port"].ShouldBe(port);
            agent[index].Attributes["server.address"].ShouldBe("127.0.0.1");
            agent[index].Attributes["server.port"].ShouldBe(port);
            agent[index].Attributes["gen_ai.request.model"].ShouldBe(Model);
        }

        chat[2].Attributes.ShouldNotContainKey("server.address");
        chat[2].Attributes.ShouldNotContainKey("server.port");
        chat[2].Attributes["gen_ai.request.model"].ShouldBe(Model, "the model of a model call travels with the call and is not part of the key");
        agent[2].Attributes.ShouldNotContainKey("server.address");
        agent[2].Attributes.ShouldNotContainKey("server.port");
        agent[2].Attributes.ShouldNotContainKey("gen_ai.request.model");
        chat[2].Status.ShouldNotBe(TraceSpanStatus.Error);
        agent[2].Status.ShouldNotBe(TraceSpanStatus.Error);
    }

    [Fact]
    public async Task The_limit_reaches_both_pools_and_each_writes_one_warning_that_names_the_setting_and_no_endpoint()
    {
        await using var setup = await FakeOpenAiCompatibleServer.StartAsync();
        var servers = new List<FakeOpenAiCompatibleServer>();
        var logs = new RecordingLoggerProvider();
        string[] tenants = ["tenant-a", "tenant-b", "tenant-c", "tenant-d", "tenant-e"];

        try
        {
            foreach (var unused in tenants)
            {
                servers.Add(await FakeOpenAiCompatibleServer.StartAsync());
            }

            await using var host = await StartAsync(setup, limit: 1, logs, tenants);

            for (var index = 0; index < tenants.Length; index++)
            {
                await BindAsync(host, tenants[index], servers[index]);
                await RunAsync(host, tenants[index], "hello");
            }

            var lanes = host.Services.GetRequiredService<ChatTelemetryLanes>();
            var decorator = host.Services.GetServices<IAgentDecorator>().OfType<OpenTelemetryAgentDecorator>().Single();

            lanes.AdmittedIdentities.ShouldBe(1);
            lanes.OverflowedCalls.ShouldBeGreaterThan(0);
            decorator.AdmittedIdentities.ShouldBe(1);

            var warnings = logs.Entries
                .Where(static entry => entry.StartsWith("Warning ", StringComparison.Ordinal)
                    && entry.Contains(OptionName, StringComparison.Ordinal))
                .ToList();

            warnings.Count.ShouldBe(2, "one for the model calls and one for the agents, however many endpoints came after the limit");

            foreach (var warning in warnings)
            {
                warning.ShouldNotContain("127.0.0.1");

                foreach (var server in servers)
                {
                    warning.ShouldNotContain(server.BaseAddress.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
        finally
        {
            foreach (var server in servers)
            {
                await server.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Runs_of_two_tenants_that_share_the_lane_of_the_overflow_each_reach_their_own_endpoint_with_their_own_key()
    {
        await using var setup = await FakeOpenAiCompatibleServer.StartAsync();
        await using var one = await FakeOpenAiCompatibleServer.StartAsync();
        await using var two = await FakeOpenAiCompatibleServer.StartAsync();
        string[] tenants = ["tenant-one", "tenant-two"];

        // A limit of zero puts every endpoint in the lane of its provider: both tenants share one lane.
        await using var host = await StartAsync(setup, limit: 0, new RecordingLoggerProvider(), tenants);
        await BindAsync(host, tenants[0], one);
        await BindAsync(host, tenants[1], two);

        // Each tenant sends its runs one after another, and the two tenants send at the same time: the lane of the overflow
        // carries calls of both at once, while each fake server (whose counters are not safe against overlapping requests)
        // sees one request at a time.
        var runs = tenants
            .Select(async tenant =>
            {
                var traces = new List<RunTrace>();

                for (var index = 0; index < 12; index++)
                {
                    traces.Add(await RunAsync(host, tenant, $"message {index}"));
                }

                return traces;
            })
            .ToList();

        var traces = (await Task.WhenAll(runs)).SelectMany(static tenantTraces => tenantTraces).ToList();

        one.ChatCompletionCallCount.ShouldBe(12);
        two.ChatCompletionCallCount.ShouldBe(12);
        one.ReceivedAuthorizationHeaders.ShouldAllBe(static header => header != null && header.EndsWith("key-tenant-one", StringComparison.Ordinal));
        two.ReceivedAuthorizationHeaders.ShouldAllBe(static header => header != null && header.EndsWith("key-tenant-two", StringComparison.Ordinal));

        traces.Count.ShouldBe(24);

        foreach (var trace in traces)
        {
            var chat = RunTraceReader.Operations(trace, "chat").ShouldHaveSingleItem();

            chat.Attributes.ShouldNotContainKey("server.address");
            chat.Attributes["gen_ai.provider.name"].ShouldNotBeNullOrWhiteSpace();
        }

        host.Services.GetRequiredService<ChatTelemetryLanes>().AdmittedIdentities.ShouldBe(0);
    }

    private static string KeyName(string tenant) => $"Tracon:ProviderKeys:{tenant}:{Provider}";

    private static Task<TraconTestHost> StartAsync(FakeOpenAiCompatibleServer setup, int limit, RecordingLoggerProvider logs, IReadOnlyList<string> tenants)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .UseOpenAICompatible(Provider, options => options.Endpoint = setup.BaseAddress)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer briefly.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection(tenants.Select(tenant => new KeyValuePair<string, string?>(KeyName(tenant), "key-" + tenant)))
                    .Build());

                // The servers are on the loopback interface; the endpoint is the tenant's own, so the guard judges it.
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconOptions>(options =>
                {
                    options.Observability.SuccessSampleRatio = 1;
                    options.Observability.MaxTelemetryLaneCardinality = limit;
                });
                services.AddLogging(builder => builder.AddProvider(logs));
            });

    private static async Task BindAsync(TraconTestHost host, string tenant, FakeOpenAiCompatibleServer server)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/tracon/api/tenants/{tenant}/providers/{Provider}", UriKind.Relative))
        {
            Content = JsonContent.Create(new { apiKeyConfigurationName = KeyName(tenant), endpoint = server.BaseAddress.ToString() }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<RunTrace> RunAsync(TraconTestHost host, string tenant, string message)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = message }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));

        var runId = JsonDocument.Parse(frames.Single(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal)).Data)
            .RootElement.GetProperty("runId").GetGuid();

        using var trace = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tracon/api/runs/{runId}/trace", UriKind.Relative));
        trace.Headers.Add(TenantHeader, tenant);

        using var traceResponse = await host.Client.SendAsync(trace, TestContext.Current.CancellationToken);

        traceResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await traceResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await traceResponse.Content.ReadFromJsonAsync<RunTrace>(TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }
}
