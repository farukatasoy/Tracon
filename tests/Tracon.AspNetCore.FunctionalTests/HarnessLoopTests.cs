using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
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
        // No MaxIterations, and a marker the model never writes. Without a
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

    // ---- Phase 192: how the loop ends when no criterion is satisfied ------------

    /// <summary>
    /// FailRun on exhaustion: the run fails with LoopUnsatisfied after exactly
    /// MaxIterations turns. The framework is given one iteration more so the turn at
    /// the ceiling is evaluated; it never runs that extra turn.
    /// </summary>
    [Fact]
    public async Task FailRun_on_exhaustion_fails_the_run_after_exactly_MaxIterations_turns()
    {
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "NEVER APPEARS" }],
            MaxIterations = 3,
            OnExhausted = LoopOutcome.FailRun,
        };

        // Every turn reports usage: a failed run must still carry what the loop spent.
        var provider = ProviderWithUsage("working", turns: 3);
        await using var host = await StartAsync(provider, loop);

        var exception = await Should.ThrowAsync<TraconLoopUnsatisfiedException>(() => RunAsync(host));
        exception.AgentName.ShouldBe(AgentName);
        exception.Iterations.ShouldBe(3);
        exception.UnsatisfiedCriterion.ShouldBe("completionMarker");
        exception.Message.ShouldNotContain("working", Case.Sensitive);

        provider.Requests.Count.ShouldBe(3, "the framework's extra iteration never runs");

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Failed);
        root.Error.ShouldNotBeNull().Type.ShouldBe(TraconLoopUnsatisfiedException.LoopUnsatisfiedErrorType);
        root.Error.Class.ShouldBe(RunErrorClass.LoopUnsatisfied);

        // Phase 192 audit: the run's usage is what the three turns spent, not null.
        // A run that ends in an exception has scope.ExtraUsage as its only usage source.
        root.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(3 * UsagePerTurn);

        // In this mode the turn at the ceiling is evaluated: as many events as turns.
        var iterations = (await ReadRootEventsAsync(host))
            .Where(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted)
            .ToList();

        iterations.Count.ShouldBe(3);
        iterations.SkipLast(1).ShouldAllBe(runEvent => !ReadPayload(runEvent).CeilingReached && !ReadFailsRun(runEvent));

        var last = ReadPayload(iterations[^1]);
        last.Iteration.ShouldBe(3);
        last.ContinuedBy.ShouldBe("completionMarker");
        last.CeilingReached.ShouldBeTrue();
        ReadFailsRun(iterations[^1]).ShouldBeTrue();
        iterations[^1].Text.ShouldNotBeNull().ShouldContain("fails the run");
    }

    [Fact]
    public async Task FailRun_on_exhaustion_completes_a_run_whose_last_turn_meets_the_criterion()
    {
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "ALL DONE" }],
            MaxIterations = 3,
            OnExhausted = LoopOutcome.FailRun,
        };

        var provider = Provider("one", "two", "ALL DONE");
        await using var host = await StartAsync(provider, loop);

        var response = await RunAsync(host);

        response.Text.ShouldContain("ALL DONE");
        provider.Requests.Count.ShouldBe(3);

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Completed);

        var last = (await ReadRootEventsAsync(host))
            .Last(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted);
        ReadPayload(last).Continued.ShouldBeFalse();
        ReadFailsRun(last).ShouldBeFalse();
    }

    /// <summary>
    /// MaxIterations 1 with FailRun: the single turn is evaluated (by default it is not).
    /// </summary>
    [Fact]
    public async Task FailRun_with_a_single_iteration_evaluates_that_iteration()
    {
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "NEVER APPEARS" }],
            MaxIterations = 1,
            OnExhausted = LoopOutcome.FailRun,
        };

        var provider = Provider("working");
        await using var host = await StartAsync(provider, loop);

        await Should.ThrowAsync<TraconLoopUnsatisfiedException>(() => RunAsync(host));

        provider.Requests.Count.ShouldBe(1);
        (await ReadRootEventsAsync(host)).Count(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted)
            .ShouldBe(1);
    }

    /// <summary>
    /// FailRun on a criterion error: the run fails with the criterion's OWN error, so
    /// an unreachable judge provider stays a provider error, not a rejected response.
    /// </summary>
    [Fact]
    public async Task FailRun_on_criterion_error_fails_the_run_with_the_criterions_own_class()
    {
        var thrower = new DelegateLoopEvaluator(static (_, _)
            => throw new TraconProviderUnavailableException("the judge provider is unreachable"));

        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "explodes" }],
            MaxIterations = 6,
            OnCriterionError = LoopOutcome.FailRun,
        };

        var provider = ProviderWithUsage("first answer", turns: 1);
        await using var host = await StartAsync(
            provider,
            loop,
            configure: builder => builder.AddLoopEvaluator("explodes", thrower));

        await Should.ThrowAsync<TraconProviderUnavailableException>(() => RunAsync(host));

        provider.Requests.Count.ShouldBe(1);

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Failed);
        root.Error.ShouldNotBeNull().Class.ShouldBe(RunErrorClass.ProviderUnavailable);
        root.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(UsagePerTurn);

        var iteration = (await ReadRootEventsAsync(host))
            .Single(static runEvent => runEvent.Type == RunEventType.LoopIterationCompleted);
        ReadPayload(iteration).FailedCriterion.ShouldBe("explodes");
        ReadFailsRun(iteration).ShouldBeTrue();
    }

    /// <summary>The run's own cancellation is never a criterion error, in FailRun mode too.</summary>
    [Fact]
    public async Task Cancellation_during_a_FailRun_loop_ends_the_run_as_cancelled()
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

        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "custom" }],
            MaxIterations = 3,
            OnExhausted = LoopOutcome.FailRun,
            OnCriterionError = LoopOutcome.FailRun,
        };

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

        provider.Requests.Count.ShouldBe(2);
        (await RootRunAsync(host)).Status.ShouldBe(RunStatus.Canceled);
    }

    /// <summary>
    /// Streaming: the last turn's text reaches the caller, then the failure, and the
    /// run closes as failed.
    /// </summary>
    [Fact]
    public async Task FailRun_on_exhaustion_streams_the_last_turn_and_then_fails_the_run()
    {
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "NEVER APPEARS" }],
            MaxIterations = 2,
            OnExhausted = LoopOutcome.FailRun,
        };

        await using var host = await StartAsync(ProviderWithUsage("streamed draft", turns: 2), loop);

        var agent = await ResolveAsync(host);
        var session = await agent.CreateSessionAsync();
        var text = new System.Text.StringBuilder();

        await Should.ThrowAsync<TraconLoopUnsatisfiedException>(async () =>
        {
            await foreach (var update in agent.RunStreamingAsync([new Microsoft.Extensions.AI.ChatMessage(
                Microsoft.Extensions.AI.ChatRole.User, "go")], session))
            {
                text.Append(update.Text);
            }
        });

        text.ToString().ShouldContain("streamed draft");

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Failed);
        root.Error.ShouldNotBeNull().Class.ShouldBe(RunErrorClass.LoopUnsatisfied);
        root.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(2 * UsagePerTurn);
    }

    /// <summary>
    /// The reason this phase exists: in a sequential workflow, an agent whose loop fails
    /// on exhaustion stops the workflow, and the next node (a TTS step in the podcast
    /// pipeline) never receives the response no criterion accepted.
    /// </summary>
    [Fact]
    public async Task A_workflow_stops_at_an_agent_whose_loop_fails_on_exhaustion()
    {
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "NEVER APPEARS" }],
            MaxIterations = 2,
            OnExhausted = LoopOutcome.FailRun,
        };

        var spoken = 0;
        var provider = Provider("draft");
        await using var host = await StartAsync(
            provider,
            loop,
            configure: builder => builder
                .UseWorkflows()
                .AddWorkflowFunction<List<Microsoft.Extensions.AI.ChatMessage>, List<Microsoft.Extensions.AI.ChatMessage>>(
                    "speak",
                    _ => (messages, _, _) =>
                    {
                        Interlocked.Increment(ref spoken);

                        return new ValueTask<List<Microsoft.Extensions.AI.ChatMessage>>(messages);
                    }));

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/workflows/podcast",
                   new WorkflowSaveRequest
                   {
                       Kind = WorkflowKind.Sequential,
                       Nodes =
                       [
                           new WorkflowNodeReference { Name = AgentName, Kind = WorkflowNodeKind.Agent },
                           new WorkflowNodeReference { Name = "speak", Kind = WorkflowNodeKind.Function },
                       ],
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using (var response = await host.Client.PostAsJsonAsync(
                   "/tracon/api/workflows/podcast/run",
                   new WorkflowRunHttpRequest { Message = "write the episode" }))
        {
            _ = await response.Content.ReadAsStringAsync();
        }

        spoken.ShouldBe(0, "the function node after the failed agent never runs");
        provider.Requests.Count.ShouldBe(2);

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { Take = 10, OnlyRootRuns = false });
        var workflow = runs.Single(static run => run.Kind == RunKind.Workflow);
        var agentRun = runs.Single(static run => run.Kind == RunKind.Agent);

        agentRun.Status.ShouldBe(RunStatus.Failed);
        agentRun.ParentRunId.ShouldBe(workflow.Id);
        agentRun.Error.ShouldNotBeNull().Class.ShouldBe(RunErrorClass.LoopUnsatisfied);
        workflow.Status.ShouldBe(RunStatus.Failed);

        // Measured (plan 192.5, open question 1): MAF does not wrap the exception - the
        // workflow row names TraconLoopUnsatisfiedException - but WorkflowRunner writes the
        // CLR type name and never classifies, for every workflow failure. The participant
        // run carries the class; classifying the workflow row is candidate F-314.
        workflow.Error.ShouldNotBeNull().Type.ShouldBe(typeof(TraconLoopUnsatisfiedException).FullName);
        workflow.Error.Class.ShouldBeNull();
    }

    /// <summary>A name that is not a LoopOutcome member is refused when the definition is saved.</summary>
    [Fact]
    public async Task An_unknown_loop_outcome_name_is_refused_with_400()
    {
        await using var host = await StartAsync(Provider("x"), loop: null);

        using var response = await host.Client.PutAsync(
            new Uri("/tracon/api/agents/outcome-agent", UriKind.Relative),
            new StringContent(
                """
                {"name":"outcome-agent","instructions":"Work.","model":{"provider":"looping","model":"loop-model"},
                 "harness":{"loop":{"criteria":[{"kind":"completionMarker","marker":"DONE"}],"onExhausted":"Explode"}}}
                """,
                System.Text.Encoding.UTF8,
                "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The policy is part of the definition, so a saved change reaches the next run of
    /// a cached agent: the first run returns, the second, after the save, fails.
    /// </summary>
    [Fact]
    public async Task A_saved_policy_change_reaches_the_next_run_of_a_cached_agent()
    {
        var provider = Provider("working");
        await using var host = await StartAsync(provider, loop: null);

        await SaveLoopAgentAsync(host, "policy-agent", "ReturnLastResponse", create: true);
        (await RunByHttpAsync(host, "policy-agent")).ShouldBe("Completed");

        await SaveLoopAgentAsync(host, "policy-agent", "FailRun", create: false);
        (await RunByHttpAsync(host, "policy-agent")).ShouldBe("Failed");
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

    private static bool ReadFailsRun(RunEvent runEvent)
    {
        using var document = JsonDocument.Parse(runEvent.Payload.ShouldNotBeNull());

        return document.RootElement.GetProperty("failsRun").GetBoolean();
    }

    private static async Task SaveLoopAgentAsync(TraconTestHost host, string name, string onExhausted, bool create)
    {
        var body = new
        {
            name,
            instructions = "Work.",
            model = new { provider = ProviderName, model = ModelName },
            harness = new
            {
                loop = new
                {
                    criteria = new[] { new { kind = "completionMarker", marker = "NEVER APPEARS" } },
                    maxIterations = 2,
                    onExhausted,
                },
            },
        };

        using var saved = create
            ? await host.Client.PostAsJsonAsync(new Uri("/tracon/api/agents", UriKind.Relative), body)
            : await host.Client.PutAsJsonAsync(new Uri($"/tracon/api/agents/{name}", UriKind.Relative), body);

        saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());
    }

    /// <summary>Runs a definition over HTTP in a new session and returns the run's status.</summary>
    private static async Task<string> RunByHttpAsync(TraconTestHost host, string name)
    {
        var session = Guid.NewGuid().ToString("N");

        using (var response = await host.Client.PostAsJsonAsync(
                   new Uri($"/tracon/api/agents/{name}/run", UriKind.Relative),
                   new AgentRunRequest { Message = "go", SessionId = session }))
        {
            _ = await response.Content.ReadAsStringAsync();
        }

        using var runs = await host.Client.GetAsync(new Uri($"/tracon/api/runs?sessionId={session}", UriKind.Relative));
        var run = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray().ShouldHaveSingleItem();

        return run.GetProperty("status").GetString().ShouldNotBeNull();
    }

    /// <summary>Tokens one turn of <see cref="ProviderWithUsage"/> reports (input + output).</summary>
    private const int UsagePerTurn = 10;

    /// <summary>A provider whose first <paramref name="turns"/> responses each report <see cref="UsagePerTurn"/> tokens.</summary>
    private static FakeModelProvider ProviderWithUsage(string response, int turns)
    {
        var provider = new FakeModelProvider(ProviderName);
        provider.ForModel(ModelName, cfg =>
        {
            for (var turn = 0; turn < turns; turn++)
            {
                cfg.RespondsWith(response, inputTokens: 6, outputTokens: 4);
            }
        });

        return provider;
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
