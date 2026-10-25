using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The <c>agentReview</c> criterion's own rules (Phase 242): how it reads the reviewer's verdict, what it asks
/// the reviewer, and what it hands the audit trail. The gates of the child run (tenant, depth, budget,
/// approval) belong to <c>ChildAgentInvoker</c> and are pinned by <c>AgentReviewLoopTests</c>.
/// </summary>
// MAAI001: the loop API is marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class AgentReviewLoopEvaluatorTests
{
    private const string Reviewer = "draft-reviewer";

    [Fact]
    public async Task An_approving_verdict_stops_the_loop()
    {
        var reviewer = new ScriptedReviewer("{\"approved\":true}");

        var evaluation = await new AgentReviewLoopEvaluator(reviewer, Reviewer).EvaluateAsync(Context("a draft"));

        evaluation.ShouldReinvoke.ShouldBeFalse();
    }

    [Fact]
    public async Task A_declining_verdict_continues_with_the_reviewers_feedback()
    {
        var reviewer = new ScriptedReviewer("{\"approved\":false,\"feedback\":\"Add a heading.\"}");

        var evaluation = await new AgentReviewLoopEvaluator(reviewer, Reviewer).EvaluateAsync(Context("a draft"));

        evaluation.ShouldReinvoke.ShouldBeTrue();
        evaluation.Feedback.ShouldBe("Add a heading.");
    }

    [Theory]
    [InlineData("{\"approved\":false}")]
    [InlineData("{\"approved\":false,\"feedback\":null}")]
    [InlineData("{\"approved\":false,\"feedback\":\"   \"}")]
    public async Task A_declining_verdict_without_feedback_sends_the_fixed_sentence(string answer)
    {
        var evaluation = await new AgentReviewLoopEvaluator(new ScriptedReviewer(answer), Reviewer).EvaluateAsync(Context("a draft"));

        evaluation.ShouldReinvoke.ShouldBeTrue();
        evaluation.Feedback.ShouldBe(AgentReviewLoopEvaluator.NoFeedback);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("approved")]
    [InlineData("```json\n{\"approved\":true}\n```")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("{\"approved\":\"true\"}")]
    [InlineData("{\"approved\":1}")]
    [InlineData("{\"approved\":null}")]
    [InlineData("{\"approved\":true,\"feedback\":3}")]
    [InlineData("{\"approved\":true,\"feedback\":[\"a\"]}")]
    public async Task A_verdict_that_cannot_be_read_is_a_failure_and_never_an_approval(string answer)
    {
        var evaluator = new AgentReviewLoopEvaluator(new ScriptedReviewer(answer), Reviewer);

        var failure = await Should.ThrowAsync<TraconException>(async () => await evaluator.EvaluateAsync(Context("a draft")));

        failure.Message.ShouldContain($"'{Reviewer}'", Case.Sensitive);
        failure.Message.ShouldContain("did not give a readable verdict", Case.Sensitive);
    }

    [Fact]
    public async Task The_failure_names_the_defect_and_does_not_repeat_the_answer()
    {
        var evaluator = new AgentReviewLoopEvaluator(new ScriptedReviewer("SECRET-ANSWER-TEXT is not json"), Reviewer);

        var failure = await Should.ThrowAsync<TraconException>(async () => await evaluator.EvaluateAsync(Context("a draft")));

        failure.Message.ShouldNotContain("SECRET-ANSWER-TEXT", Case.Sensitive);
    }

    [Fact]
    public async Task The_reviewer_reads_the_last_assistant_message_of_the_turn_as_the_draft()
    {
        var reviewer = new ScriptedReviewer("{\"approved\":true}");
        var response = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant, "A PLAN, NOT THE DRAFT"),
            new ChatMessage(ChatRole.Assistant, "THE DRAFT"),
        ]);

        await new AgentReviewLoopEvaluator(reviewer, Reviewer).EvaluateAsync(Context(response, iteration: 4, request: "Write it."));

        var prompt = reviewer.Prompts.ShouldHaveSingleItem();

        prompt.ShouldContain("THE DRAFT", Case.Sensitive);
        prompt.ShouldNotContain("A PLAN, NOT THE DRAFT", Case.Sensitive);
        prompt.ShouldContain("Write it.", Case.Sensitive);
        prompt.ShouldContain("Iteration: 4", Case.Sensitive);
    }

    [Fact]
    public async Task The_request_is_the_last_user_message_the_loop_started_with()
    {
        var reviewer = new ScriptedReviewer("{\"approved\":true}");
        var context = new LoopContext(
            new NoopAgent(),
            new NoopSession(),
            [
                new ChatMessage(ChatRole.User, "an earlier question"),
                new ChatMessage(ChatRole.Assistant, "an earlier answer"),
                new ChatMessage(ChatRole.User, "the current request"),
            ],
            new AgentResponse(new ChatMessage(ChatRole.Assistant, "draft")),
            new AgentRunOptions());

        await new AgentReviewLoopEvaluator(reviewer, Reviewer).EvaluateAsync(context);

        var prompt = reviewer.Prompts.ShouldHaveSingleItem();

        prompt.ShouldContain("the current request", Case.Sensitive);
        prompt.ShouldNotContain("an earlier question", Case.Sensitive);
    }

    [Fact]
    public async Task An_empty_draft_still_goes_to_the_reviewer_who_decides()
    {
        var reviewer = new ScriptedReviewer("{\"approved\":false,\"feedback\":\"Write something.\"}");
        var context = new LoopContext(
            new NoopAgent(),
            new NoopSession(),
            [new ChatMessage(ChatRole.User, "go")],
            new AgentResponse([]),
            new AgentRunOptions());

        var evaluation = await new AgentReviewLoopEvaluator(reviewer, Reviewer).EvaluateAsync(context);

        reviewer.Prompts.Count.ShouldBe(1);
        evaluation.ShouldReinvoke.ShouldBeTrue();
    }

    [Fact]
    public void The_prompt_carries_the_request_the_iteration_and_the_draft_and_no_instruction()
    {
        AgentReviewLoopEvaluator.BuildPrompt("Write a poem.", 2, "Roses are red")
            .ShouldBe("Request:\nWrite a poem.\n\nIteration: 2\n\nDraft:\nRoses are red");
    }

    [Fact]
    public void A_feedback_over_the_limit_is_cut_and_says_so()
    {
        var feedback = AgentReviewLoopEvaluator.FeedbackFor(new string('x', AgentReviewLoopEvaluator.FeedbackLimit + 500));

        feedback.ShouldEndWith(AgentReviewLoopEvaluator.TruncationNote);
        feedback.Length.ShouldBe(AgentReviewLoopEvaluator.FeedbackLimit + AgentReviewLoopEvaluator.TruncationNote.Length);
    }

    [Fact]
    public void A_feedback_at_the_limit_is_kept_whole()
    {
        var feedback = new string('x', AgentReviewLoopEvaluator.FeedbackLimit);

        AgentReviewLoopEvaluator.FeedbackFor(feedback).ShouldBe(feedback);
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        // The limit falls between the two halves of the emoji.
        var feedback = new string('x', AgentReviewLoopEvaluator.FeedbackLimit - 1) + "😀" + "tail";

        var cut = AgentReviewLoopEvaluator.FeedbackFor(feedback);

        cut.ShouldEndWith(AgentReviewLoopEvaluator.TruncationNote);

        var kept = cut[..^AgentReviewLoopEvaluator.TruncationNote.Length];

        kept.Length.ShouldBe(AgentReviewLoopEvaluator.FeedbackLimit - 1);
        char.IsHighSurrogate(kept[^1]).ShouldBeFalse();
    }

    [Fact]
    public async Task The_trail_names_the_reviewer_and_the_run_it_opened()
    {
        var runId = Guid.NewGuid();
        var reviewer = new ScriptedReviewer("{\"approved\":true}") { OpensRun = runId };
        var trail = new LoopCriterionTrail();

        await InScopeAsync(trail, () => new AgentReviewLoopEvaluator(reviewer, Reviewer).EvaluateAsync(Context("a draft")).AsTask());

        trail.Reviewer.ShouldBe(new LoopReviewerTrail(Reviewer, AgentVersion: null, runId));
        trail.Judge.ShouldBeNull();
    }

    [Fact]
    public async Task The_trail_still_names_the_run_when_the_call_fails()
    {
        var runId = Guid.NewGuid();
        var reviewer = new ScriptedReviewer("{\"approved\":true}") { OpensRun = runId, Fails = new InvalidOperationException("the provider is down") };
        var trail = new LoopCriterionTrail();

        await InScopeAsync(
            trail,
            async () => await Should.ThrowAsync<InvalidOperationException>(
                async () => await new AgentReviewLoopEvaluator(reviewer, Reviewer).EvaluateAsync(Context("a draft"))));

        trail.Reviewer.ShouldBe(new LoopReviewerTrail(Reviewer, AgentVersion: null, runId));
    }

    [Fact]
    public async Task A_call_refused_before_a_run_starts_names_the_reviewer_with_no_run()
    {
        var reviewer = new ScriptedReviewer("{\"approved\":true}") { Fails = new TraconException("refused"), OpensNoRun = true };
        var trail = new LoopCriterionTrail();

        await InScopeAsync(
            trail,
            async () => await Should.ThrowAsync<TraconException>(
                async () => await new AgentReviewLoopEvaluator(reviewer, Reviewer).EvaluateAsync(Context("a draft"))));

        trail.Reviewer.ShouldBe(new LoopReviewerTrail(Reviewer, AgentVersion: null, RunId: null));
    }

    [Fact]
    public async Task Our_own_cancellation_is_not_swallowed_into_a_verdict()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var reviewer = new ScriptedReviewer("{\"approved\":true}") { Fails = new OperationCanceledException(source.Token) };

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await new AgentReviewLoopEvaluator(reviewer, Reviewer).EvaluateAsync(Context("a draft"), source.Token));
    }

    [Fact]
    public async Task One_instance_serves_two_runs_at_once_without_mixing_their_reviewers()
    {
        // The first call is held inside the reviewer while the second one runs to its end.
        var reviewer = new ScriptedReviewer("{\"approved\":true}") { HoldsFirstCall = true };
        var evaluator = new AgentReviewLoopEvaluator(reviewer, Reviewer);
        var firstTrail = new LoopCriterionTrail();
        var secondTrail = new LoopCriterionTrail();

        var first = Task.Run(() => InScopeAsync(firstTrail, () => evaluator.EvaluateAsync(Context("one")).AsTask()));
        await reviewer.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Run(() => InScopeAsync(secondTrail, () => evaluator.EvaluateAsync(Context("two")).AsTask()));

        var secondRun = secondTrail.Reviewer.ShouldNotBeNull().RunId.ShouldNotBeNull();
        firstTrail.Reviewer.ShouldBeNull("the first call has not finished yet");

        reviewer.ReleaseFirstCall();
        await first;

        var firstRun = firstTrail.Reviewer.ShouldNotBeNull().RunId.ShouldNotBeNull();

        firstRun.ShouldNotBe(secondRun);
        reviewer.Prompts.Count.ShouldBe(2);
    }

    [Fact]
    public void A_response_format_declares_the_verdict_with_both_properties_typed()
    {
        AgentReviewLoopEvaluator.DeclaresVerdict(Schema("""{"type":"object","properties":{"approved":{"type":"boolean"},"feedback":{"type":"string"}}}"""))
            .ShouldBeTrue();
        AgentReviewLoopEvaluator.DeclaresVerdict(Schema("""{"type":"object","properties":{"approved":{"type":["boolean"]},"feedback":{"type":["string","null"]}}}"""))
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"approved":{"type":"boolean"}}}""")]
    [InlineData("""{"type":"object","properties":{"feedback":{"type":"string"}}}""")]
    [InlineData("""{"type":"object","properties":{"approved":{"type":"string"},"feedback":{"type":"string"}}}""")]
    [InlineData("""{"type":"object","properties":{"approved":{"type":"boolean"},"feedback":{"type":"integer"}}}""")]
    [InlineData("""{"type":"object","properties":{"approved":true,"feedback":true}}""")]
    [InlineData("""{"type":"object"}""")]
    public void A_response_format_without_the_typed_verdict_does_not_declare_it(string schema)
        => AgentReviewLoopEvaluator.DeclaresVerdict(Schema(schema)).ShouldBeFalse();

    [Fact]
    public void Plain_json_and_no_response_format_do_not_declare_the_verdict()
    {
        AgentReviewLoopEvaluator.DeclaresVerdict(null).ShouldBeFalse();
        AgentReviewLoopEvaluator.DeclaresVerdict(new AgentResponseFormat { Kind = AgentResponseFormatKind.Json }).ShouldBeFalse();
        AgentReviewLoopEvaluator.DeclaresVerdict(new AgentResponseFormat { Kind = AgentResponseFormatKind.Text }).ShouldBeFalse();
    }

    private static AgentResponseFormat Schema(string json)
        => new()
        {
            Kind = AgentResponseFormatKind.JsonSchema,
            Schema = JsonDocument.Parse(json).RootElement.Clone(),
            SchemaName = "verdict",
        };

    private static LoopContext Context(string draft)
        => Context(new AgentResponse(new ChatMessage(ChatRole.Assistant, draft)), iteration: 1, request: "go");

    private static LoopContext Context(AgentResponse response, int iteration, string request)
    {
        var context = new LoopContext(
            new NoopAgent(),
            new NoopSession(),
            [new ChatMessage(ChatRole.User, request)],
            response,
            new AgentRunOptions());

        // The framework's loop numbers its iterations; a test has no loop to do it.
        typeof(LoopContext).GetProperty(nameof(LoopContext.Iteration))!.GetSetMethod(nonPublic: true)!.Invoke(context, [iteration]);

        return context;
    }

    /// <summary>Runs <paramref name="body"/> with a run scope that carries <paramref name="trail"/>, the way the composite evaluator sets it.</summary>
    private static async Task InScopeAsync(LoopCriterionTrail trail, Func<Task> body)
    {
        var runId = Guid.NewGuid();

        // Set in this body, in the flow that awaits the evaluator: a scope set in a helper would not reach it.
        TraconRunContext.SetCurrent(new AgentRunScope { RunId = runId, RootRunId = runId, CriterionTrail = trail });

        try
        {
            await body();
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    /// <summary>A stand-in for <c>ChildAgentInvoker</c>: it answers with a scripted text and fills the report as the invoker does.</summary>
    private sealed class ScriptedReviewer(string answer) : AIAgent
    {
        private readonly TaskCompletionSource _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstCallReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public List<string> Prompts { get; } = [];

        /// <summary>The run each call opens. Every call draws its own when this is <see langword="null"/>.</summary>
        public Guid? OpensRun { get; init; }

        public Exception? Fails { get; init; }

        /// <summary>The call is refused before a run starts, as the invoker refuses at its gates.</summary>
        public bool OpensNoRun { get; init; }

        public bool HoldsFirstCall { get; init; }

        public Task FirstCallStarted => _firstCallStarted.Task;

        public void ReleaseFirstCall() => _firstCallReleased.SetResult();

        protected override async Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            lock (Prompts)
            {
                Prompts.Add(string.Join("\n", messages.Select(static message => message.Text)));
            }

            var call = Interlocked.Increment(ref _calls);

            // The invoker fills the report before the child starts, so a failure still names the run.
            if (!OpensNoRun)
            {
                ChildRunReport.Fill(options, OpensRun ?? Guid.NewGuid(), this);
            }

            if (HoldsFirstCall && call == 1)
            {
                _firstCallStarted.SetResult();
                await _firstCallReleased.Task;
            }

            if (Fails is not null)
            {
                throw Fails;
            }

            return new AgentResponse(new ChatMessage(ChatRole.Assistant, answer));
        }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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
