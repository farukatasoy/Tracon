using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The telemetry lanes are never disposed, and the host and the port of an endpoint (and the model of an agent) are text
/// a tenant administrator writes: each new value would make a meter and an activity source that stay for the life of the
/// process. Both pools therefore key a fixed number of identities, and every further one goes to the lane of its provider.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 What the tests guard is the shape of the overflow. The call still writes its span and its metrics (a lane that
/// refused it would cost the run its model span), the span still names the provider and loses only the labels the
/// tenant chose, an identity that earned its lane keeps it, and the count never goes above the limit however the
/// identities arrive. A limit that is read by comparison alone passes a loop that adds one identity at a time and fails
/// when sixty-four arrive together, so the parallel cases are the ones that matter.
/// </para>
/// <para>
/// The names of the span attributes are the framework's, read from the span a real client chain writes. A fake that
/// reports no endpoint cannot show a lane that leaks one.
/// </para>
/// </remarks>
public sealed class TelemetryLaneCardinalityTests
{
    private const string OptionName = "Tracon:Observability:MaxTelemetryLaneCardinality";
    private static readonly IReadOnlyList<ChatMessage> Messages = [new ChatMessage(ChatRole.User, "hello")];

    [Fact]
    public async Task Endpoints_past_the_limit_share_the_lane_of_their_provider_and_lose_only_the_endpoint_label()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes(maxIdentities: 3);

        for (var endpoint = 0; endpoint < 10; endpoint++)
        {
            var client = lanes.Wrap(new ScriptedPipeline($"tenant-{endpoint}", Metadata("provider", $"https://host-{endpoint}.example.test:8443/v1/", "model")));

            await client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);
        }

        lanes.AdmittedIdentities.ShouldBe(3);
        lanes.OverflowedCalls.ShouldBe(7);
        lanes.LaneCount.ShouldBe(4, "three lanes of their own and the one lane of the provider");
        probe.SourcesCreated.ShouldBe(4, "a lane is one meter and one activity source, so the limit bounds what stays in the runtime");

        var spans = probe.Spans();

        spans.Count.ShouldBe(10, "an endpoint without a lane of its own still writes the span of its call");
        spans.ShouldAllBe(static span => Has(span, "gen_ai.provider.name", "provider"));
        spans.ShouldAllBe(static span => Has(span, "gen_ai.request.model", "model"), "the model travels with the call and is not part of the key");
        spans.Take(3).ShouldAllBe(static span => Tag(span, "server.address") != null && Has(span, "server.port", "8443"));
        spans.Skip(3).ShouldAllBe(static span => Tag(span, "server.address") == null && Tag(span, "server.port") == null);
    }

    [Fact]
    public async Task An_endpoint_that_has_a_lane_keeps_it_after_the_limit_is_full()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes(maxIdentities: 2);
        var first = lanes.Wrap(new ScriptedPipeline("a", Metadata("provider", "https://first.example.test/v1/", "m")));
        var second = lanes.Wrap(new ScriptedPipeline("b", Metadata("provider", "https://second.example.test/v1/", "m")));

        lanes.Wrap(new ScriptedPipeline("c", Metadata("provider", "https://third.example.test/v1/", "m")));
        lanes.AdmittedIdentities.ShouldBe(2);

        // A pipeline built later for an endpoint that was accepted still finds its lane, and a lane taken back would turn
        // the series of that endpoint into two.
        var again = lanes.Wrap(new ScriptedPipeline("d", Metadata("provider", "https://first.example.test/other/", "m")));

        foreach (var client in new[] { first, second, again })
        {
            await client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);
        }

        lanes.OverflowedCalls.ShouldBe(1);
        lanes.LaneCount.ShouldBe(3);
        probe.Spans().Select(static span => Tag(span, "server.address")).ShouldBe(["first.example.test", "second.example.test", "first.example.test"]);
    }

    [Fact]
    public void A_pipeline_that_names_no_endpoint_takes_no_place_whatever_the_limit()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes(maxIdentities: 0);

        lanes.Wrap(new ScriptedPipeline("a", Metadata("provider-one", uri: null, "m")));
        lanes.Wrap(new ScriptedPipeline("b", Metadata("provider-two", uri: null, "m")));
        lanes.Wrap(new ScriptedPipeline("c"));

        lanes.AdmittedIdentities.ShouldBe(0);
        lanes.OverflowedCalls.ShouldBe(0, "a provider that reports no endpoint is not over a limit that counts endpoints");
        lanes.LaneCount.ShouldBe(3);
    }

    [Fact]
    public void A_limit_of_zero_keys_no_endpoint_and_every_endpoint_uses_the_lane_of_its_provider()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes(maxIdentities: 0);

        lanes.Wrap(new ScriptedPipeline("a", Metadata("provider-one", "https://one.example.test/", "m")));
        lanes.Wrap(new ScriptedPipeline("b", Metadata("provider-one", "https://two.example.test:8443/", "m")));
        lanes.Wrap(new ScriptedPipeline("c", Metadata("provider-two", "https://one.example.test/", "m")));
        lanes.Wrap(new ScriptedPipeline("d", Metadata("provider-one", uri: null, "m")));

        lanes.AdmittedIdentities.ShouldBe(0);
        lanes.OverflowedCalls.ShouldBe(3);
        lanes.LaneCount.ShouldBe(2, "one lane for each provider; a pipeline that names no endpoint shares the lane the overflow uses");
    }

    [Fact]
    public void The_lane_of_the_overflow_is_built_for_a_client_that_names_the_provider_and_nothing_else()
    {
        using var probe = new ChatSpanProbe();
        var built = new List<(ChatTelemetryKey Key, ChatClientMetadata? Metadata)>();
        using var lanes = probe.NewLanes(
            createLane: (key, metadata) =>
            {
                built.Add((key, metadata));

                return new ChatTelemetryLane(key, metadata, probe.Source, logger: null);
            },
            maxIdentities: 1);

        lanes.Wrap(new ScriptedPipeline("a", Metadata("provider", "https://admitted.example.test:8443/", "m")));
        lanes.Wrap(new ScriptedPipeline("b", Metadata("provider", "https://turned-away.example.test:9443/v1/?key=value", "another-model")));

        var (key, metadata) = built.Last();

        // If the lane were told the endpoint of the pipeline that happened to arrive first, every span of the overflow would
        // name that tenant's host.
        key.ShouldBe(new ChatTelemetryKey("provider", ServerAddress: null, ServerPort: 0));
        metadata.ShouldNotBeNull().ProviderName.ShouldBe("provider");
        metadata.ProviderUri.ShouldBeNull();
        metadata.DefaultModelId.ShouldBeNull();
    }

    [Fact]
    public async Task Endpoints_that_arrive_at_the_same_moment_are_accepted_up_to_the_limit_and_not_one_more()
    {
        using var probe = new ChatSpanProbe();
        const int limit = 16;
        const int arrivals = 64;

        // A slow builder widens the window in which two endpoints could both take the last place.
        using var lanes = probe.NewLanes(
            createLane: (key, metadata) =>
            {
                Thread.Sleep(2);

                return new ChatTelemetryLane(key, metadata, probe.Source, logger: null);
            },
            maxIdentities: limit);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var wraps = Enumerable.Range(0, arrivals)
            .Select(index => Task.Run(
                async () =>
                {
                    await start.Task;

                    return lanes.Wrap(new ScriptedPipeline($"tenant-{index}", Metadata("provider", $"https://host-{index}.example.test/", "m")));
                },
                TestContext.Current.CancellationToken))
            .ToList();

        start.SetResult();
        await Task.WhenAll(wraps);

        lanes.AdmittedIdentities.ShouldBe(limit);
        lanes.OverflowedCalls.ShouldBe(arrivals - limit);
        lanes.LaneCount.ShouldBe(limit + 1);
        probe.SourcesCreated.ShouldBe(limit + 1);
    }

    [Fact]
    public void The_first_endpoint_over_the_limit_writes_one_warning_that_names_the_setting_and_no_endpoint()
    {
        using var probe = new ChatSpanProbe();
        var logs = new WarningLogFactory();
        using var lanes = probe.NewLanes(logs, maxIdentities: 2);

        for (var endpoint = 0; endpoint < 1000; endpoint++)
        {
            lanes.Wrap(new ScriptedPipeline("a", Metadata("provider", $"https://resource-{endpoint}.secret-tenant.example.test:{9000 + endpoint}/v1/", "private-model")));
        }

        var warning = logs.Entries.ShouldHaveSingleItem();

        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain(OptionName);
        warning.Message.ShouldContain("2");
        warning.Message.ShouldNotContain("secret-tenant", Case.Insensitive);
        warning.Message.ShouldNotContain("resource-", Case.Insensitive);
        warning.Message.ShouldNotContain("9000");
        warning.Message.ShouldNotContain("private-model", Case.Insensitive);
        lanes.OverflowedCalls.ShouldBe(998);
    }

    [Fact]
    public void A_lane_that_cannot_be_built_spends_no_place()
    {
        using var probe = new ChatSpanProbe();
        var broken = true;
        using var lanes = probe.NewLanes(
            createLane: (key, metadata) => broken
                ? throw new InvalidOperationException("no meter")
                : new ChatTelemetryLane(key, metadata, probe.Source, logger: null),
            maxIdentities: 1);

        lanes.Wrap(new ScriptedPipeline("a", Metadata("provider", "https://first.example.test/", "m")));

        lanes.AdmittedIdentities.ShouldBe(0, "the place is spent when the lane is stored, so a builder that throws costs none");

        broken = false;
        lanes.Wrap(new ScriptedPipeline("b", Metadata("provider", "https://second.example.test/", "m")));

        lanes.AdmittedIdentities.ShouldBe(1);
        lanes.OverflowedCalls.ShouldBe(0, "the failure was not remembered, and the next endpoint found the place free");
    }

    [Fact]
    public async Task A_pool_that_is_disposed_disposes_the_lane_of_the_overflow_too()
    {
        using var probe = new ChatSpanProbe();
        var lanes = probe.NewLanes(maxIdentities: 0);
        var pipeline = new ScriptedPipeline("a", Metadata("provider", "https://one.example.test/", "m"));
        var client = lanes.Wrap(pipeline);

        await client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);
        lanes.LaneCount.ShouldBe(1);

        lanes.Dispose();

        lanes.LaneCount.ShouldBe(0);

        var after = new ScriptedPipeline("b", Metadata("provider", "https://two.example.test/", "m"));
        ReferenceEquals(lanes.Wrap(after), after).ShouldBeTrue("a disposed pool lends nothing, the overflow included");
    }

    [Fact]
    public async Task Calls_of_two_tenants_held_in_flight_together_on_the_lane_of_the_overflow_each_reach_their_own_pipeline()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes(maxIdentities: 0);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tenantA = new ScriptedPipeline("tenant-a", Metadata("provider", "https://a.example.test/", "m"), gate.Task);
        var tenantB = new ScriptedPipeline("tenant-b", Metadata("provider", "https://b.example.test/", "m"), gate.Task);
        var clientA = lanes.Wrap(tenantA);
        var clientB = lanes.Wrap(tenantB);

        // Two endpoints of two tenants share one lane. A lane that held the target of a call in a field would hand the
        // call of one tenant to the pipeline of the other.
        var calls = Enumerable.Range(0, 100)
            .Select(index => (Expected: index % 2 == 0 ? "tenant-a" : "tenant-b", Call: (index % 2 == 0 ? clientA : clientB).GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken)))
            .ToList();

        await WaitUntil.TrueAsync(() => tenantA.Calls == 50 && tenantB.Calls == 50);
        gate.SetResult();

        foreach (var (expected, call) in calls)
        {
            (await call).Text.ShouldBe(expected);
        }

        lanes.LaneCount.ShouldBe(1, "both tenants went through the one lane of the provider");
        probe.Spans().Count.ShouldBe(100);
    }

    [Fact]
    public void A_negative_limit_is_refused()
    {
        using var probe = new ChatSpanProbe();

        Should.Throw<ArgumentOutOfRangeException>(() => probe.NewLanes(maxIdentities: -1));
        Should.Throw<ArgumentOutOfRangeException>(() => new TelemetryLanePool(probe.Source, enableSensitiveData: false, maxIdentities: -1));
    }

    [Fact]
    public async Task Agents_past_the_limit_borrow_lanes_of_their_provider_and_lose_only_the_model_and_endpoint_labels()
    {
        using var probe = new ChatSpanProbe();
        using var pool = new TelemetryLanePool(probe.Source, enableSensitiveData: false, maxIdentities: 3);

        for (var agent = 0; agent < 10; agent++)
        {
            var pooled = new PooledOpenTelemetryAgent(Agent($"agent-{agent}", "provider", $"https://host-{agent}.example.test:8443/v1", "model-1"), pool);

            await pooled.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        }

        pool.AdmittedIdentities.ShouldBe(3);
        pool.OverflowedCalls.ShouldBe(7);
        pool.LaneCount.ShouldBe(4, "one lane for each of the three identities and one for the overflow, because the runs came one after another");

        var spans = AgentSpans(probe);

        spans.Count.ShouldBe(10, "an identity without a lane of its own still writes the span of its run");
        spans.Select(static span => Tag(span, "gen_ai.agent.name")).ShouldBe(Enumerable.Range(0, 10).Select(static agent => $"agent-{agent}"));
        spans.ShouldAllBe(static span => Has(span, "gen_ai.provider.name", "provider"));
        spans.Take(3).ShouldAllBe(static span => Tag(span, "server.address") != null && Has(span, "server.port", "8443") && Has(span, "gen_ai.request.model", "model-1"));
        spans.Skip(3).ShouldAllBe(static span =>
            Tag(span, "server.address") == null && Tag(span, "server.port") == null && Tag(span, "gen_ai.request.model") == null);
    }

    [Fact]
    public async Task A_model_alone_counts_as_an_identity_and_an_agent_that_names_neither_takes_no_place()
    {
        using var probe = new ChatSpanProbe();
        using var pool = new TelemetryLanePool(probe.Source, enableSensitiveData: false, maxIdentities: 2);

        foreach (var model in new[] { "model-1", "model-2", "model-3", "model-4" })
        {
            await new PooledOpenTelemetryAgent(Agent("agent", "provider", endpoint: null, model), pool)
                .RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        }

        await new PooledOpenTelemetryAgent(Agent("quiet", "provider", endpoint: null, model: null), pool)
            .RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        pool.AdmittedIdentities.ShouldBe(2, "the default model is free text of the agent definition, so it is counted");
        pool.OverflowedCalls.ShouldBe(2);

        var spans = AgentSpans(probe);

        spans.Select(static span => Tag(span, "gen_ai.request.model")).ShouldBe(["model-1", "model-2", null, null, null]);
    }

    [Fact]
    public async Task An_agent_whose_chat_client_says_nothing_about_itself_is_never_turned_away()
    {
        using var probe = new ChatSpanProbe();
        using var pool = new TelemetryLanePool(probe.Source, enableSensitiveData: false, maxIdentities: 0);
        var agent = new PooledOpenTelemetryAgent(new ChatClientAgent(new ScriptedPipeline("answer"), new ChatClientAgentOptions { Name = "quiet" }), pool);

        await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        pool.AdmittedIdentities.ShouldBe(0);
        pool.OverflowedCalls.ShouldBe(0);
        pool.LaneCount.ShouldBe(1);
        AgentSpans(probe).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Agent_identities_that_arrive_at_the_same_moment_are_accepted_up_to_the_limit_and_not_one_more()
    {
        using var probe = new ChatSpanProbe();
        const int limit = 16;
        const int arrivals = 64;
        using var pool = new TelemetryLanePool(probe.Source, enableSensitiveData: false, maxIdentities: limit);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runs = Enumerable.Range(0, arrivals)
            .Select(index => Task.Run(
                async () =>
                {
                    await start.Task;
                    await new PooledOpenTelemetryAgent(Agent($"agent-{index}", "provider", $"https://host-{index}.example.test/", "model"), pool)
                        .RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
                },
                TestContext.Current.CancellationToken))
            .ToList();

        start.SetResult();
        await Task.WhenAll(runs);

        pool.AdmittedIdentities.ShouldBe(limit);
        pool.OverflowedCalls.ShouldBe(arrivals - limit);
        AgentSpans(probe).Count.ShouldBe(arrivals);
    }

    [Fact]
    public async Task The_first_agent_identity_over_the_limit_writes_one_warning_that_names_the_setting_and_no_endpoint_or_model()
    {
        using var probe = new ChatSpanProbe();
        var logs = new WarningLogFactory();
        using var pool = new TelemetryLanePool(probe.Source, enableSensitiveData: false, logs.CreateLogger("pool"), maxIdentities: 2);

        for (var identity = 0; identity < 1000; identity++)
        {
            await new PooledOpenTelemetryAgent(
                    Agent("agent", "provider", $"https://resource-{identity}.secret-tenant.example.test:{9000 + identity}/", "private-model"),
                    pool)
                .RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        }

        var warning = logs.Entries.ShouldHaveSingleItem();

        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain(OptionName);
        warning.Message.ShouldContain("2");
        warning.Message.ShouldNotContain("secret-tenant", Case.Insensitive);
        warning.Message.ShouldNotContain("resource-", Case.Insensitive);
        warning.Message.ShouldNotContain("9000");
        warning.Message.ShouldNotContain("private-model", Case.Insensitive);
        pool.OverflowedCalls.ShouldBe(998);
    }

    [Fact]
    public async Task An_identity_keeps_its_place_when_its_lane_could_not_be_built_and_the_next_run_builds_it()
    {
        using var probe = new ChatSpanProbe();
        var attempts = 0;
        using var pool = new TelemetryLanePool(
            probe.Source,
            enableSensitiveData: false,
            logger: null,
            createLane: (identity, source, sensitive) => Interlocked.Increment(ref attempts) == 1
                ? throw new InvalidOperationException("the first build fails (probe)")
                : new TelemetryLane(identity, source, sensitive),
            maxIdentities: 1);
        var admitted = new PooledOpenTelemetryAgent(Agent("a", "provider", "https://first.example.test/", "m"), pool);
        var other = new PooledOpenTelemetryAgent(Agent("b", "provider", "https://second.example.test/", "m"), pool);

        await admitted.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        pool.AdmittedIdentities.ShouldBe(1, "the place belongs to the identity, not to a lane");
        pool.LaneCount.ShouldBe(0);

        await admitted.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        await other.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        pool.LaneCount.ShouldBe(2);
        pool.OverflowedCalls.ShouldBe(1);

        var spans = AgentSpans(probe);

        Tag(spans[0], "server.address").ShouldBe("first.example.test", "the identity that failed to get a lane once is not turned into an overflow");
        Tag(spans[1], "server.address").ShouldBeNull();
    }

    [Fact]
    public async Task Runs_of_two_tenants_held_in_flight_together_on_the_overflow_each_name_their_own_agent()
    {
        using var probe = new ChatSpanProbe();
        using var pool = new TelemetryLanePool(probe.Source, enableSensitiveData: false, maxIdentities: 0);
        const int concurrent = 60;
        var entered = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var alpha = new PooledOpenTelemetryAgent(HoldingAgent("alpha", "https://a.example.test/", OnEntered, release.Task), pool);
        var beta = new PooledOpenTelemetryAgent(HoldingAgent("beta", "https://b.example.test/", OnEntered, release.Task), pool);

        void OnEntered()
        {
            if (Interlocked.Increment(ref entered) == concurrent)
            {
                release.SetResult();
            }
        }

        await Task.WhenAll(Enumerable.Range(0, concurrent)
            .Select(run => (run % 2 == 0 ? alpha : beta).RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken)));

        var spans = AgentSpans(probe);

        spans.Count.ShouldBe(concurrent);
        spans.Count(static span => Has(span, "gen_ai.agent.name", "alpha")).ShouldBe(concurrent / 2);
        spans.Count(static span => Has(span, "gen_ai.agent.name", "beta")).ShouldBe(concurrent / 2);
        spans.ShouldAllBe(static span => Tag(span, "server.address") == null);
        pool.LaneCount.ShouldBe(concurrent, "every run in flight holds a lane of its own, and all of them are lanes of the overflow");
        pool.IdleCount.ShouldBe(concurrent);
    }

    [Fact]
    public async Task The_decorator_hands_the_limit_of_the_options_to_its_pool()
    {
        var descriptor = new AgentDescriptor { Name = "alpha", Origin = AgentDefinitionOrigin.Code, SourceName = "test" };
        using var decorator = new OpenTelemetryAgentDecorator(Options.Create(new TraconOptions { Observability = { MaxTelemetryLaneCardinality = 1 } }));

        foreach (var endpoint in new[] { "first", "second", "third" })
        {
            var decorated = decorator.Decorate(Agent(endpoint, "provider", $"https://{endpoint}.example.test/", "m"), descriptor);

            await decorated.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        }

        decorator.LaneCount.ShouldBe(2, "the identity the limit allows and the lane of the overflow");
    }

    private static ChatClientMetadata Metadata(string? provider, string? uri, string model)
        => new(provider, uri is null ? null : new Uri(uri), model);

    private static ChatClientAgent Agent(string name, string provider, string? endpoint, string? model)
        => new(
            new ScriptedPipeline("answer", new ChatClientMetadata(provider, endpoint is null ? null : new Uri(endpoint), model)),
            new ChatClientAgentOptions { Id = name + "-id", Name = name, Description = "about " + name });

    private static ChatClientAgent HoldingAgent(string name, string endpoint, Action entered, Task release)
        => new(
            new HoldingPipeline(new ChatClientMetadata("provider", new Uri(endpoint), "m"), entered, release),
            new ChatClientAgentOptions { Id = name + "-id", Name = name });

    private static List<Activity> AgentSpans(ChatSpanProbe probe)
        => [.. probe.Spans().Where(static span => Has(span, "gen_ai.operation.name", "invoke_agent"))];

    private static string? Tag(Activity span, string key) => span.GetTagItem(key)?.ToString();

    private static bool Has(Activity span, string key, string value) => string.Equals(Tag(span, key), value, StringComparison.Ordinal);

    /// <summary>A chat client that reports its metadata, tells the test it was reached, and answers once it is let go.</summary>
    private sealed class HoldingPipeline(ChatClientMetadata metadata, Action entered, Task release) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            entered();
            await release.WaitAsync(cancellationToken).ConfigureAwait(false);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(metadata) ? metadata : null;

        public void Dispose()
        {
        }
    }
}
