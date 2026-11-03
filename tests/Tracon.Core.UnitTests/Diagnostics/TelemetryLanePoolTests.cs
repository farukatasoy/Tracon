using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The telemetry agent is shared through lanes: a run borrows one for the length of its call and the meter behind it
/// lives as long as the pool.
/// </summary>
/// <remarks>
/// <para>
/// Every pool here writes to a source name of its own, so a listener bound to that name sees this test's lanes and no
/// other test's. A listener bound to the name Tracon uses would also count every host another test starts in parallel.
/// </para>
/// <para>
/// 🚨 What the tests guard is the pair of facts that make a lane the right shape. A lane that is disposed after a run
/// splits the series the OpenTelemetry SDK exports into one series per run, and every run then builds a meter of its own;
/// a lane that is never taken back or never cleaned keeps the last run's agent alive. The first is read from the number
/// of lanes the pool made (one lane is one meter and one activity source), the second from the count of waiting lanes that
/// still point at an agent. The instruments themselves belong to the framework's chat client and cannot be bound by
/// instance, so the process-wide count of published instruments is asserted by a functional test.
/// </para>
/// </remarks>
public sealed class TelemetryLanePoolTests
{
    [Fact]
    public async Task Runs_one_after_another_share_one_lane_and_one_set_of_instruments()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha"), pool);

        for (var run = 0; run < 200; run++)
        {
            await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        }

        pool.LaneCount.ShouldBe(1, "a lane is one meter and one activity source, and a lane per run is the leak this pool exists to stop");
        pool.IdleCount.ShouldBe(1);
        probe.AgentSpans().Count.ShouldBe(200, "sharing the lane loses no span");
    }

    [Fact]
    public async Task Runs_that_overlap_each_name_their_own_agent_on_the_span_and_the_lanes_stay_at_the_peak()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        const int concurrent = 100;

        var entered = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task HoldAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref entered) == concurrent)
            {
                release.SetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
        }

        var alpha = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha") { BeforeAnswer = HoldAsync }, pool);
        var beta = new PooledOpenTelemetryAgent(new ScriptedAgent("beta") { BeforeAnswer = HoldAsync }, pool);

        await Task.WhenAll(Enumerable.Range(0, concurrent)
            .Select(run => (run % 2 == 0 ? alpha : beta).RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken)));

        var spans = probe.AgentSpans();

        spans.Count.ShouldBe(concurrent);
        spans.Count(static span => string.Equals(Tag(span, "gen_ai.agent.name"), "alpha", StringComparison.Ordinal)).ShouldBe(concurrent / 2);
        spans.Count(static span => string.Equals(Tag(span, "gen_ai.agent.name"), "beta", StringComparison.Ordinal)).ShouldBe(concurrent / 2);
        spans.ShouldAllBe(static span => string.Equals(Tag(span, "gen_ai.agent.id"), Tag(span, "gen_ai.agent.name") + "-id", StringComparison.Ordinal));
        spans.ShouldAllBe(static span => string.Equals(Tag(span, "gen_ai.agent.description"), "about " + Tag(span, "gen_ai.agent.name"), StringComparison.Ordinal));

        pool.LaneCount.ShouldBe(concurrent, "every run in flight at the busiest moment holds a lane of its own");
        pool.IdleCount.ShouldBe(concurrent);

        for (var run = 0; run < 100; run++)
        {
            await alpha.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        }

        pool.LaneCount.ShouldBe(concurrent, "later runs borrow the lanes that exist and make none");
    }

    [Fact]
    public async Task A_stream_the_consumer_stops_early_hands_its_lane_back()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha"), pool);

        await foreach (var update in agent.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
        {
            update.ShouldNotBeNull();
            break;
        }

        pool.IdleCount.ShouldBe(1);
        pool.LaneCount.ShouldBe(1);

        await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        pool.LaneCount.ShouldBe(1, "the next run borrows the lane the early stop returned");
    }

    [Fact]
    public async Task A_stream_that_is_read_to_the_end_hands_its_lane_back_once()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha"), pool);
        var updates = 0;

        await foreach (var unused in agent.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
        {
            updates++;
        }

        updates.ShouldBe(ScriptedAgent.StreamLength);
        pool.IdleCount.ShouldBe(1);
        pool.LaneCount.ShouldBe(1);
        probe.AgentSpans().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_that_throws_hands_its_lane_back()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha") { Failure = new InvalidOperationException("model down") }, pool);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken));

        pool.IdleCount.ShouldBe(1);
        pool.LaneCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_stream_that_throws_midway_hands_its_lane_back()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha") { Failure = new InvalidOperationException("model down") }, pool);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var unused in agent.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
            {
                // The failure comes from the stream itself, after its first update.
            }
        });

        pool.IdleCount.ShouldBe(1);
        pool.LaneCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_that_is_canceled_hands_its_lane_back()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var agent = new PooledOpenTelemetryAgent(
            new ScriptedAgent("alpha")
            {
                BeforeAnswer = async cancellationToken =>
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated
                },
            },
            pool);

        var run = agent.RunAsync("hello", cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await run);

        pool.IdleCount.ShouldBe(1);
        pool.LaneCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_lane_that_comes_back_no_longer_points_at_the_agent_of_its_last_run()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha"), pool);
        var failing = new PooledOpenTelemetryAgent(new ScriptedAgent("beta") { Failure = new InvalidOperationException("model down") }, pool);

        await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await failing.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken));

        await foreach (var unused in agent.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
        {
            break;
        }

        pool.IdleCount.ShouldBe(1);
        pool.IdleAimedCount.ShouldBe(0, "a lane that stays aimed at its last agent keeps that agent, its sessions and its tools alive");
    }

    [Fact]
    public async Task Agents_of_different_providers_use_lanes_of_their_own_and_the_span_names_the_provider()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var one = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha") { Provider = "provider-one" }, pool);
        var two = new PooledOpenTelemetryAgent(new ScriptedAgent("beta") { Provider = "provider-two" }, pool);
        var none = new PooledOpenTelemetryAgent(new ScriptedAgent("gamma") { Provider = null }, pool);

        await one.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        await two.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        await none.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        await one.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        pool.LaneCount.ShouldBe(3, "the provider name is read once, when a lane is built, so a provider needs a lane of its own");

        var spans = probe.AgentSpans();

        Tag(spans.Single(static span => string.Equals(Tag(span, "gen_ai.agent.name"), "beta", StringComparison.Ordinal)), "gen_ai.provider.name").ShouldBe("provider-two");
        Tag(spans.Single(static span => string.Equals(Tag(span, "gen_ai.agent.name"), "gamma", StringComparison.Ordinal)), "gen_ai.provider.name").ShouldBeNull();
        spans.Where(static span => string.Equals(Tag(span, "gen_ai.agent.name"), "alpha", StringComparison.Ordinal))
            .ShouldAllBe(static span => string.Equals(Tag(span, "gen_ai.provider.name"), "provider-one", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_sensitive_data_setting_reaches_the_lane(bool enableSensitiveData)
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool(enableSensitiveData);
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha"), pool);

        await agent.RunAsync("a secret question", cancellationToken: TestContext.Current.CancellationToken);

        var recorded = probe.AgentSpans().Single().TagObjects.Any(static tag => tag.Key.Contains("messages", StringComparison.Ordinal));

        recorded.ShouldBe(enableSensitiveData);
    }

    [Fact]
    public async Task The_session_and_the_service_lookups_reach_the_agent_underneath()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var inner = new ScriptedAgent("alpha") { Provider = "provider-one" };
        var agent = new PooledOpenTelemetryAgent(inner, pool);

        agent.Name.ShouldBe("alpha");
        agent.Id.ShouldBe("alpha-id");
        agent.GetService<AIAgentMetadata>()!.ProviderName.ShouldBe("provider-one");

        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        session.ShouldBeSameAs(inner.Session);
        pool.LaneCount.ShouldBe(0, "asking for a session or a service is not a run and needs no lane");
    }

    [Fact]
    public async Task A_pool_that_is_disposed_disposes_its_lanes_and_lets_later_runs_through_without_telemetry()
    {
        using var probe = new TelemetryProbe();
        var pool = probe.NewPool();
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha"), pool);

        await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        pool.LaneCount.ShouldBe(1);

        pool.Dispose();
        pool.Dispose();

        pool.LaneCount.ShouldBe(0, "disposing the pool disposes the lanes that wait");

        var response = await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        var streamed = 0;

        await foreach (var unused in agent.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
        {
            streamed++;
        }

        response.Text.ShouldBe(ScriptedAgent.Answer);
        streamed.ShouldBe(ScriptedAgent.StreamLength);
        probe.AgentSpans().Count.ShouldBe(1, "the runs after the pool closed wrote no telemetry");
    }

    [Fact]
    public async Task A_lane_that_comes_back_after_the_pool_was_disposed_is_disposed()
    {
        using var probe = new TelemetryProbe();
        var pool = probe.NewPool();
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha"), pool);

        await foreach (var unused in agent.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
        {
            pool.Dispose();
            pool.LaneCount.ShouldBe(1, "the lane in use is not taken from the run");
            break;
        }

        pool.LaneCount.ShouldBe(0, "the lane that came back after the pool closed is disposed");
    }

    [Theory]
    [InlineData(RunShape.Response)]
    [InlineData(RunShape.Stream)]
    [InlineData(RunShape.Failure)]
    public async Task A_lane_writes_the_tags_a_telemetry_agent_built_over_the_agent_itself_writes(RunShape shape)
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var chatAgent = CreateChatAgent("alpha", "provider-one", "model-1", "https://models.example:8443/v1", fail: shape == RunShape.Failure);

        // The framework's telemetry agent reads the provider, the default model and the endpoint of the chat client
        // once, when it is built. A lane is built before any run is aimed at it, so it has to be built for those values.
#pragma warning disable MAAI001
        using var direct = new OpenTelemetryAgent(new AggregatedResponseGuard(chatAgent), probe.Source, autoWireChatClient: false);
#pragma warning restore MAAI001
        var pooled = new PooledOpenTelemetryAgent(chatAgent, pool);

        await RunAsync(direct, shape);
        await RunAsync(pooled, shape);

        var spans = probe.AgentSpans();

        spans.Count.ShouldBe(2);

        var expected = Describe(spans[0]);
        var actual = Describe(spans[1]);

        actual.Keys.OrderBy(static key => key, StringComparer.Ordinal).ShouldBe(expected.Keys.OrderBy(static key => key, StringComparer.Ordinal));

        foreach (var key in new[]
        {
            "gen_ai.request.model", "gen_ai.provider.name", "server.address", "server.port", "gen_ai.agent.name", "gen_ai.agent.id",
            "gen_ai.agent.description", "gen_ai.operation.name", "error.type",
        })
        {
            actual.GetValueOrDefault(key).ShouldBe(expected.GetValueOrDefault(key), key);
        }

        actual["gen_ai.request.model"].ShouldBe("model-1");
        actual["gen_ai.provider.name"].ShouldBe("provider-one");
        actual["server.address"].ShouldBe("models.example");
        actual["server.port"].ShouldBe("8443");
        spans[1].Status.ShouldBe(spans[0].Status);
    }

    [Fact]
    public async Task Agents_that_differ_in_provider_model_or_endpoint_use_lanes_of_their_own_and_keep_their_own_tags()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var one = new PooledOpenTelemetryAgent(CreateChatAgent("alpha", "provider-one", "model-1", "https://one.example:8443"), pool);
        var otherModel = new PooledOpenTelemetryAgent(CreateChatAgent("beta", "provider-one", "model-2", "https://one.example:8443"), pool);
        var otherEndpoint = new PooledOpenTelemetryAgent(CreateChatAgent("gamma", "provider-one", "model-1", "https://two.example"), pool);
        var sameAsOne = new PooledOpenTelemetryAgent(CreateChatAgent("delta", "provider-one", "model-1", "https://one.example:8443/other/path"), pool);

        foreach (var agent in new[] { one, otherModel, otherEndpoint, sameAsOne, one })
        {
            await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        }

        pool.LaneCount.ShouldBe(3, "the path of the endpoint is not read, so alpha and delta share a lane");

        var spans = probe.AgentSpans().GroupBy(static span => Tag(span, "gen_ai.agent.name")!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        Tag(spans["beta"], "gen_ai.request.model").ShouldBe("model-2");
        Tag(spans["gamma"], "server.address").ShouldBe("two.example");
        Tag(spans["delta"], "gen_ai.request.model").ShouldBe("model-1");
        Tag(spans["delta"], "server.address").ShouldBe("one.example");
    }

    [Fact]
    public async Task One_lane_that_serves_one_agent_after_another_names_each_of_them_on_its_own_span()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var alpha = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha"), pool);
        var beta = new PooledOpenTelemetryAgent(new ScriptedAgent("beta"), pool);

        await alpha.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        await beta.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        await foreach (var unused in alpha.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
        {
            // Read to the end.
        }

        await foreach (var unused in beta.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
        {
            // Read to the end.
        }

        pool.LaneCount.ShouldBe(1, "all four calls ran one after another on the one lane");

        var spans = probe.AgentSpans();

        spans.Select(static span => Tag(span, "gen_ai.agent.name")).ShouldBe(["alpha", "beta", "alpha", "beta"]);
        spans.Select(static span => Tag(span, "gen_ai.agent.id")).ShouldBe(["alpha-id", "beta-id", "alpha-id", "beta-id"]);
        spans.Select(static span => Tag(span, "gen_ai.agent.description")).ShouldBe(["about alpha", "about beta", "about alpha", "about beta"]);
    }

    [Fact]
    public async Task A_stream_that_is_canceled_hands_its_lane_back_and_a_stream_never_read_takes_none()
    {
        using var probe = new TelemetryProbe();
        using var pool = probe.NewPool();
        var agent = new PooledOpenTelemetryAgent(new ScriptedAgent("alpha"), pool);
        using var cancellation = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in agent.RunStreamingAsync("hello", cancellationToken: cancellation.Token))
            {
                await cancellation.CancelAsync();
            }
        });

        pool.LaneCount.ShouldBe(1);
        pool.IdleCount.ShouldBe(1);

        await using (var enumerator = agent.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            // Asking for the enumerator is not asking for an update: the lane is taken when the first update is asked for.
            pool.IdleCount.ShouldBe(1);
        }

        pool.LaneCount.ShouldBe(1);
        pool.IdleCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_decorator_hands_back_an_agent_that_borrows_lanes_and_leaves_a_disabled_one_alone()
    {
        var agent = new ScriptedAgent("alpha");
        var descriptor = new AgentDescriptor { Name = "alpha", Origin = AgentDefinitionOrigin.Code, SourceName = "test" };

        using var disabled = new OpenTelemetryAgentDecorator(Options.Create(new TraconOptions { Observability = { Enabled = false } }));
        disabled.Decorate(agent, descriptor).ShouldBeSameAs(agent);

        using var enabled = new OpenTelemetryAgentDecorator(Options.Create(new TraconOptions()));
        var decorated = enabled.Decorate(agent, descriptor);

        decorated.ShouldBeOfType<PooledOpenTelemetryAgent>();
        decorated.Name.ShouldBe("alpha");

        // The decorator belongs to the host and the host disposes it: a run after that still answers.
        enabled.Dispose();
        enabled.Dispose();
        (await decorated.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken)).Text.ShouldBe(ScriptedAgent.Answer);
    }

    [Fact]
    public async Task The_container_disposes_the_decorator_and_with_it_the_lanes_when_the_host_stops()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new TraconOptions()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, OpenTelemetryAgentDecorator>());

        var provider = services.BuildServiceProvider();
        var decorator = provider.GetServices<IAgentDecorator>().OfType<OpenTelemetryAgentDecorator>().Single();
        var descriptor = new AgentDescriptor { Name = "alpha", Origin = AgentDefinitionOrigin.Code, SourceName = "test" };
        var decorated = decorator.Decorate(new ScriptedAgent("alpha"), descriptor);

        decorator.LaneCount.ShouldBe(0, "no lane exists before the first call");

        await decorated.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        await decorated.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        decorator.LaneCount.ShouldBe(1);

        await provider.DisposeAsync();

        decorator.LaneCount.ShouldBe(0);
    }

    public enum RunShape
    {
        Response,
        Stream,
        Failure,
    }

    private static ChatClientAgent CreateChatAgent(string name, string provider, string model, string endpoint, bool fail = false)
        => new(
            new MetadataChatClient(new ChatClientMetadata(provider, new Uri(endpoint), model), fail),
            new ChatClientAgentOptions { Id = name + "-id", Name = name, Description = "about " + name });

    private static async Task RunAsync(AIAgent agent, RunShape shape)
    {
        switch (shape)
        {
            case RunShape.Response:
                await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
                break;
            case RunShape.Stream:
                await foreach (var unused in agent.RunStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
                {
                    // Read to the end.
                }

                break;
            default:
                await Should.ThrowAsync<InvalidOperationException>(
                    async () => await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken));
                break;
        }
    }

    private static Dictionary<string, string?> Describe(Activity span)
        => span.TagObjects.ToDictionary(static tag => tag.Key, static tag => tag.Value?.ToString(), StringComparer.Ordinal);

    private static string? Tag(Activity span, string key) => span.GetTagItem(key)?.ToString();

    /// <summary>Listens to the spans of one source name, and makes pools that write to it.</summary>
    private sealed class TelemetryProbe : IDisposable
    {
        private readonly ActivityListener _activities;
        private readonly List<Activity> _spans = [];
        private readonly Lock _gate = new();

        public TelemetryProbe()
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(source.Name, Source, StringComparison.Ordinal),
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (_gate)
                    {
                        _spans.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_activities);
        }

        public string Source { get; } = "lane-pool-test-" + Guid.NewGuid().ToString("N");

        public TelemetryLanePool NewPool(bool enableSensitiveData = false) => new(Source, enableSensitiveData);

        public List<Activity> AgentSpans()
        {
            lock (_gate)
            {
                return [.. _spans.Where(static span => string.Equals(Tag(span, "gen_ai.operation.name"), "invoke_agent", StringComparison.Ordinal))];
            }
        }

        public void Dispose() => _activities.Dispose();
    }

    /// <summary>An agent that answers with a fixed text and fixed token counts, and can be made to wait or to fail.</summary>
    private sealed class ScriptedAgent(string name) : AIAgent
    {
        public const string Answer = "scripted answer";
        public const int InputTokens = 10;
        public const int OutputTokens = 5;
        public const int StreamLength = 4;

        public AgentSession? Session { get; private set; }

        public string? Provider { get; init; } = "scripted-provider";

        public Func<CancellationToken, Task>? BeforeAnswer { get; init; }

        public Exception? Failure { get; init; }

        public override string? Name => name;

        public override string? Description => "about " + name;

        protected override string? IdCore => name + "-id";

        public override object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(AIAgentMetadata) && Provider is not null
                ? new AIAgentMetadata(Provider)
                : base.GetService(serviceType, serviceKey);

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        {
            Session = new ScriptedSession();

            return new ValueTask<AgentSession>(Session);
        }

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override async Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (BeforeAnswer is not null)
            {
                await BeforeAnswer(cancellationToken).ConfigureAwait(false);
            }

            await Task.Yield();

            if (Failure is not null)
            {
                throw Failure;
            }

            return new AgentResponse(new ChatMessage(ChatRole.Assistant, Answer))
            {
                Usage = new UsageDetails { InputTokenCount = InputTokens, OutputTokenCount = OutputTokens, TotalTokenCount = InputTokens + OutputTokens },
            };
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < StreamLength; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Yield();

                if (index == 1 && Failure is not null)
                {
                    throw Failure;
                }

                yield return new AgentResponseUpdate(ChatRole.Assistant, "part ");
            }
        }
    }

    /// <summary>A chat client that answers with fixed text, names a provider, a default model and an endpoint, and can be made to fail.</summary>
    private sealed class MetadataChatClient(ChatClientMetadata metadata, bool fail) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => fail
                ? Task.FromException<ChatResponse>(new InvalidOperationException("model down"))
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
                {
                    Usage = new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2, TotalTokenCount = 5 },
                });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(metadata) ? metadata : null;

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedSession : AgentSession;
}
