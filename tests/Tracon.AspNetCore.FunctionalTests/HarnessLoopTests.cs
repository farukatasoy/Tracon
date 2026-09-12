using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// 151's harness loop, exercised through a real host: the run is recorded, the
/// events go through the real <c>RunEventWriter</c>, and the streaming path is
/// the real <c>RunCoreStreamingAsync</c>.
/// </summary>
/// <remarks>
/// A unit test cannot answer any of the questions in this class. Whether the
/// iteration event reaches the run store crosses the recording boundary;
/// whether it is written in the right ambient scope on the streaming path
/// crosses the async-iterator boundary, which is where four earlier phases lost
/// exactly this kind of write.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class HarnessLoopTests
{
    private const string ProviderName = "looping";
    private const string ModelName = "loop-model";
    private const string AgentName = "looper";

    [Fact]
    public async Task A_harness_without_loop_settings_writes_no_iteration_event()
    {
        var provider = Provider("just one answer");
        await using var host = await StartAsync(provider, loop: null);

        await RunAsync(host);

        var events = await ReadRootEventsAsync(host);
        events.ShouldNotContain(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted);

        // The model answered exactly once: nothing re-invoked the agent.
        provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_marker_criterion_loops_until_the_marker_and_records_every_iteration()
    {
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "ALL DONE" }],
            MaxIterations = 6,
        };

        var provider = Provider("still working", "nearly there", "ALL DONE");
        await using var host = await StartAsync(provider, loop);

        await RunAsync(host);

        var iterations = (await ReadRootEventsAsync(host))
            .Where(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted)
            .ToList();

        iterations.Count.ShouldBe(3);
        provider.Requests.Count.ShouldBe(3);

        var first = ReadPayload(iterations[0]);
        first.Iteration.ShouldBe(1);
        first.Continued.ShouldBeTrue();
        first.ContinuedBy.ShouldBe("completionMarker");
        first.HasFeedback.ShouldBeTrue();
        first.FailedCriterion.ShouldBeNull();
        first.CeilingReached.ShouldBeFalse();

        var last = ReadPayload(iterations[2]);
        last.Iteration.ShouldBe(3);
        last.Continued.ShouldBeFalse();
        last.ContinuedBy.ShouldBeNull();
        last.FailedCriterion.ShouldBeNull();
        last.CeilingReached.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unreachable_criterion_stops_at_the_Tracon_ceiling_instead_of_running_on()
    {
        // No MaxIterations, and a marker the model never writes. Without an
        // Tracon-side ceiling this is an unbounded model bill.
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "NEVER APPEARS" }],
        };

        var provider = Provider("working");
        await using var host = await StartAsync(provider, loop);

        var response = await RunAsync(host);

        response.ShouldNotBeNull();
        provider.Requests.Count.ShouldBe(LoopSettings.DefaultMaxIterations);

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Completed);

        // The ceiling has to be visible in the RUN RECORD, not only in the
        // provider's call count: an operator asking "why did this stop?" reads
        // the events, and a criterion that never agreed must not look like one
        // that finally did.
        var iterations = (await ReadRootEventsAsync(host))
            .Where(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted)
            .ToList();

        // ONE fewer event than turns, and that is the contract, not a gap: the
        // turn that reaches the ceiling is never evaluated, so it has no
        // decision to record. The event before it says so.
        iterations.Count.ShouldBe(LoopSettings.DefaultMaxIterations - 1);

        var last = ReadPayload(iterations[^1]);
        last.Iteration.ShouldBe(LoopSettings.DefaultMaxIterations - 1);
        last.ContinuedBy.ShouldBe("completionMarker");
        last.FailedCriterion.ShouldBeNull();

        // Without this the record could not tell "the criterion was finally
        // satisfied" apart from "we simply ran out of iterations".
        last.CeilingReached.ShouldBeTrue();
        iterations.SkipLast(1).ShouldAllBe(runEvent => !ReadPayload(runEvent).CeilingReached);
    }

    [Fact]
    public async Task An_unknown_criterion_kind_is_refused_with_400_when_the_definition_is_saved()
    {
        await using var host = await StartAsync(Provider("unused"), loop: null);

        var request = new AgentDefinitionRequest
        {
            Name = "loop-reject",
            Instructions = "Work.",
            Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
            Harness = new HarnessSettings
            {
                Loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = "no-such-thing" }] },
            },
        };

        using var saved = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            request,
            TestContext.Current.CancellationToken);

        // The refusal has to happen at SAVE time. A definition that only fails
        // once a run reaches it is a loop with no stop criterion sitting in the
        // catalog, and the person who saved it is long gone by then.
        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain("no-such-thing");

        // The validate endpoint answers with a REPORT, not a status code: it
        // exists so an editor can show every finding at once.
        using var validated = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            request,
            TestContext.Current.CancellationToken);

        validated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = (await validated.Content.ReadFromJsonAsync<AgentValidationReport>(
            TestContext.Current.CancellationToken)).ShouldNotBeNull();

        report.Valid.ShouldBeFalse();
        report.Messages.ShouldContain(message => message.Message.Contains("no-such-thing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_streaming_loop_keeps_one_unbroken_span_tree()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                var provider = new FakeModelProvider(ProviderName);
                provider.ForModel(ModelName, cfg => cfg.RespondsWith("still working", "ALL DONE"));

                builder.AddModelProvider(provider);
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Works until it is done.",
                    Instructions = "Work.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    Harness = new HarnessSettings
                    {
                        Loop = new LoopSettings
                        {
                            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "ALL DONE" }],
                            MaxIterations = 6,
                        },
                    },
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Observability.SuccessSampleRatio = 1));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "go" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var runFrame = frames.Single(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal));
        var runId = JsonDocument.Parse(runFrame.Data).RootElement.GetProperty("runId").GetGuid();

        using var traceResponse = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/trace", UriKind.Relative),
            TestContext.Current.CancellationToken);

        traceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var trace = (await traceResponse.Content.ReadFromJsonAsync<RunTrace>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        // The trap this pins: an ambient write inside an async iterator does not
        // survive to the next MoveNextAsync, and a loop adds one more layer of
        // iterators between the recorder and the model. If that context were
        // lost the run's spans would land under a DIFFERENT trace and the tree
        // would come back short.
        var root = trace.Spans.SingleOrDefault(static span =>
            string.Equals(span.Name, TraconDiagnostics.RunActivityName, StringComparison.Ordinal))
            .ShouldNotBeNull();

        trace.RunId.ShouldBe(runId);
        trace.Spans.Count.ShouldBeGreaterThan(1);
        trace.Spans.Where(span => span.Id != root.Id).ShouldAllBe(span => span.ParentId != null);
    }

    [Fact]
    public async Task The_streaming_path_records_the_same_iterations_and_names_the_frame_on_the_wire()
    {
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "ALL DONE" }],
            MaxIterations = 6,
        };

        await using var host = await StartAsync(Provider("still working", "ALL DONE"), loop);

        var agent = await ResolveAsync(host);
        var session = await agent.CreateSessionAsync();

        await foreach (var _ in agent.RunStreamingAsync([new Microsoft.Extensions.AI.ChatMessage(
            Microsoft.Extensions.AI.ChatRole.User, "go")], session))
        {
            // The updates themselves are not the subject; the recorded events are.
        }

        var iterations = (await ReadRootEventsAsync(host))
            .Where(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted)
            .ToList();

        // The write happens inside an async iterator, where an ambient scope
        // set once at the top does NOT survive to the next MoveNextAsync.
        iterations.Count.ShouldBe(2);
        ReadPayload(iterations[1]).Continued.ShouldBeFalse();

        var root = await RootRunAsync(host);
        var wire = await host.Client.GetStringAsync(
            new Uri($"/tracon/api/runs/{root.Id}/events", UriKind.Relative));

        wire.ShouldContain("event: loop.iteration-completed", Case.Sensitive);
        wire.ShouldNotContain("event: unknown", Case.Sensitive);
    }

    [Fact]
    public async Task A_criterion_that_throws_stops_the_loop_and_leaves_the_run_successful()
    {
        var thrower = new DelegateLoopEvaluator(static (_, _)
            => throw new InvalidOperationException("the judge is unreachable"));

        var loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = "explodes" }], MaxIterations = 6 };

        var provider = Provider("first answer");
        await using var host = await StartAsync(
            provider,
            loop,
            configure: builder => builder.AddLoopEvaluator("explodes", thrower));

        var response = await RunAsync(host);

        // The work the agent already did is not thrown away, and the failure is
        // not silent either: the iteration event names the criterion.
        response.Text.ShouldContain("first answer");

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Completed);

        var iteration = (await ReadRootEventsAsync(host))
            .Single(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted);

        var payload = ReadPayload(iteration);
        payload.FailedCriterion.ShouldBe("explodes");
        payload.Continued.ShouldBeFalse();

        provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_iteration_event_reports_feedback_without_carrying_its_text()
    {
        const string Secret = "SECRET-FEEDBACK-TEXT";

        var evaluator = new DelegateLoopEvaluator((context, _) => new ValueTask<LoopEvaluation>(
            context.Iteration >= 2 ? LoopEvaluation.Stop() : LoopEvaluation.Continue(Secret)));

        var loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = "custom" }], MaxIterations = 6 };

        await using var host = await StartAsync(
            Provider("one", "two"),
            loop,
            configure: builder => builder.AddLoopEvaluator("custom", evaluator));

        await RunAsync(host);

        var iterations = (await ReadRootEventsAsync(host))
            .Where(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted)
            .ToList();

        iterations.Count.ShouldBe(2);
        ReadPayload(iterations[0]).HasFeedback.ShouldBeTrue();

        foreach (var iteration in iterations)
        {
            iteration.Payload?.ShouldNotContain(Secret, Case.Sensitive);
            iteration.Text?.ShouldNotContain(Secret, Case.Sensitive);
        }
    }

    [Fact]
    public async Task An_exhausted_tree_budget_stops_the_loop_rather_than_letting_it_open_another_iteration()
    {
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "NEVER APPEARS" }],
            MaxIterations = 8,
        };

        // The answer reports usage; a response with no usage would leave the
        // budget at zero forever and the test would prove nothing.
        var provider = new FakeModelProvider(ProviderName);
        provider.ForModel(ModelName, cfg => cfg.RespondsWith("working", inputTokens: 5, outputTokens: 5));

        await using var host = await StartAsync(provider, loop);

        var agent = await ResolveAsync(host);
        var session = await agent.CreateSessionAsync();

        // One token of headroom: the budget is spent by the first model call,
        // so the SECOND one is refused. The budget decorator sits INSIDE the
        // loop, which is what makes the loop stop.
        var options = new TraconRunOptions { Budget = new AgentRunBudget { MaxTotalTokens = 1 } };

        await Should.ThrowAsync<TraconRunBudgetExceededException>(async () => await agent.RunAsync(
            [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "go")],
            session,
            options));

        provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_during_the_loop_stops_it_instead_of_running_one_more_iteration()
    {
        using var cancellation = new CancellationTokenSource();

        var evaluator = new DelegateLoopEvaluator((context, _) =>
        {
            if (context.Iteration >= 2)
            {
                cancellation.Cancel();
            }

            return new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("keep going"));
        });

        var loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = "custom" }], MaxIterations = 20 };

        var provider = Provider("one");
        await using var host = await StartAsync(
            provider,
            loop,
            configure: builder => builder.AddLoopEvaluator("custom", evaluator));

        var agent = await ResolveAsync(host);
        var session = await agent.CreateSessionAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await agent.RunAsync(
            [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "go")],
            session,
            cancellationToken: cancellation.Token));

        // Two model calls, not twenty: the loop honored the cancellation
        // between iterations rather than draining its ceiling.
        provider.Requests.Count.ShouldBe(2);
    }

    private static async Task<Microsoft.Agents.AI.AgentResponse> RunAsync(TraconTestHost host)
    {
        var agent = await ResolveAsync(host);
        var session = await agent.CreateSessionAsync();

        return await agent
            .RunAsync([new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "go")], session)
            .WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task<AIAgent> ResolveAsync(TraconTestHost host)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        return (await catalog.ResolveAsync(AgentName, culture: null, CancellationToken.None)).ShouldNotBeNull();
    }

    private static async Task<RunRecord> RootRunAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        return (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
    }

    private static async Task<List<RunEvent>> ReadRootEventsAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = await RootRunAsync(host);
        var events = new List<RunEvent>();

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            events.Add(runEvent);
        }

        return events;
    }


    private static IterationPayload ReadPayload(RunEvent runEvent)
    {
        var payload = runEvent.Payload.ShouldNotBeNull();
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        return new IterationPayload(
            root.GetProperty("iteration").GetInt32(),
            root.GetProperty("continued").GetBoolean(),
            root.GetProperty("continuedBy").ValueKind == JsonValueKind.Null
                ? null
                : root.GetProperty("continuedBy").GetString(),
            root.GetProperty("hasFeedback").GetBoolean(),
            root.GetProperty("failedCriterion").ValueKind == JsonValueKind.Null
                ? null
                : root.GetProperty("failedCriterion").GetString(),
            root.GetProperty("ceilingReached").GetBoolean());
    }

    private static FakeModelProvider Provider(params string[] responses)
    {
        var provider = new FakeModelProvider(ProviderName);
        provider.ForModel(ModelName, cfg => cfg.RespondsWith(responses));

        return provider;
    }

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        LoopSettings? loop,
        Action<ITraconBuilder>? configure = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);
                configure?.Invoke(builder);

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

    private sealed record IterationPayload(
        int Iteration,
        bool Continued,
        string? ContinuedBy,
        bool HasFeedback,
        string? FailedCriterion,
        bool CeilingReached);
}
#pragma warning restore MAAI001
