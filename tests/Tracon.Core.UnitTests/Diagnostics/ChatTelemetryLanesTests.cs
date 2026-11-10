using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The telemetry client of the model-call pipelines is shared through lanes: a pipeline sends each call through the
/// lane of its provider and endpoint with the target of that call, and the meter behind the lane lives as long as the
/// pool.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 What the tests guard is the set of facts that make a lane the right shape. A lane per pipeline is the leak the
/// pool exists to stop (one lane is one meter and one activity source, counted here by the activity sources a probe's
/// listener sees). A lane that keeps the target of a call in a field would send a call of one tenant to the pipeline of
/// another, so the calls of two tenants are held in flight together on one lane and each must get its own tenant's
/// answer. A lane that is disposed after a call splits the series the OpenTelemetry SDK exports into one series per call,
/// and a disposed activity source writes no span, so every call of a long run must write one.
/// </para>
/// <para>
/// Every pool here writes to a source name of its own. The instruments belong to the framework's client and cannot be
/// bound by instance, so the process-wide count of published instruments is asserted by a test that compiles through the
/// registry.
/// </para>
/// </remarks>
public sealed class ChatTelemetryLanesTests
{
    private static readonly IReadOnlyList<ChatMessage> Messages = [new ChatMessage(ChatRole.User, "hello")];

    [Fact]
    public async Task Pipelines_of_one_provider_and_endpoint_share_one_lane_whatever_their_tenant_or_model()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var pipelines = Enumerable.Range(0, 50)
            .Select(index => new ScriptedPipeline($"tenant-{index}", Metadata("provider", "https://api.example.test:8443/v1/", $"model-{index}")))
            .ToList();
        var wrapped = pipelines.Select(pipeline => lanes.Wrap(pipeline)).ToList();

        foreach (var client in wrapped)
        {
            await client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);
        }

        lanes.LaneCount.ShouldBe(1, "the tenant is not part of the key, and the model of a pipeline travels with its calls");
        probe.SourcesCreated.ShouldBe(1, "a lane is one meter and one activity source, and a lane per pipeline is the leak this pool exists to stop");
        probe.Spans().Count.ShouldBe(50, "sharing the lane loses no span");
    }

    [Fact]
    public void A_provider_or_an_endpoint_of_its_own_gets_a_lane_of_its_own()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var identities = new (string? Provider, string? Uri)[]
        {
            ("provider-one", "https://api.example.test/v1/"),
            ("provider-two", "https://api.example.test/v1/"),
            ("provider-one", "https://other.example.test/v1/"),
            ("provider-one", "https://api.example.test:8443/v1/"),
            ("provider-one", null),
            (null, null),
        };

        foreach (var (provider, uri) in identities)
        {
            lanes.Wrap(new ScriptedPipeline("a", provider is null && uri is null ? null : Metadata(provider, uri, "m")));
        }

        lanes.LaneCount.ShouldBe(identities.Length);

        // The same identities again, with other models and other paths: nothing new.
        foreach (var (provider, uri) in identities)
        {
            lanes.Wrap(new ScriptedPipeline("b", provider is null && uri is null ? null : Metadata(provider, uri?.Replace("/v1/", "/v2/", StringComparison.Ordinal), "another-model")));
        }

        lanes.LaneCount.ShouldBe(identities.Length);
        probe.SourcesCreated.ShouldBe(identities.Length);
    }

    [Fact]
    public async Task Calls_held_in_flight_together_on_one_lane_each_reach_the_pipeline_they_were_made_through()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = Metadata("provider", "https://api.example.test/v1/", "model");
        var tenantA = new ScriptedPipeline("tenant-a", metadata, gate.Task);
        var tenantB = new ScriptedPipeline("tenant-b", metadata, gate.Task);
        var clientA = lanes.Wrap(tenantA);
        var clientB = lanes.Wrap(tenantB);

        // Every call is inside its pipeline, behind the gate, before any of them is let out: a lane that kept the target
        // of a call in a field would hold the last one's.
        var calls = Enumerable.Range(0, 200)
            .Select(index => (Expected: index % 2 == 0 ? "tenant-a" : "tenant-b", Call: (index % 2 == 0 ? clientA : clientB).GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken)))
            .ToList();

        await WaitUntil.TrueAsync(() => tenantA.Calls == 100 && tenantB.Calls == 100);
        gate.SetResult();

        foreach (var (expected, call) in calls)
        {
            (await call).Text.ShouldBe(expected);
        }

        lanes.LaneCount.ShouldBe(1, "the two tenants shared one lane, which is what the test is about");
        probe.Spans().Count.ShouldBe(200);
    }

    [Fact]
    public async Task Streams_opened_together_on_one_lane_and_read_afterwards_each_come_from_the_pipeline_they_were_made_through()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = Metadata("provider", "https://api.example.test/v1/", "model");
        var tenantA = new ScriptedPipeline("tenant-a", metadata, gate.Task);
        var tenantB = new ScriptedPipeline("tenant-b", metadata, gate.Task);
        var clientA = lanes.Wrap(tenantA);
        var clientB = lanes.Wrap(tenantB);

        // A stream does nothing until it is read, so a lane that holds the target of a call anywhere but in the call
        // itself would hold the target of the last stream opened when the first one is read.
        var opened = Enumerable.Range(0, 100)
            .Select(index => (
                Expected: index % 2 == 0 ? "tenant-a" : "tenant-b",
                Stream: (index % 2 == 0 ? clientA : clientB).GetStreamingResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken)))
            .ToList();

        var reads = opened.Select(stream => (stream.Expected, Read: ReadAsync(stream.Stream))).ToList();

        await WaitUntil.TrueAsync(() => tenantA.Calls == 50 && tenantB.Calls == 50);
        gate.SetResult();

        foreach (var (expected, read) in reads)
        {
            (await read).ShouldBe([$"{expected}-1", $"{expected}-2"]);
        }

        lanes.LaneCount.ShouldBe(1);
        probe.Spans().Count.ShouldBe(100);

        static async Task<List<string>> ReadAsync(IAsyncEnumerable<ChatResponseUpdate> stream)
        {
            var texts = new List<string>();

            await foreach (var update in stream)
            {
                if (update.Text is { Length: > 0 } text)
                {
                    texts.Add(text);
                }
            }

            return texts;
        }
    }

    [Fact]
    public async Task A_lane_refuses_a_call_that_comes_without_the_target_it_was_made_for()
    {
        using var probe = new ChatSpanProbe();
        using var lane = new ChatTelemetryLane(ChatTelemetryKey.Of(null), metadata: null, probe.Source, logger: null);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await lane.Client.GetResponseAsync(Messages, options: null, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await lane.Client.GetResponseAsync(Messages, new ChatOptions { ModelId = "m" }, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<InvalidOperationException>(
            async () =>
            {
                await foreach (var unused in lane.Client.GetStreamingResponseAsync(Messages, new ChatOptions(), TestContext.Current.CancellationToken))
                {
                }
            });
    }

    [Fact]
    public async Task The_options_a_caller_gives_reach_the_pipeline_as_they_were_and_none_stays_none()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var pipeline = new ScriptedPipeline("a", Metadata("provider", "https://api.example.test/", "default-model"));
        var client = lanes.Wrap(pipeline);
        var options = new ChatOptions { Temperature = 0.2f, AdditionalProperties = new AdditionalPropertiesDictionary { ["key"] = "value" } };

        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
        ReferenceEquals(pipeline.LastOptions, options).ShouldBeTrue("the pipeline under the telemetry ring sees the caller's options, not the ring's copy");
        options.ModelId.ShouldBeNull("the default model is put on the ring's copy, never on the caller's options");

        await client.GetResponseAsync(Messages, options: null, TestContext.Current.CancellationToken);
        pipeline.LastOptions.ShouldBeNull();

        await foreach (var unused in client.GetStreamingResponseAsync(Messages, options, TestContext.Current.CancellationToken))
        {
        }

        ReferenceEquals(pipeline.LastOptions, options).ShouldBeTrue();
    }

    [Fact]
    public async Task A_long_run_of_calls_writes_a_span_for_every_one_because_the_lane_is_never_disposed()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var client = lanes.Wrap(new ScriptedPipeline("a", Metadata("provider", "https://api.example.test/", "m")));

        for (var call = 0; call < 100; call++)
        {
            await client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);
        }

        // A lane disposed after a call would split the exported metric into a series per call, and a disposed activity
        // source writes no span.
        probe.Spans().Count.ShouldBe(100);
        lanes.LaneCount.ShouldBe(1);
    }

    [Fact]
    public async Task Disposing_a_pipeline_disposes_what_is_under_the_ring_and_leaves_the_lane_alone()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var metadata = Metadata("provider", "https://api.example.test/", "m");
        var first = new ScriptedPipeline("first", metadata);
        var second = new ScriptedPipeline("second", metadata);
        var firstClient = lanes.Wrap(first);
        var secondClient = lanes.Wrap(second);

        firstClient.Dispose();

        first.Disposed.ShouldBeTrue();
        second.Disposed.ShouldBeFalse();

        (await secondClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken)).Text.ShouldBe("second");
        probe.Spans().Count.ShouldBe(1, "the lane the disposed pipeline used still writes spans for the one that did not");
    }

    [Fact]
    public async Task A_stream_the_consumer_leaves_early_ends_its_span_and_leaves_the_lane_ready()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var client = lanes.Wrap(new ScriptedPipeline("a", Metadata("provider", "https://api.example.test/", "m")));

        await foreach (var unused in client.GetStreamingResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            break;
        }

        probe.Spans().Count.ShouldBe(1, "leaving the stream ends the span of that call");

        await client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        probe.Spans().Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_pipeline_that_fails_writes_the_failure_on_the_span_and_the_error_reaches_the_caller()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var client = lanes.Wrap(new ScriptedPipeline("a", Metadata("provider", "https://api.example.test/", "m"), failure: new InvalidOperationException("model down")));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            async () => await client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        thrown.Message.ShouldBe("model down");

        var span = probe.Spans().ShouldHaveSingleItem();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.GetTagItem("error.type").ShouldBe(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task Many_pipelines_of_one_identity_wrapped_at_the_same_moment_make_one_lane()
    {
        using var probe = new ChatSpanProbe();
        var built = 0;
        using var holdBuilder = new ManualResetEventSlim();

        // The builder is held until every caller has piled up behind it, which widens the window two pipelines that miss
        // together have to both build a lane to the whole of the callers' arrival, and not to a guess of how long it takes.
        using var lanes = probe.NewLanes(
            createLane: (key, metadata) =>
            {
                Interlocked.Increment(ref built);
                holdBuilder.Wait(TestContext.Current.CancellationToken);

                return new ChatTelemetryLane(key, metadata, probe.Source, logger: null);
            });
        var metadata = Metadata("provider", "https://api.example.test/", "m");
        var clients = new IChatClient[64];

        // Dedicated threads, so that every caller can be parked at once whatever the thread pool is willing to start.
        var callers = Enumerable.Range(0, clients.Length)
            .Select(index => new Thread(() => clients[index] = lanes.Wrap(new ScriptedPipeline($"tenant-{index}", metadata))) { IsBackground = true })
            .ToArray();

        try
        {
            foreach (var caller in callers)
            {
                caller.Start();
            }

            // A caller that missed is inside the builder or waits for the lock the builder holds: parked either way.
            await WaitUntil.TrueAsync(
                () => callers.All(static caller => caller.ThreadState.HasFlag(System.Threading.ThreadState.WaitSleepJoin)),
                "every caller to be parked behind the builder");
        }
        finally
        {
            holdBuilder.Set();
        }

        foreach (var caller in callers)
        {
            caller.Join();
        }

        // Two pipelines that miss together must not both build a client: the loser of a race would be dropped without a
        // dispose, and a meter that is dropped is a meter that stays.
        built.ShouldBe(1);
        lanes.LaneCount.ShouldBe(1);
        probe.SourcesCreated.ShouldBe(1);
        clients.ShouldAllBe(static client => client != null);
    }

    [Fact]
    public async Task The_pool_disposes_its_lanes_and_wraps_nothing_afterwards_while_a_call_in_flight_finishes()
    {
        using var probe = new ChatSpanProbe();
        var lanes = probe.NewLanes();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = Metadata("provider", "https://api.example.test/", "m");
        var waiting = new ScriptedPipeline("in-flight", metadata, gate.Task);
        var client = lanes.Wrap(waiting);
        var call = client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(() => waiting.Calls == 1);
        lanes.Dispose();
        gate.SetResult();

        (await call).Text.ShouldBe("in-flight", "the host stopping does not fail a call that is running");

        var after = new ScriptedPipeline("after", metadata);
        ReferenceEquals(lanes.Wrap(after), after).ShouldBeTrue("a disposed pool lends nothing: the pipeline runs without a model span");
        lanes.LaneCount.ShouldBe(0);
        lanes.Dispose();
    }

    [Fact]
    public async Task A_lane_that_cannot_be_built_costs_the_pipeline_its_spans_and_nothing_else()
    {
        using var probe = new ChatSpanProbe();
        var warnings = new WarningLogFactory();
        var broken = true;
        using var lanes = probe.NewLanes(
            warnings,
            (key, metadata) => broken
                ? throw new InvalidOperationException("no meter")
                : new ChatTelemetryLane(key, metadata, probe.Source, logger: null));
        var pipeline = new ScriptedPipeline("a", Metadata("provider", "https://api.example.test/", "m"));

        var wrapped = lanes.Wrap(pipeline);

        ReferenceEquals(wrapped, pipeline).ShouldBeTrue();
        (await wrapped.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken)).Text.ShouldBe("a");
        warnings.Entries.ShouldContain(static entry => entry.Level == LogLevel.Warning && entry.Exception is InvalidOperationException);
        lanes.LaneCount.ShouldBe(0);

        // Nothing about the failure is remembered: the next pipeline tries again.
        broken = false;
        var retried = lanes.Wrap(new ScriptedPipeline("b", Metadata("provider", "https://api.example.test/", "m")));
        (await retried.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken)).Text.ShouldBe("b");
        probe.Spans().Count.ShouldBe(1);
        lanes.LaneCount.ShouldBe(1);
    }

    [Fact]
    public void The_ring_answers_what_the_pipeline_underneath_says_about_itself_and_does_not_hand_out_the_shared_client()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var metadata = Metadata("provider", "https://api.example.test:8443/v1/", "default-model");
        var client = lanes.Wrap(new ScriptedPipeline("a", metadata));

        client.GetService<ChatClientMetadata>().ShouldBeSameAs(metadata);
        client.GetService<ScriptedPipeline>().ShouldNotBeNull();
        client.GetService<ActivitySource>().ShouldNotBeNull().Name.ShouldBe(probe.Source);
        client.GetService<OpenTelemetryChatClient>().ShouldBeNull("a flag set on the shared client would reach the pipelines of every tenant");
    }

    [Fact]
    public void A_pipeline_that_names_no_provider_still_gets_a_lane()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();

        lanes.Wrap(new ScriptedPipeline("a"));
        lanes.Wrap(new ScriptedPipeline("b"));

        lanes.LaneCount.ShouldBe(1);
    }

    [Fact]
    public void A_clone_of_the_options_that_carry_a_call_keeps_the_target_and_the_callers_options()
    {
        var target = new ScriptedPipeline("a");
        var original = new ChatOptions { Temperature = 0.3f, ModelId = "named-model" };
        var routed = new RoutedChatOptions(target, original, "default-model");

        // A layer between the ring and the lane that clones the options must not turn them into plain ones: the lane
        // would refuse the call, and a clone that lost the caller's options would hand the pipeline different ones.
        var clone = routed.Clone().ShouldBeOfType<RoutedChatOptions>();

        clone.ShouldNotBeSameAs(routed);
        clone.Target.ShouldBeSameAs(target);
        clone.Original.ShouldBeSameAs(original);
        clone.Temperature.ShouldBe(0.3f);
        clone.ModelId.ShouldBe("named-model");

        // The default model fills a call that names none, and never replaces one that does.
        new RoutedChatOptions(target, null, "default-model").ModelId.ShouldBe("default-model");
        new RoutedChatOptions(target, new ChatOptions(), "default-model").ModelId.ShouldBe("default-model");
        new RoutedChatOptions(target, null, defaultModelId: null).ModelId.ShouldBeNull();
    }

    [Fact]
    public async Task The_carrier_writes_no_span_tag_even_when_the_lane_records_the_options_of_a_call()
    {
        // With content recording on, the framework's client writes every additional property of the options as a tag. A
        // carrier kept there would write the target on the span; the carrier is a type of its own, so the tags are the
        // ones a client that never saw a carrier writes for the same options.
        using var directProbe = new ChatSpanProbe();
        using var sharedProbe = new ChatSpanProbe();
        var metadata = Metadata("provider", "https://api.example.test/v1/", "default-model");
        var pipeline = new ScriptedPipeline("answer", metadata);
        var options = new ChatOptions
        {
            Temperature = 0.2f,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["caller.key"] = "caller value" },
        };

        using var direct = new OpenTelemetryChatClient(pipeline, logger: null, directProbe.Source) { EnableSensitiveData = true };
        await direct.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        using var lane = new ChatTelemetryLane(ChatTelemetryKey.Of(metadata), metadata, sharedProbe.Source, logger: null);
        lane.Client.EnableSensitiveData = true;
        var shared = new SharedTelemetryChatClient(pipeline, lane.Client, metadata.DefaultModelId);
        await shared.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        var directTags = directProbe.Spans().ShouldHaveSingleItem().TagObjects.ToDictionary(static tag => tag.Key, static tag => tag.Value?.ToString(), StringComparer.Ordinal);
        var sharedTags = sharedProbe.Spans().ShouldHaveSingleItem().TagObjects.ToDictionary(static tag => tag.Key, static tag => tag.Value?.ToString(), StringComparer.Ordinal);

        sharedTags.ShouldContainKeyAndValue("caller.key", "caller value");
        sharedTags.Keys.Order(StringComparer.Ordinal).ShouldBe(directTags.Keys.Order(StringComparer.Ordinal));
        sharedTags.Values.ShouldNotContain(static value => value != null && value.Contains(nameof(ScriptedPipeline), StringComparison.Ordinal));
    }

    private static ChatClientMetadata Metadata(string? provider, string? uri, string model)
        => new(provider, uri is null ? null : new Uri(uri), model);
}
