using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The native agent stream (<c>POST /api/agents/{name}/run</c>) carries a <c>loop.iteration-completed</c>
/// frame after every evaluated iteration of a harness loop, between that iteration's last update and the next
/// iteration's first.
/// </summary>
/// <remarks>
/// <para>
/// Only a real host proves the order: the evaluator runs inside the stream's own enumeration, between two
/// <c>MoveNextAsync</c> calls of the endpoint's loop, and the frame is written from there. A unit test of the
/// evaluator would see the callback run and never see where the frame lands.
/// </para>
/// <para>
/// The sub-agent case is the class scan of the sibling surfaces: a child's loop writes to the child's run, and
/// nothing may carry it into the stream of the run above it.
/// </para>
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only"
#pragma warning disable MAAI001
public sealed class LoopIterationFrameTests
{
    private const string Frame = "loop.iteration-completed";
    private const string ProviderName = "looping";
    private const string ModelName = "loop-model";
    private const string AgentName = "looper";
    private const string RouterModel = "router-model";
    private const string ChildName = "arastirma-ci";
    private const string ChildTool = "arastirma_ci";

    [Fact]
    public async Task Every_evaluated_iteration_writes_a_frame_between_its_last_update_and_the_next_iterations_first()
    {
        var provider = Provider("still working", "nearly there", "ALL DONE");
        await using var host = await StartAsync(provider, MarkerLoop(maxIterations: 6));

        var frames = await PostStreamingAsync(host, AgentName);

        provider.Requests.Count.ShouldBe(3);
        frames[^1].Event.ShouldBe("done");

        // The frames of one iteration are the updates between two loop frames (the feedback the loop sends the
        // model back with is one of them); runs of updates collapse into one group.
        var groups = UpdateGroups(frames);

        groups.Count.ShouldBe(3, "three iterations, so three groups of updates, each closed by its own frame");
        groups[0].ShouldContain("still working");
        groups[0].ShouldNotContain("nearly there");
        groups[1].ShouldContain("nearly there");
        groups[1].ShouldNotContain("still working");
        groups[2].ShouldContain("ALL DONE");

        // The last iteration is evaluated too: it is the frame that says the loop stopped.
        var payloads = FramePayloads(frames);

        payloads.Count.ShouldBe(3);
        payloads.Select(static payload => payload.Iteration).ShouldBe([1, 2, 3]);
        payloads.Select(static payload => payload.Continued).ShouldBe([true, true, false]);
        payloads[^1].ContinuedBy.ShouldBeNull();

        // Frame ids are the stream's own sequence; the new frame leaves no gap in it.
        frames.Select(static frame => long.Parse(frame.Id!, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(Enumerable.Range(0, frames.Count).Select(static index => (long)index));
    }

    [Fact]
    public async Task A_loop_that_stops_at_the_ceiling_writes_one_frame_fewer_than_it_ran_iterations()
    {
        var provider = Provider("first draft", "second draft");
        await using var host = await StartAsync(provider, MarkerLoop(maxIterations: 2, marker: "NEVER APPEARS"));

        var frames = await PostStreamingAsync(host, AgentName);

        provider.Requests.Count.ShouldBe(2);

        // The turn that reaches the ceiling is not evaluated. The frame of the
        // turn before it says so, and the answer is the iteration AFTER that frame.
        var payload = FramePayloads(frames).ShouldHaveSingleItem();

        payload.Iteration.ShouldBe(1);
        payload.Continued.ShouldBeTrue();
        payload.CeilingReached.ShouldBeTrue();

        var groups = UpdateGroups(frames);

        groups.Count.ShouldBe(2);
        groups[1].ShouldContain("second draft");
    }

    [Fact]
    public async Task A_loop_that_fails_the_run_at_its_ceiling_sends_its_last_frame_before_the_error()
    {
        var provider = Provider("first draft", "second draft");
        var loop = MarkerLoop(maxIterations: 2, marker: "NEVER APPEARS") with { OnExhausted = LoopOutcome.FailRun };

        await using var host = await StartAsync(provider, loop);

        var frames = await PostStreamingAsync(host, AgentName);

        // FailRun evaluates the turn at the ceiling too, so every turn has a frame, and no turn follows the last.
        provider.Requests.Count.ShouldBe(2);
        FramePayloads(frames).Select(static payload => payload.Iteration).ShouldBe([1, 2]);

        frames[^1].Event.ShouldBe("error");
        IsLoopFrame(frames[^2]).ShouldBeTrue();

        using var last = JsonDocument.Parse(ReadPayloadJson(frames[^2].Data));

        last.RootElement.GetProperty("failsRun").GetBoolean().ShouldBeTrue();
        last.RootElement.GetProperty("ceilingReached").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_frame_data_is_the_event_the_events_stream_serves_byte_for_byte()
    {
        var provider = Provider("still working", "ALL DONE");
        await using var host = await StartAsync(provider, MarkerLoop(maxIterations: 6));

        var frames = await PostStreamingAsync(host, AgentName);
        var run = await OutputTruncationTests.SingleRunAsync(host);
        var recorded = await OutputTruncationStreamingTests.EventFramesAsync(host, run.Id);

        var live = frames.Where(IsLoopFrame).Select(static frame => frame.Data).ToList();
        var stored = recorded.Where(IsLoopFrame).Select(static frame => frame.Data).ToList();

        live.Count.ShouldBe(2);
        live.ShouldBe(stored);
    }

    [Fact]
    public async Task The_frame_data_is_the_stored_event_byte_for_byte_when_a_database_keeps_it()
    {
        // The in-memory store hands the very instance back; a database serializes the event and reads it again,
        // so only a database can show a field that loses something on the way (a timestamp finer than the column).
        using var database = new TempSqliteDatabase("loop-frame-bytes");

        var provider = Provider("still working", "nearly there", "ALL DONE");
        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.UseSqlite(database.ConnectionString);
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Description = "Works until it is done.",
                Instructions = "Work.",
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                Harness = new HarnessSettings { Loop = MarkerLoop(maxIterations: 6) },
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var frames = await PostStreamingAsync(host, AgentName);
        var run = await OutputTruncationTests.SingleRunAsync(host);
        var recorded = await OutputTruncationStreamingTests.EventFramesAsync(host, run.Id);

        var live = frames.Where(IsLoopFrame).Select(static frame => frame.Data).ToList();

        live.Count.ShouldBe(3);
        live.ShouldBe(recorded.Where(IsLoopFrame).Select(static frame => frame.Data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_agent_without_a_loop_writes_no_frame(bool harness)
    {
        var provider = Provider("just one answer");
        await using var host = await StartAsync(provider, loop: null, harness: harness);

        var frames = await PostStreamingAsync(host, AgentName);

        frames.ShouldNotContain(static frame => string.Equals(frame.Event, Frame, StringComparison.Ordinal));
        frames.Select(static frame => frame.Event).Distinct(StringComparer.Ordinal).ShouldBe(["run", "update", "done"]);
    }

    [Fact]
    public async Task A_sub_agents_loop_writes_to_its_own_run_and_not_to_the_stream_above_it()
    {
        var provider = new FakeModelProvider(ProviderName);

        provider.ForModel(
            RouterModel,
            static router => router
                .CallsTool(ChildTool, new { query = "sub task" })
                .EchoesLastToolResult("Delegated: "));
        provider.ForModel(ModelName, static child => child.RespondsWith("child draft", "child final ALL DONE"));

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = ChildName,
                Description = "Performs research.",
                Instructions = "Research.",
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                Harness = new HarnessSettings { Loop = MarkerLoop(maxIterations: 4) },
                Origin = AgentDefinitionOrigin.Code,
            });
            builder.AddAgent(new AgentDefinition
            {
                Name = "router",
                Description = "Delegates the work.",
                Instructions = "Delegate.",
                Model = new ModelBinding { Provider = ProviderName, Model = RouterModel },
                CallableAgents = [ChildName],
                SubAgents = new SubAgentSettings { CallMode = SubAgentCallMode.Tool },
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var frames = await PostStreamingAsync(host, "router");

        frames.ShouldContain(static frame => string.Equals(frame.Event, "done", StringComparison.Ordinal));
        frames.ShouldNotContain(static frame => string.Equals(frame.Event, Frame, StringComparison.Ordinal));

        // The loop did run: the child's own run recorded both iterations.
        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });
        var child = all.Single(static run => run.Depth == 1);
        var childEvents = new List<RunEvent>();

        await foreach (var runEvent in runs.ReadEventsAsync(child.Id))
        {
            childEvents.Add(runEvent);
        }

        childEvents.Count(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted).ShouldBe(2);
    }

    [Fact]
    public async Task A_criterion_that_fails_to_evaluate_writes_a_frame_that_names_it_and_the_stream_ends_with_done()
    {
        var thrower = new DelegateLoopEvaluator(static (_, _) => throw new InvalidOperationException("the judge is unreachable"));
        var loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = "explodes" }], MaxIterations = 6 };

        var provider = Provider("first answer");
        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddLoopEvaluator("explodes", thrower);
            builder.AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Description = "Works until it is done.",
                Instructions = "Work.",
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                Harness = new HarnessSettings { Loop = loop },
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var frames = await PostStreamingAsync(host, AgentName);

        // OnCriterionError defaults to ReturnLastResponse: the work is kept, the run completes, and the one
        // frame says which criterion could not decide.
        var payload = FramePayloads(frames).ShouldHaveSingleItem();

        payload.FailedCriterion.ShouldBe("explodes");
        payload.Continued.ShouldBeFalse();
        frames[^1].Event.ShouldBe("done");
    }

    [Fact]
    public async Task A_failing_callback_fails_the_run_and_is_not_swallowed_as_a_criterion_failure()
    {
        // OnCriterionError defaults to Stop, which swallows a criterion that throws. The callback writes to the
        // caller's stream; if it ran inside that try/catch, a dead connection would end the loop quietly and the
        // run would complete.
        var provider = Provider("still working", "ALL DONE");
        await using var host = await StartAsync(provider, MarkerLoop(maxIterations: 6));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var agent = (await catalog.ResolveAsync(AgentName, culture: null, CancellationToken.None)).ShouldNotBeNull();

        var options = new TraconRunOptions
        {
            AfterLoopIterationRecorded = static (_, _) => throw new InvalidOperationException("the stream is gone"),
        };

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var update in agent.RunStreamingAsync("go", session: null, options))
            {
                _ = update;
            }
        });

        failure.Message.ShouldBe("the stream is gone");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        (await host.WaitForTerminalRunAsync(root.Id)).Status.ShouldBe(RunStatus.Failed);

        // The event was recorded before the callback ran, and it does not name a failed criterion.
        var iteration = new List<RunEvent>();

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            iteration.Add(runEvent);
        }

        var recorded = iteration.Single(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted);

        ReadPayload(recorded).FailedCriterion.ShouldBeNull();
    }

    private static bool IsLoopFrame(SseFrame frame) => string.Equals(frame.Event, Frame, StringComparison.Ordinal);

    /// <summary>The text of every group of updates, a group being the updates between two loop frames (or the stream's ends).</summary>
    private static List<string> UpdateGroups(IReadOnlyList<SseFrame> frames)
    {
        var groups = new List<string>();
        var current = new List<string>();

        foreach (var frame in frames)
        {
            if (string.Equals(frame.Event, "update", StringComparison.Ordinal))
            {
                current.Add(frame.Data);
            }
            else if (IsLoopFrame(frame))
            {
                groups.Add(string.Concat(current));
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            groups.Add(string.Concat(current));
        }

        return groups;
    }

    private static List<IterationPayload> FramePayloads(IEnumerable<SseFrame> frames)
        => [.. frames.Where(IsLoopFrame).Select(static frame => ReadPayload(frame.Data))];

    private static IterationPayload ReadPayload(RunEvent runEvent) => ParsePayload(runEvent.Payload.ShouldNotBeNull());

    /// <summary>Reads the payload of a frame's data, which is a serialized <see cref="RunEvent"/> whose payload is a JSON string.</summary>
    private static IterationPayload ReadPayload(string frameData) => ParsePayload(ReadPayloadJson(frameData));

    private static string ReadPayloadJson(string frameData)
    {
        using var frame = JsonDocument.Parse(frameData);

        return frame.RootElement.GetProperty("payload").GetString().ShouldNotBeNull();
    }

    private static IterationPayload ParsePayload(string payloadJson)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        var root = payload.RootElement;

        return new IterationPayload(
            root.GetProperty("iteration").GetInt32(),
            root.GetProperty("continued").GetBoolean(),
            root.GetProperty("continuedBy").ValueKind == JsonValueKind.Null
                ? null
                : root.GetProperty("continuedBy").GetString(),
            root.GetProperty("failedCriterion").ValueKind == JsonValueKind.Null
                ? null
                : root.GetProperty("failedCriterion").GetString(),
            root.GetProperty("ceilingReached").GetBoolean());
    }

    private static async Task<List<SseFrame>> PostStreamingAsync(TraconTestHost host, string agentName)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{agentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello" },
            TestContext.Current.CancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

        return await SseReader.ReadAllAsync(stream);
    }

    private static LoopSettings MarkerLoop(int maxIterations, string marker = "ALL DONE")
        => new()
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = marker }],
            MaxIterations = maxIterations,
        };

    private static FakeModelProvider Provider(params string[] responses)
    {
        var provider = new FakeModelProvider(ProviderName);
        provider.ForModel(ModelName, cfg => cfg.RespondsWith(responses));

        return provider;
    }

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider, LoopSettings? loop, bool harness = true)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Description = "Works until it is done.",
                Instructions = "Work.",
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                Harness = harness || loop is not null ? new HarnessSettings { Loop = loop } : null,
                Origin = AgentDefinitionOrigin.Code,
            });
        });

    private sealed record IterationPayload(
        int Iteration,
        bool Continued,
        string? ContinuedBy,
        string? FailedCriterion,
        bool CeilingReached);
}
#pragma warning restore MAAI001
