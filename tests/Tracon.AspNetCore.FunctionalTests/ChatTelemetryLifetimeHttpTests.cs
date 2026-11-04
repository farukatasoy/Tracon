using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An installation that takes the tenant from a header lets the caller choose how many tenants compile an agent, and every
/// compile builds a model-call pipeline; none of them may leave a telemetry meter or activity source behind.
/// </summary>
/// <remarks>
/// <para>
/// This is the path the shared telemetry client exists for: HTTP, the tenant header, the compiled-agent cache with a limit
/// far under the number of tenants, the registry that builds the pipeline and the validator that builds one more per
/// request. The telemetry client the framework builds into a pipeline makes a meter and an activity source that stay in
/// the runtime's lists until the client is disposed, and a pipeline is never disposed: a compiled agent leaves the cache by
/// being dropped, because a run may still hold it. The cache bound alone therefore held the agents and not their meters.
/// </para>
/// <para>
/// Two counts say the same thing. The census counts the instruments and activity sources the whole process made while the
/// test ran, by their names, and is read with room for the other tests of the process: a leak adds one set for every
/// compile, so three hundred compiles add three hundred. The count of lanes the host holds is exact and belongs to the
/// host of this test alone.
/// </para>
/// </remarks>
public sealed class ChatTelemetryLifetimeHttpTests
{
    private const string AgentName = "shared-lane-agent";
    private const string ProviderName = "shared-lane";
    private const string ModelName = "shared-lane-1";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string KeyName = "Tracon:ProviderKeys:tenant-a:SharedLane";
    private const string TenantKey = "tenant-a-key";
    private const int Limit = 8;
    private const int Tenants = 300;
    private const int Room = Tenants / 3;

    [Fact]
    public async Task Three_hundred_tenants_through_a_small_cache_leave_no_telemetry_instrument_behind()
    {
        await using var host = await StartAsync(new SharedIdentityProvider());
        var cache = host.Services.GetRequiredService<CompiledAgentCache>();
        var lanes = host.Services.GetRequiredService<ChatTelemetryLanes>();

        // The first runs may make what the later ones share.
        for (var warmUp = 0; warmUp < 3; warmUp++)
        {
            await RunAsync(host, $"warm-up-{warmUp}", "hello");
        }

        using var census = InstrumentCensus.Open(TraconDiagnostics.ActivitySourceName);

        for (var tenant = 0; tenant < Tenants; tenant++)
        {
            await RunAsync(host, $"tenant-{tenant:D4}", "hello");
        }

        cache.Count.ShouldBeLessThanOrEqualTo(Limit);
        cache.Count.ShouldBeGreaterThan(Limit / 2, "the limit was reached, so most of the tenants compiled their agent again after it left the cache");

        census.LiveInstruments.ShouldBeLessThan(
            Room,
            $"{Tenants} tenants left {census.LiveInstruments} live telemetry instruments behind");
        census.CreatedSources.ShouldBeLessThan(
            Room,
            $"{Tenants} tenants made {census.CreatedSources} activity sources");
        lanes.LaneCount.ShouldBe(1, "every tenant of the host runs through the one provider, and the lane belongs to the provider");
    }

    [Fact]
    public async Task Requests_that_all_miss_a_cache_far_smaller_than_the_set_of_tenants_leave_no_telemetry_instrument_behind()
    {
        // The shape of the soak that found the leak: more active (tenant, agent) pairs than the cache holds, so the pairs
        // come round in turn, the entry a request needs has left the cache by then, and every request compiles again.
        const int Pairs = Limit * 5;
        const int Rounds = 15;

        var provider = new SharedIdentityProvider();
        await using var host = await StartAsync(provider);
        var cache = host.Services.GetRequiredService<CompiledAgentCache>();
        var lanes = host.Services.GetRequiredService<ChatTelemetryLanes>();

        // The first runs may make what the later ones share.
        await RunAsync(host, "tenant-0000", "hello");

        var compiledBefore = provider.Created;
        using var census = InstrumentCensus.Open(TraconDiagnostics.ActivitySourceName);

        for (var round = 0; round < Rounds; round++)
        {
            for (var pair = 0; pair < Pairs; pair++)
            {
                await RunAsync(host, $"tenant-{pair:D4}", "hello");
            }
        }

        var requests = Pairs * Rounds;

        cache.Count.ShouldBeLessThanOrEqualTo(Limit);
        (provider.Created - compiledBefore).ShouldBeGreaterThan(requests * 9 / 10, "the pairs outnumber the cache, so almost every request compiled its agent again");

        census.LiveInstruments.ShouldBeLessThan(
            requests / 3,
            $"{requests} requests that each compiled again left {census.LiveInstruments} live telemetry instruments behind");
        census.CreatedSources.ShouldBeLessThan(
            requests / 3,
            $"{requests} requests that each compiled again made {census.CreatedSources} activity sources");
        lanes.LaneCount.ShouldBe(1);
    }

    [Fact]
    public async Task Validating_a_definition_three_hundred_times_leaves_no_telemetry_instrument_behind()
    {
        await using var host = await TraconTestHost.StartAsync();
        var lanes = host.Services.GetRequiredService<ChatTelemetryLanes>();

        // Every validation builds the chat client of the definition's model, to prove the provider accepts its settings.
        for (var warmUp = 0; warmUp < 3; warmUp++)
        {
            await ValidateAsync(host);
        }

        using var census = InstrumentCensus.Open(TraconDiagnostics.ActivitySourceName);

        for (var request = 0; request < Tenants; request++)
        {
            await ValidateAsync(host);
        }

        lanes.LaneCount.ShouldBeLessThanOrEqualTo(2);
        census.LiveInstruments.ShouldBeLessThan(Room, $"{Tenants} validations left {census.LiveInstruments} live telemetry instruments behind");
        census.CreatedSources.ShouldBeLessThan(Room, $"{Tenants} validations made {census.CreatedSources} activity sources");
    }

    [Fact]
    public async Task Concurrent_runs_of_a_tenant_with_its_own_key_and_a_tenant_without_one_each_reach_their_own_client()
    {
        var provider = new SharedIdentityProvider();
        await using var host = await StartAsync(provider);
        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "tenant-a",
                ProviderName = ProviderName,
                ApiKeyConfigurationName = KeyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);
        var lanes = host.Services.GetRequiredService<ChatTelemetryLanes>();

        // The tenant with a key compiles on every run, with its own client; the other tenant's agent is cached. Both
        // clients say the same thing about themselves, so both go through one lane.
        var runs = Enumerable.Range(0, 60)
            .Select(index =>
            {
                var tenant = index % 2 == 0 ? "tenant-a" : "tenant-b";
                var message = $"{tenant}:{index}";

                return (Tenant: tenant, Message: message, Run: RunAsync(host, tenant, message));
            })
            .ToList();

        var runIds = await Task.WhenAll(runs.Select(static run => run.Run));

        foreach (var (tenant, message, _) in runs)
        {
            provider.Handled[message].ShouldBe(
                string.Equals(tenant, "tenant-a", StringComparison.Ordinal) ? TenantKey : SharedIdentityProvider.SetupKey,
                $"the run '{message}' reached the client of the other tenant");
        }

        lanes.LaneCount.ShouldBe(1, "two tenants, two clients, one provider: one lane");

        // Each run wrote the span of its own call, once, with the provider the lane was built for.
        for (var index = 0; index < runs.Count; index++)
        {
            var trace = await ReadTraceAsync(host, runs[index].Tenant, runIds[index]);
            var chat = RunTraceReader.Operations(trace, "chat").ShouldHaveSingleItem();

            chat.Attributes["gen_ai.provider.name"].ShouldBe(ProviderName);
        }
    }

    [Fact]
    public async Task A_run_in_flight_when_its_agent_leaves_the_cache_finishes_and_writes_its_model_span()
    {
        var provider = new SharedIdentityProvider();
        await using var host = await StartAsync(provider);
        var cache = host.Services.GetRequiredService<CompiledAgentCache>();

        // The run is inside the model's stream, holding the agent the cache is about to drop.
        var held = RunAsync(host, "tenant-held", "hold me");
        await provider.Holding.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        for (var tenant = 0; tenant < Limit * 4; tenant++)
        {
            await RunAsync(host, $"tenant-{tenant:D4}", "hello");
        }

        cache.Count.ShouldBeLessThanOrEqualTo(Limit);
        held.IsCompleted.ShouldBeFalse("the run was meant to be in flight while the cache turned over");

        provider.Release.SetResult();
        var runId = await held;

        var trace = await ReadTraceAsync(host, "tenant-held", runId);
        var chat = RunTraceReader.Operations(trace, "chat").ShouldHaveSingleItem();

        chat.Status.ShouldNotBe(TraceSpanStatus.Error);
        chat.Attributes["gen_ai.provider.name"].ShouldBe(ProviderName);
        provider.Handled["hold me"].ShouldBe(SharedIdentityProvider.SetupKey);
    }

    [Fact]
    public async Task The_container_disposes_the_lanes_when_the_host_stops()
    {
        var host = await StartAsync(new SharedIdentityProvider());
        var lanes = host.Services.GetRequiredService<ChatTelemetryLanes>();

        await RunAsync(host, "tenant-a", "hello");
        lanes.LaneCount.ShouldBe(1);

        await host.DisposeAsync();

        lanes.LaneCount.ShouldBe(0, "a lane is never disposed while the pool lives, and the pool lives as long as the host");
    }

    private static Task<TraconTestHost> StartAsync(SharedIdentityProvider provider)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: static services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, TenantKey)]).Build());
                services.Configure<TraconOptions>(static options =>
                {
                    options.AgentCache.MaxEntries = Limit;
                    options.Observability.SuccessSampleRatio = 1;
                });
            });

    private static async Task ValidateAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            TestData.Request(),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> RunAsync(TraconTestHost host, string tenant, string message)
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

        return System.Text.Json.JsonDocument.Parse(frames.Single(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal)).Data)
            .RootElement.GetProperty("runId").GetGuid();
    }

    private static async Task<RunTrace> ReadTraceAsync(TraconTestHost host, string tenant, Guid runId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tracon/api/runs/{runId}/trace", UriKind.Relative));
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await response.Content.ReadFromJsonAsync<RunTrace>(TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    /// <summary>
    /// A provider whose clients say the same thing about themselves whoever holds them, so every tenant's pipeline goes
    /// through one lane, and which remembers what each client was asked and for whom it was built.
    /// </summary>
    private sealed class SharedIdentityProvider : ITenantCredentialModelProvider
    {
        public const string SetupKey = "setup-key";

        private int _created;

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = ModelName }];

        /// <summary>Gets the key of the client that answered each message.</summary>
        public ConcurrentDictionary<string, string> Handled { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the signal that a message that starts with 'hold' is inside the stream of its client.</summary>
        public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets the signal that lets a held stream go on.</summary>
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets the number of clients built so far: one for each compile of an agent.</summary>
        public int Created => Volatile.Read(ref _created);

        public IChatClient CreateChatClient(ModelBinding binding)
        {
            Interlocked.Increment(ref _created);

            return new KeyedClient(this, SetupKey);
        }

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential)
        {
            ArgumentNullException.ThrowIfNull(credential);
            Interlocked.Increment(ref _created);

            return new KeyedClient(this, credential.ApiKey);
        }

        private sealed class KeyedClient(SharedIdentityProvider owner, string key) : IChatClient
        {
            private static readonly ChatClientMetadata Metadata = new(ProviderName, providerUri: null, defaultModelId: ModelName);

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                var message = Remember(messages);

                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"{key}:{message}")));
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                var message = Remember(messages);

                yield return new ChatResponseUpdate(ChatRole.Assistant, $"{key}:");

                if (message.StartsWith("hold", StringComparison.Ordinal))
                {
                    owner.Holding.TrySetResult();
                    await owner.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                yield return new ChatResponseUpdate(ChatRole.Assistant, message);
            }

            public object? GetService(Type serviceType, object? serviceKey = null)
            {
                if (serviceKey is not null)
                {
                    return null;
                }

                return serviceType.IsInstanceOfType(Metadata) ? Metadata : serviceType.IsInstanceOfType(this) ? this : null;
            }

            public void Dispose()
            {
            }

            private string Remember(IEnumerable<ChatMessage> messages)
            {
                var message = messages.Last(static candidate => candidate.Role == ChatRole.User).Text;
                owner.Handled[message] = key;

                return message;
            }
        }
    }
}
