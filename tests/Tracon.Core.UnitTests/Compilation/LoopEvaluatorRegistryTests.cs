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

    // ---- Phase 201: factory registrations (F-301) and the shared rule set ----

    [Fact]
    public async Task Concurrent_first_compilations_call_a_factory_once_and_share_its_result()
    {
        // Two agents naming the same kind can compile at the same moment on a
        // cold process. Two calls would build two criteria — and run a factory
        // that opens a connection or resolves an agent twice.
        var calls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var registry = BuildRegistry(new TraconLoopEvaluatorRegistration(
            "slow",
            _ =>
            {
                Interlocked.Increment(ref calls);
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));

                return new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
            },
            Description: null));
        var definition = WithLoop(new LoopSettings { Criteria = [new LoopCriterion { Kind = "slow" }] });

        var compilations = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => Build(registry, definition).Evaluator))
            .ToArray();

        entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        release.Set();
        var evaluators = await Task.WhenAll(compilations).WaitAsync(TimeSpan.FromSeconds(10));

        calls.ShouldBe(1);
        evaluators
            .Select(static evaluator => evaluator.ShouldBeOfType<RecordingLoopEvaluator>().Criteria[0].Evaluator)
            .Distinct()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void A_factory_that_throws_is_not_cached_and_the_kind_is_named()
    {
        // A dependency that is not ready on the first compilation must not
        // poison the kind for the life of the process.
        var calls = 0;
        var registry = BuildRegistry(new TraconLoopEvaluatorRegistration(
            "flaky",
            _ => ++calls == 1
                ? throw new InvalidOperationException("Connection string 'secret-db' is missing.")
                : new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop())),
            Description: null));
        var definition = WithLoop(new LoopSettings { Criteria = [new LoopCriterion { Kind = "flaky" }] });

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, definition));

        exception.AgentName.ShouldBe(definition.Name);
        exception.Message.ShouldContain("'flaky'");
        exception.Message.ShouldContain(nameof(InvalidOperationException));
        exception.InnerException.ShouldBeOfType<InvalidOperationException>();

        // The host's own message stays in the log: it can carry what the factory
        // read from configuration, and this message reaches the management API.
        exception.Message.ShouldNotContain("secret-db");

        Build(registry, definition).Evaluator.ShouldBeOfType<RecordingLoopEvaluator>();
        calls.ShouldBe(2);

        Build(registry, definition);
        calls.ShouldBe(2);
    }

    [Fact]
    public void A_factory_that_returns_null_fails_the_compilation()
    {
        var registry = BuildRegistry(new TraconLoopEvaluatorRegistration("empty", _ => null!, Description: null));
        var definition = WithLoop(new LoopSettings { Criteria = [new LoopCriterion { Kind = "empty" }] });

        Should.Throw<TraconCompilationException>(() => Build(registry, definition))
            .Message.ShouldContain("returned null");
    }

    [Fact]
    public void A_factory_that_asks_for_its_own_criterion_fails_instead_of_recursing()
    {
        LoopEvaluatorRegistry? registry = null;
        var definition = WithLoop(new LoopSettings { Criteria = [new LoopCriterion { Kind = "selfish" }] });

        registry = BuildRegistry(new TraconLoopEvaluatorRegistration(
            "selfish",
            _ => Build(registry!, definition).Evaluator,
            Description: null));

        Should.Throw<TraconCompilationException>(() => Build(registry, definition))
            .Message.ShouldContain("asked for the same criterion");
    }

    [Fact]
    public void A_factory_receives_the_registrys_service_provider()
    {
        IServiceProvider? received = null;
        var services = new ProbeServices();
        var registry = new LoopEvaluatorRegistry(
            [
                new TraconLoopEvaluatorRegistration(
                    "probe",
                    provider =>
                    {
                        received = provider;
                        return new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
                    },
                    Description: null),
            ],
            TestData.Providers(new FakeModelProvider()),
            services: services);

        Build(registry, WithLoop(new LoopSettings { Criteria = [new LoopCriterion { Kind = "probe" }] }));

        received.ShouldBeSameAs(services);
    }

    [Fact]
    public void Duplicate_and_shadowing_factory_registrations_are_refused_before_any_factory_runs()
    {
        var calls = 0;
        Func<IServiceProvider, LoopEvaluator> factory = _ =>
        {
            calls++;
            return new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
        };

        Should.Throw<TraconException>(() => BuildRegistry(
                new TraconLoopEvaluatorRegistration("reviewed", factory, Description: null),
                new TraconLoopEvaluatorRegistration("REVIEWED", factory, Description: null)))
            .Message.ShouldContain("REVIEWED");

        Should.Throw<TraconException>(() => BuildRegistry(
                new TraconLoopEvaluatorRegistration("completionmarker", factory, Description: null)))
            .Message.ShouldContain("built in");

        calls.ShouldBe(0);
    }

    [Fact]
    public void The_kind_list_puts_built_ins_first_then_code_kinds_by_name_without_calling_a_factory()
    {
        var calls = 0;
        Func<IServiceProvider, LoopEvaluator> factory = _ =>
        {
            calls++;
            return new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
        };
        var registry = BuildRegistry(
            new TraconLoopEvaluatorRegistration("zeta", factory, "Checks the last letter."),
            new TraconLoopEvaluatorRegistration("alpha", factory, Description: null));

        registry.Kinds.Select(static kind => (kind.Kind, kind.BuiltIn)).ShouldBe(
        [
            ("completionMarker", true),
            ("todoCompletion", true),
            ("aiJudge", true),
            ("backgroundTaskCompletion", true),
            ("alpha", false),
            ("zeta", false),
        ]);
        registry.Kinds.Where(static kind => kind.BuiltIn).ShouldAllBe(static kind => kind.Description != null);
        registry.Kinds.Single(static kind => string.Equals(kind.Kind, "zeta", StringComparison.Ordinal)).Description.ShouldBe("Checks the last letter.");
        registry.Kinds.Single(static kind => string.Equals(kind.Kind, "alpha", StringComparison.Ordinal)).Description.ShouldBeNull();
        calls.ShouldBe(0);
    }

    [Fact]
    public void The_judge_model_is_the_host_binding_or_null()
    {
        BuildRegistry().JudgeModel.ShouldBeNull();

        var judge = TestData.Binding();
        BuildRegistry(judge: new ModelRunJudgeOptions { Model = judge }).JudgeModel.ShouldBeSameAs(judge);
    }

    [Fact]
    public void Every_problem_is_found_with_its_code_and_path_and_Build_refuses_with_all_of_them()
    {
        // The validation report and the compiler read ONE rule set. Before, the
        // compiler stopped at the first problem and the report printed only that
        // one, without a path.
        var registry = BuildRegistry();
        var loop = new LoopSettings
        {
            Criteria =
            [
                new LoopCriterion { Kind = "completionMarker" },
                new LoopCriterion { Kind = "x" },
                new LoopCriterion { Kind = " " },
                new LoopCriterion { Kind = "aiJudge", JudgeCriteria = ["  "] },
            ],
            MaxIterations = 0,
            OnExhausted = (LoopOutcome)7,
        };

        var problems = registry.FindProblems(loop);

        problems.Select(static problem => (problem.Code, problem.Path)).ShouldBe(
        [
            ("loop_max_iterations_invalid", "harness.loop.maxIterations"),
            ("loop_outcome_invalid", "harness.loop.onExhausted"),
            ("loop_marker_missing", "harness.loop.criteria[0].marker"),
            ("loop_criterion_unknown", "harness.loop.criteria[1].kind"),
            ("loop_criterion_kind_missing", "harness.loop.criteria[2].kind"),
            ("loop_judge_criteria_missing", "harness.loop.criteria[3].judgeCriteria[0]"),
            ("loop_judge_unconfigured", "harness.loop.criteria[3].kind"),
        ]);

        var exception = Should.Throw<TraconCompilationException>(() => Build(registry, WithLoop(loop)));

        foreach (var problem in problems)
        {
            exception.Message.ShouldContain(problem.Message);
        }
    }

    [Fact]
    public void An_empty_criteria_list_is_one_problem_at_the_list_path()
    {
        var problem = BuildRegistry().FindProblems(new LoopSettings { Criteria = [] }).ShouldHaveSingleItem();

        problem.Code.ShouldBe("loop_criteria_empty");
        problem.Path.ShouldBe("harness.loop.criteria");
    }

    [Fact]
    public void A_blank_judge_criterion_beside_a_real_one_is_refused_at_its_own_entry()
    {
        // Before, only an ALL-blank list was refused; ["ok?", "  "] compiled and
        // the judge received a criterion with no text.
        var registry = BuildRegistry(judge: new ModelRunJudgeOptions { Model = TestData.Binding() });
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "aiJudge", JudgeCriteria = ["ok?", "  "] }],
        };

        var problem = registry.FindProblems(loop).ShouldHaveSingleItem();

        problem.Code.ShouldBe("loop_judge_criteria_missing");
        problem.Path.ShouldBe("harness.loop.criteria[0].judgeCriteria[1]");
        Should.Throw<TraconCompilationException>(() => Build(registry, WithLoop(loop)))
            .Message.ShouldContain("Entry 2");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void A_positive_MaxIterations_at_either_end_has_no_problem(int maxIterations)
    {
        var registry = BuildRegistry();
        var loop = new LoopSettings
        {
            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }],
            MaxIterations = maxIterations,
            OnExhausted = LoopOutcome.FailRun,
        };

        registry.FindProblems(loop).ShouldBeEmpty();

        // int.MaxValue with FailRun must not overflow the framework's N + 1.
        Build(registry, WithLoop(loop)).Options.MaxIterations.ShouldBe(maxIterations == int.MaxValue ? int.MaxValue : maxIterations + 1);
    }

    [Fact]
    public void A_kind_registered_in_code_is_known_to_the_rule_set_before_its_factory_runs()
    {
        var calls = 0;
        var registry = BuildRegistry(new TraconLoopEvaluatorRegistration(
            "reviewed",
            _ =>
            {
                calls++;
                return new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
            },
            Description: null));

        registry.FindProblems(new LoopSettings { Criteria = [new LoopCriterion { Kind = "Reviewed" }] }).ShouldBeEmpty();
        calls.ShouldBe(0);
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

    /// <summary>A provider whose only job is to be recognized by reference.</summary>
    private sealed class ProbeServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
#pragma warning restore MAAI001
