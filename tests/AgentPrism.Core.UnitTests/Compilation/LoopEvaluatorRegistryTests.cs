using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

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

        var (evaluator, _) = registry.Build(definition);

        var composite = evaluator.ShouldBeOfType<RecordingLoopEvaluator>();
        composite.Criteria.Select(static binding => binding.Kind)
            .ShouldBe(["completionMarker", "backgroundTaskCompletion", "todoCompletion"]);
        composite.Criteria[0].Evaluator.ShouldBeOfType<CompletionMarkerLoopEvaluator>();
        composite.Criteria[1].Evaluator.ShouldBeOfType<BackgroundTaskCompletionLoopEvaluator>();
        composite.Criteria[2].Evaluator.ShouldBeOfType<TodoCompletionLoopEvaluator>();
    }

    [Fact]
    public void An_unset_MaxIterations_takes_the_AgentPrism_default_instead_of_the_frameworks()
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }],
        });

        var (_, options) = registry.Build(definition);

        // The ceiling is AgentPrism's own promise, not a value inherited from
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

        var (_, options) = registry.Build(definition);

        options.MaxIterations.ShouldBe(3);
        options.FreshContextPerIteration.ShouldBeTrue();

        // Every iteration's messages stay in the response, so the run record
        // carries the whole loop and not only its last turn.
        options.NonStreamingReturnsLastResponseOnly.ShouldBeFalse();
    }

    [Fact]
    public void An_empty_criteria_list_is_refused()
    {
        var registry = BuildRegistry();
        var definition = WithLoop(new LoopSettings { Criteria = [] });

        var exception = Should.Throw<AgentPrismCompilationException>(() => registry.Build(definition));

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

        var exception = Should.Throw<AgentPrismCompilationException>(() => registry.Build(definition));

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

        var exception = Should.Throw<AgentPrismCompilationException>(() => registry.Build(definition));

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

        var exception = Should.Throw<AgentPrismCompilationException>(() => registry.Build(definition));

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

        var exception = Should.Throw<AgentPrismCompilationException>(() => registry.Build(definition));

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

        var exception = Should.Throw<AgentPrismCompilationException>(() => registry.Build(definition));

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

        var (evaluator, _) = registry.Build(definition);

        var composite = evaluator.ShouldBeOfType<RecordingLoopEvaluator>();
        composite.Criteria[0].Evaluator.ShouldBeOfType<AIJudgeLoopEvaluator>();
    }

    [Fact]
    public void A_kind_registered_in_code_resolves_by_its_declarative_name()
    {
        var custom = new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
        var registry = BuildRegistry(new AgentPrismLoopEvaluatorRegistration("hasCitations", custom));
        var definition = WithLoop(new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "hasCitations" }],
        });

        var (evaluator, _) = registry.Build(definition);

        var composite = evaluator.ShouldBeOfType<RecordingLoopEvaluator>();
        composite.Criteria[0].Evaluator.ShouldBeSameAs(custom);
    }

    [Fact]
    public void The_same_kind_registered_twice_is_refused_at_construction()
    {
        var first = new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
        var second = new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));

        var exception = Should.Throw<AgentPrismException>(() => BuildRegistry(
            new AgentPrismLoopEvaluatorRegistration("hasCitations", first),
            new AgentPrismLoopEvaluatorRegistration("hasCitations", second)));

        exception.Message.ShouldContain("hasCitations");
    }

    [Fact]
    public void Shadowing_a_built_in_kind_is_refused_at_construction()
    {
        // Allowing it would let the SAME definition mean different things in
        // two applications, which is exactly what a declarative kind name is
        // supposed to rule out.
        var custom = new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));

        var exception = Should.Throw<AgentPrismException>(() => BuildRegistry(
            new AgentPrismLoopEvaluatorRegistration("aiJudge", custom)));

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

        var (evaluator, _) = registry.Build(definition);

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

        var exception = Should.Throw<AgentPrismCompilationException>(() => registry.Build(definition));

        exception.Message.ShouldContain("noSuchThing");
        exception.Message.ShouldContain("completionMarker");
    }

    private static LoopEvaluatorRegistry BuildRegistry(params AgentPrismLoopEvaluatorRegistration[] registrations)
        => BuildRegistry(judge: null, registrations);

    private static LoopEvaluatorRegistry BuildRegistry(
        ModelRunJudgeOptions? judge,
        params AgentPrismLoopEvaluatorRegistration[] registrations)
        => new(registrations, TestData.Providers(new FakeModelProvider()), judge);

    private static AgentDefinition WithLoop(LoopSettings loop)
        => TestData.Definition(harness: new HarnessSettings { Loop = loop });
}
#pragma warning restore MAAI001
