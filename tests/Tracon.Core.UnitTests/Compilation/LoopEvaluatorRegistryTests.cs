using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The translation of a definition's declarative <see cref="LoopSettings"/>
/// into the Microsoft Agent Framework loop: which criterion kinds are built in,
/// which ones come from code, and every way the settings are refused rather
/// than silently accepted.
/// </summary>
/// <remarks>
/// A refusal matters more here than anywhere else in compilation. Every
/// rejected case in this class is one that would otherwise produce a loop with
/// no reachable stop criterion, which is a loop that only ever ends by running
/// out of iterations — a bill the consumer first sees on an invoice.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class LoopEvaluatorRegistryTests
{
    [Fact]
    public void Builds_one_binding_per_criterion_in_the_declared_order()
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria =
            [
                new LoopCriterion { Kind = "completionMarker", Marker = "DONE" },
                new LoopCriterion { Kind = "backgroundTaskCompletion" },
                new LoopCriterion { Kind = "todoCompletion" },
            ],
        });

        var (evaluator, _) = Build(registry, definition);

        var composite = evaluator.ShouldBeOfType<RecordingLoopEvaluator>();
        composite.Criteria.Select(static binding => binding.Kind)
            .ShouldBe(["completionMarker", "backgroundTaskCompletion", "todoCompletion"]);
        composite.Criteria[0].Evaluator.ShouldBeOfType<CompletionMarkerLoopEvaluator>();
        composite.Criteria[1].Evaluator.ShouldBeOfType<BackgroundTaskCompletionLoopEvaluator>();
        composite.Criteria[2].Evaluator.ShouldBeOfType<TodoCompletionLoopEvaluator>();
    }

    [Fact]
    public void An_unset_MaxIterations_takes_the_Tracon_default_instead_of_the_frameworks()
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }],
        });

        var (_, options) = Build(registry, definition);

        // The ceiling is Tracon's own promise, not a value inherited from
        // whatever MAF happens to default to this version.
        options.MaxIterations.ShouldBe(LoopSettings.DefaultMaxIterations);
    }

    [Fact]
    public void The_definitions_own_MaxIterations_and_FreshContextPerIteration_reach_the_framework_options()
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }],
            MaxIterations = 3,
            FreshContextPerIteration = true,
        });

        var (_, options) = Build(registry, definition);

        options.MaxIterations.ShouldBe(3);
        options.FreshContextPerIteration.ShouldBeTrue();

        // A non-streaming run returns the final iteration's answer, with the
        // usage of every iteration (K-889); the iterations stay on the run as
        // LoopIterationCompleted events.
        options.NonStreamingReturnsLastResponseOnly.ShouldBeTrue();
    }

    /// <summary>
    /// Phase 192: the framework does not evaluate the turn that reaches its ceiling.
    /// Failing the run there without evaluating it would refuse a last response that
    /// meets every criterion, so in FailRun mode the framework gets one iteration more
    /// and the composite decides at the definition's own ceiling. The default mode
    /// keeps today's ceiling, events and cost exactly.
    /// </summary>
    [Theory]
    [InlineData(LoopOutcome.ReturnLastResponse, 3, 3)]
    [InlineData(LoopOutcome.FailRun, 3, 4)]
    [InlineData(LoopOutcome.FailRun, 1, 2)]
    public void FailRun_on_exhaustion_gives_the_framework_one_more_iteration(LoopOutcome onExhausted, int maxIterations, int frameworkCeiling)
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }],
            MaxIterations = maxIterations,
            OnExhausted = onExhausted,
        });

        var (_, options) = Build(registry, definition);

        options.MaxIterations.ShouldBe(frameworkCeiling);
    }

    [Fact]
    public void The_criterion_error_policy_does_not_change_the_framework_ceiling()
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }],
            MaxIterations = 3,
            OnCriterionError = LoopOutcome.FailRun,
        });

        var (_, options) = Build(registry, definition);

        options.MaxIterations.ShouldBe(3);
    }

    /// <summary>
    /// A number that is not a member would otherwise behave like the default in
    /// silence: the definition said "fail", or something, and the loop returned.
    /// </summary>
    [Theory]
    [InlineData(nameof(LoopSettings.OnExhausted))]
    [InlineData(nameof(LoopSettings.OnCriterionError))]
    public void An_undefined_loop_outcome_is_refused(string field)
    {
        var registry = BuildRegistry();
        var undefined = (LoopOutcome)7;
        var loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }] };
        var definition = WithLoop(string.Equals(field, nameof(LoopSettings.OnExhausted), StringComparison.Ordinal)
            ? loop with { OnExhausted = undefined }
            : loop with { OnCriterionError = undefined });

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, definition));

        exception.AgentName.ShouldBe(definition.Name);
        exception.Message.ShouldContain(field);
    }

    [Fact]
    public void An_empty_criteria_list_is_refused()
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings { Criteria = [] });

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, definition));

        exception.AgentName.ShouldBe(definition.Name);
        exception.Message.ShouldContain(nameof(LoopSettings.Criteria));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_MaxIterations_is_refused(int maxIterations)
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }],
            MaxIterations = maxIterations,
        });

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, definition));

        exception.Message.ShouldContain(nameof(LoopSettings.MaxIterations));
    }

    [Fact]
    public void An_unknown_kind_is_refused_and_names_the_registration_call()
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "no-such-thing" }],
        });

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, definition));

        exception.AgentName.ShouldBe(definition.Name);
        exception.Message.ShouldContain("no-such-thing");
        exception.Message.ShouldContain("AddLoopEvaluator");
    }

    [Fact]
    public void A_completionMarker_without_a_marker_is_refused()
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker" }],
        });

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, definition));

        exception.Message.ShouldContain(nameof(LoopCriterion.Marker));
    }

    [Fact]
    public void An_aiJudge_without_criteria_is_refused()
    {
        var registry = BuildRegistry(judge: new ModelRunJudgeOptions { Model = TestData.Binding() });
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "aiJudge" }],
        });

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, definition));

        exception.Message.ShouldContain(nameof(LoopCriterion.JudgeCriteria));
    }

    [Fact]
    public void An_aiJudge_without_a_configured_judge_model_is_refused_instead_of_borrowing_the_agents_model()
    {
        // The agent's own binding is perfectly usable here. It is still refused:
        // the judge runs on EVERY iteration, and charging that to an expensive
        // agent model would multiply the loop's bill without anyone asking.
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "aiJudge", JudgeCriteria = ["Is the answer complete?"] }],
        });

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, definition));

        exception.Message.ShouldContain("AddModelRunJudge");
    }

    [Fact]
    public void An_aiJudge_uses_the_separately_configured_judge_binding()
    {
        var registry = BuildRegistry(judge: new ModelRunJudgeOptions { Model = TestData.Binding() });
        var definition = WithLoop(new LoopSettings
        {
            Criteria =
            [
                new LoopCriterion
                {
                    Kind = "aiJudge",
                    JudgeCriteria = ["Is the answer complete?"],
                    JudgeInstructions = "Be strict.",
                },
            ],
        });

        var (evaluator, _) = Build(registry, definition);

        var composite = evaluator.ShouldBeOfType<RecordingLoopEvaluator>();
        composite.Criteria[0].Evaluator.ShouldBeOfType<AIJudgeLoopEvaluator>();
    }

    [Fact]
    public void A_kind_registered_in_code_resolves_by_its_declarative_name()
    {
        var custom = new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
        var registry = BuildRegistry(new TraconLoopEvaluatorRegistration("hasCitations", custom));
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "hasCitations" }],
        });

        var (evaluator, _) = Build(registry, definition);

        var composite = evaluator.ShouldBeOfType<RecordingLoopEvaluator>();
        composite.Criteria[0].Evaluator.ShouldBeSameAs(custom);
    }

    [Fact]
    public void The_same_kind_registered_twice_is_refused_at_construction()
    {
        var first = new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
        var second = new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));

        var exception = Should.Throw<TraconException>(() => BuildRegistry(
            new TraconLoopEvaluatorRegistration("hasCitations", first),
            new TraconLoopEvaluatorRegistration("hasCitations", second)));

        exception.Message.ShouldContain("hasCitations");
    }

    [Fact]
    public void Shadowing_a_built_in_kind_is_refused_at_construction()
    {
        // Allowing it would let the SAME definition mean different things in
        // two applications, which is exactly what a declarative kind name is
        // supposed to rule out.
        var custom = new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));

        var exception = Should.Throw<TraconException>(() => BuildRegistry(
            new TraconLoopEvaluatorRegistration("aiJudge", custom)));

        exception.Message.ShouldContain("aiJudge");
    }

    [Theory]
    [InlineData("aijudge")]
    [InlineData("AIJUDGE")]
    [InlineData("CompletionMarker")]
    public void A_built_in_kind_resolves_whatever_its_casing(string kind)
    {
        // Registration matched built-in names case-INSENSITIVELY while use
        // matched them case-SENSITIVELY, so "aijudge" was a dead name: it
        // could not be registered ("built in and cannot be replaced") and it
        // could not be used ("unknown kind"). The two messages read as a
        // contradiction and the consumer cannot tell casing is the reason.
        var registry = BuildRegistry(
            judge: new ModelRunJudgeOptions { Model = TestData.Binding() });

        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = kind, Marker = "DONE", JudgeCriteria = ["is it done"] }],
        });

        var (evaluator, _) = Build(registry, definition);

        evaluator.ShouldBeOfType<RecordingLoopEvaluator>().Criteria.ShouldHaveSingleItem();
    }

    [Fact]
    public void An_unknown_kind_still_fails_and_names_the_built_in_kinds()
    {
        // The other direction: widening the match must not turn every name
        // into a built-in one.
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "noSuchThing" }],
        });

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, definition));

        exception.Message.ShouldContain("noSuchThing");
        exception.Message.ShouldContain("completionMarker");
    }

    private static LoopEvaluatorRegistry BuildRegistry(params TraconLoopEvaluatorRegistration[] registrations)
        => BuildRegistry(judge: null, registrations);

    // The compiler builds the judge client before the loop; these tests need
    // no tenant, so they take the synchronous member.
    private static (LoopEvaluator Evaluator, LoopAgentOptions Options) Build(
        LoopEvaluatorRegistry registry,
        AgentDefinition definition)
        => registry.Build(definition, registry.CreateJudgeChatClient(definition));

    private static LoopEvaluatorRegistry BuildRegistry(
        ModelRunJudgeOptions? judge,
        params TraconLoopEvaluatorRegistration[] registrations)
        => new(registrations, TestData.Providers(new FakeModelProvider()), judge);

    private static AgentDefinition WithLoop(LoopSettings loop)
        => TestData.Definition(harness: new HarnessSettings { Loop = loop });
}
#pragma warning restore MAAI001
