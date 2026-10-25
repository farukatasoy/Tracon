using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// What <see cref="RecordingLoopEvaluator"/> writes about WHO decided (Phase 242): every evaluated criterion
/// with its decision, the model an <c>aiJudge</c> really called and the agent and run of an
/// <c>agentReview</c>, and nothing the model was told.
/// </summary>
/// <remarks>
/// The evaluator is driven directly, with the criteria as delegates; the real judge client, the real reviewer
/// and the stream are pinned by <c>AgentReviewLoopTests</c> and <c>HarnessLoopTests</c>.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class RecordingLoopEvaluatorCriteriaTests
{
    [Fact]
    public async Task Every_criterion_up_to_the_one_that_continues_is_listed_with_its_decision()
    {
        await using var rig = await Rig.StartAsync(
            Stop("completionMarker"),
            Stop("todoCompletion"),
            Continue("agentReview"),
            Stop("never-reached"));

        await rig.EvaluateAsync();

        var criteria = (await rig.PayloadAsync()).GetProperty("criteria").EnumerateArray().ToList();

        // First-continue short circuit (K-704): the criterion after the deciding one is not evaluated and not listed.
        criteria.Select(static entry => (entry.GetProperty("kind").GetString(), entry.GetProperty("decision").GetString()))
            .ShouldBe([("completionMarker", "stop"), ("todoCompletion", "stop"), ("agentReview", "continue")]);
    }

    [Fact]
    public async Task An_iteration_every_criterion_is_satisfied_lists_them_all_as_stop()
    {
        await using var rig = await Rig.StartAsync(Stop("completionMarker"), Stop("agentReview"));

        var evaluation = await rig.EvaluateAsync();

        evaluation.ShouldReinvoke.ShouldBeFalse();

        var payload = await rig.PayloadAsync();

        payload.GetProperty("continued").GetBoolean().ShouldBeFalse();
        payload.GetProperty("criteria").EnumerateArray().Select(static entry => entry.GetProperty("decision").GetString())
            .ShouldBe(["stop", "stop"]);
    }

    [Fact]
    public async Task A_criterion_that_throws_is_listed_as_failed_and_ends_the_list()
    {
        await using var rig = await Rig.StartAsync(
            Stop("completionMarker"),
            new LoopCriterionBinding("agentReview", new DelegateLoopEvaluator((_, _) => throw new InvalidOperationException("the reviewer is down"))),
            Stop("never-reached"));

        await rig.EvaluateAsync();

        var payload = await rig.PayloadAsync();

        payload.GetProperty("failedCriterion").GetString().ShouldBe("agentReview");
        payload.GetProperty("criteria").EnumerateArray().Select(static entry => (entry.GetProperty("kind").GetString(), entry.GetProperty("decision").GetString()))
            .ShouldBe([("completionMarker", "stop"), ("agentReview", "failed")]);
    }

    [Fact]
    public async Task A_criterion_that_names_no_judge_and_no_reviewer_writes_neither_key()
    {
        await using var rig = await Rig.StartAsync(Continue("completionMarker"));

        await rig.EvaluateAsync();

        var entry = (await rig.PayloadAsync()).GetProperty("criteria")[0];

        entry.TryGetProperty("judge", out _).ShouldBeFalse();
        entry.TryGetProperty("reviewer", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task The_judge_and_the_reviewer_a_criterion_hands_back_are_written_on_its_own_entry()
    {
        var runId = Guid.NewGuid();

        await using var rig = await Rig.StartAsync(
            new LoopCriterionBinding("aiJudge", new DelegateLoopEvaluator((_, _) =>
            {
                TraconRunContext.Current!.CriterionTrail!.RecordJudge("judging-fallback", "judge-fallback-model");

                return new ValueTask<LoopEvaluation>(LoopEvaluation.Stop());
            })),
            new LoopCriterionBinding("agentReview", new DelegateLoopEvaluator((_, _) =>
            {
                TraconRunContext.Current!.CriterionTrail!.RecordReviewer(new LoopReviewerTrail("draft-reviewer", 3, runId));

                return new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("SECRET-FEEDBACK"));
            })));

        await rig.EvaluateAsync();

        var criteria = (await rig.PayloadAsync()).GetProperty("criteria");

        var judge = criteria[0].GetProperty("judge");
        judge.GetProperty("provider").GetString().ShouldBe("judging-fallback");
        judge.GetProperty("model").GetString().ShouldBe("judge-fallback-model");
        criteria[0].TryGetProperty("reviewer", out _).ShouldBeFalse();

        var reviewer = criteria[1].GetProperty("reviewer");
        reviewer.GetProperty("agentName").GetString().ShouldBe("draft-reviewer");
        reviewer.GetProperty("agentVersion").GetInt32().ShouldBe(3);
        reviewer.GetProperty("runId").GetGuid().ShouldBe(runId);
        criteria[1].TryGetProperty("judge", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_reviewer_without_a_version_or_run_writes_them_as_null()
    {
        await using var rig = await Rig.StartAsync(
            new LoopCriterionBinding("agentReview", new DelegateLoopEvaluator((_, _) =>
            {
                TraconRunContext.Current!.CriterionTrail!.RecordReviewer(new LoopReviewerTrail("draft-reviewer", AgentVersion: null, RunId: null));

                return new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("again"));
            })));

        await rig.EvaluateAsync();

        var reviewer = (await rig.PayloadAsync()).GetProperty("criteria")[0].GetProperty("reviewer");

        reviewer.GetProperty("agentVersion").ValueKind.ShouldBe(JsonValueKind.Null);
        reviewer.GetProperty("runId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_criterion_does_not_see_the_trail_of_the_one_before()
    {
        await using var rig = await Rig.StartAsync(
            new LoopCriterionBinding("aiJudge", new DelegateLoopEvaluator((_, _) =>
            {
                TraconRunContext.Current!.CriterionTrail!.RecordJudge("judging", "judge-model");

                return new ValueTask<LoopEvaluation>(LoopEvaluation.Stop());
            })),
            new LoopCriterionBinding("custom", new DelegateLoopEvaluator((_, _) =>
            {
                // A fresh holder for each criterion: nothing the judge wrote is here.
                TraconRunContext.Current!.CriterionTrail!.Judge.ShouldBeNull();

                return new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("more"));
            })));

        await rig.EvaluateAsync();

        (await rig.PayloadAsync()).GetProperty("criteria")[1].TryGetProperty("judge", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task The_run_scope_is_the_runs_own_again_after_the_criteria_have_run()
    {
        await using var rig = await Rig.StartAsync(Continue("completionMarker"));

        await rig.EvaluateAsync();

        // What the evaluator does after the criteria (the usage sum, the stream's frame) reads the run's scope.
        rig.ScopeAfterCriteria.ShouldBeSameAs(rig.Scope);
        rig.ScopeAfterCriteria!.CriterionTrail.ShouldBeNull();
    }

    [Fact]
    public async Task The_feedback_text_is_in_neither_the_payload_nor_the_event_text()
    {
        await using var rig = await Rig.StartAsync(
            new LoopCriterionBinding("agentReview", new DelegateLoopEvaluator((_, _) =>
            {
                TraconRunContext.Current!.CriterionTrail!.RecordReviewer(new LoopReviewerTrail("draft-reviewer", 2, Guid.NewGuid()));

                return new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("SECRET-FEEDBACK-TEXT"));
            })));

        var evaluation = await rig.EvaluateAsync();

        evaluation.Feedback.ShouldBe("SECRET-FEEDBACK-TEXT");

        var stored = (await rig.StoredEventsAsync()).ShouldHaveSingleItem();

        stored.Payload.ShouldNotBeNull().ShouldNotContain("SECRET-FEEDBACK-TEXT", Case.Sensitive);
        stored.Text.ShouldNotBeNull().ShouldNotContain("SECRET-FEEDBACK-TEXT", Case.Sensitive);
    }

    [Fact]
    public async Task The_event_text_names_the_reviewer_that_decided()
    {
        await using var continuing = await Rig.StartAsync(Reviews(approved: false, "draft-reviewer", version: 3));
        await continuing.EvaluateAsync();

        (await continuing.StoredEventsAsync()).ShouldHaveSingleItem().Text
            .ShouldBe("iteration 1 continued by 'agentReview' (reviewer 'draft-reviewer' v3)");

        await using var approving = await Rig.StartAsync(Reviews(approved: true, "draft-reviewer", version: 3));
        await approving.EvaluateAsync();

        (await approving.StoredEventsAsync()).ShouldHaveSingleItem().Text
            .ShouldBe("iteration 1 stopped (approved by reviewer 'draft-reviewer' v3)");

        await using var unversioned = await Rig.StartAsync(Reviews(approved: false, "draft-reviewer", version: null));
        await unversioned.EvaluateAsync();

        (await unversioned.StoredEventsAsync()).ShouldHaveSingleItem().Text
            .ShouldBe("iteration 1 continued by 'agentReview' (reviewer 'draft-reviewer')");
    }

    [Fact]
    public async Task The_event_text_of_a_loop_with_no_reviewer_is_what_it_was()
    {
        await using var rig = await Rig.StartAsync(Continue("completionMarker"));
        await rig.EvaluateAsync();

        (await rig.StoredEventsAsync()).ShouldHaveSingleItem().Text.ShouldBe("iteration 1 continued by 'completionMarker'");
    }

    [Fact]
    public async Task One_composite_instance_serves_two_runs_at_once_without_mixing_their_trails()
    {
        // ONE evaluator, as a compiled agent holds it for every run. The first run is held inside its criterion while
        // the second runs to its end; a trail kept in a field of the evaluator would show in the wrong event.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var calls = 0;

        var shared = Rig.NewEvaluator(
            new LoopCriterionBinding("agentReview", new DelegateLoopEvaluator(async (_, token) =>
            {
                var mine = Interlocked.Increment(ref calls) - 1;

                if (mine == 0)
                {
                    started.SetResult();
                    await release.Task.WaitAsync(token);
                }

                TraconRunContext.Current!.CriterionTrail!.RecordReviewer(new LoopReviewerTrail($"reviewer-{mine}", mine + 1, runIds[mine]));

                return LoopEvaluation.Continue("again");
            })));

        await using var first = await Rig.StartAsync(shared);
        await using var second = await Rig.StartAsync(shared);

        var firstEvaluation = Task.Run(() => first.EvaluateAsync().AsTask());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Run(() => second.EvaluateAsync().AsTask());

        release.SetResult();
        await firstEvaluation;

        var secondReviewer = (await second.PayloadAsync()).GetProperty("criteria")[0].GetProperty("reviewer");
        secondReviewer.GetProperty("agentName").GetString().ShouldBe("reviewer-1");
        secondReviewer.GetProperty("runId").GetGuid().ShouldBe(runIds[1]);

        var firstReviewer = (await first.PayloadAsync()).GetProperty("criteria")[0].GetProperty("reviewer");
        firstReviewer.GetProperty("agentName").GetString().ShouldBe("reviewer-0");
        firstReviewer.GetProperty("runId").GetGuid().ShouldBe(runIds[0]);
    }

    private static LoopCriterionBinding Stop(string kind)
        => new(kind, new DelegateLoopEvaluator((_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop())));

    private static LoopCriterionBinding Continue(string kind)
        => new(kind, new DelegateLoopEvaluator((_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Continue("more"))));

    private static LoopCriterionBinding Reviews(bool approved, string reviewer, int? version)
        => new("agentReview", new DelegateLoopEvaluator((_, _) =>
        {
            TraconRunContext.Current!.CriterionTrail!.RecordReviewer(new LoopReviewerTrail(reviewer, version, Guid.NewGuid()));

            return new ValueTask<LoopEvaluation>(approved ? LoopEvaluation.Stop() : LoopEvaluation.Continue("again"));
        }));

    private sealed class Rig : IAsyncDisposable
    {
        private readonly InMemoryRunStore _store;
        private readonly RecordingLoopEvaluator _evaluator;
        private readonly RunEventWriter _writer;
        private readonly LoopContext _context = new(
            new NoopAgent(),
            new NoopSession(),
            [new ChatMessage(ChatRole.User, "go")],
            new AgentResponse(new ChatMessage(ChatRole.Assistant, "a draft")),
            new AgentRunOptions());

        private Rig(InMemoryRunStore store, RunEventWriter writer, RecordingLoopEvaluator evaluator, AgentRunScope scope)
        {
            _store = store;
            _writer = writer;
            _evaluator = evaluator;
            Scope = scope;
        }

        public AgentRunScope Scope { get; }

        public AgentRunScope? ScopeAfterCriteria { get; set; }

        public static RecordingLoopEvaluator NewEvaluator(params LoopCriterionBinding[] criteria)
            => new(
                "looper",
                criteria,
                maxIterations: 50,
                LoopOutcome.ReturnLastResponse,
                LoopOutcome.ReturnLastResponse,
                NullLogger.Instance);

        public static Task<Rig> StartAsync(params LoopCriterionBinding[] criteria) => StartAsync(NewEvaluator(criteria));

        public static async Task<Rig> StartAsync(RecordingLoopEvaluator evaluator)
        {
            var store = new InMemoryRunStore();
            var writer = new RunEventWriter(
                store,
                new TraconRunRecordingOptions { RecordToolPayloads = true },
                NullLogger.Instance,
                TraconId.NewId(),
                metrics: null);

            await writer.StartAsync(
                new RunStartInfo { RunId = writer.RunId, AgentName = "looper", StartedAt = DateTimeOffset.UtcNow },
                query: "go");

            Rig? created = null;

            var scope = new AgentRunScope
            {
                RunId = writer.RunId,
                RootRunId = writer.RunId,
                Writer = writer,
                ExtraUsage = new SideChannelUsageAccumulator(),

                // Runs after the criteria, in the evaluator's own flow: it shows which scope the evaluator left in place.
                AfterLoopIterationRecorded = (_, _) =>
                {
                    created!.ScopeAfterCriteria = TraconRunContext.Current;

                    return ValueTask.CompletedTask;
                },
            };

            var rig = new Rig(store, writer, evaluator, scope);
            created = rig;

            // The framework's loop numbers its iterations; a test has no loop to do it.
            typeof(LoopContext).GetProperty(nameof(LoopContext.Iteration))!.GetSetMethod(nonPublic: true)!.Invoke(rig._context, [1]);

            return rig;
        }

        public async ValueTask<LoopEvaluation> EvaluateAsync()
        {
            // Set here, in the body that awaits the evaluator: a scope set in StartAsync would not flow back.
            TraconRunContext.SetCurrent(Scope);

            return await _evaluator.EvaluateAsync(_context);
        }

        public async Task<JsonElement> PayloadAsync()
        {
            var stored = (await StoredEventsAsync()).ShouldHaveSingleItem();

            using var document = JsonDocument.Parse(stored.Payload.ShouldNotBeNull());

            return document.RootElement.Clone();
        }

        public async Task<List<RunEvent>> StoredEventsAsync()
        {
            var events = new List<RunEvent>();

            await foreach (var runEvent in _store.ReadEventsAsync(_writer.RunId))
            {
                if (runEvent.Type == RunEventType.LoopIterationCompleted)
                {
                    events.Add(runEvent);
                }
            }

            return events;
        }

        public ValueTask DisposeAsync()
        {
            TraconRunContext.SetCurrent(null);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopSession : AgentSession;

    private sealed class NoopAgent : AIAgent
    {
        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
#pragma warning restore MAAI001
