using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// Where the harness loop ends up in the compiled agent, and — the far more
/// important half — that it ends up nowhere at all unless the definition asked
/// for it.
/// </summary>
/// <remarks>
/// The chain is probed with <c>GetService&lt;LoopAgent&gt;()</c> rather than by
/// reading a private field: <c>DelegatingAIAgent.GetService&lt;T&gt;</c> walks the chain
/// inward, so a positive answer means MAF really built the loop, not that
/// Tracon assigned an option and hoped.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class LoopCompilationTests
{
    [Fact]
    public void A_harness_without_loop_settings_gets_no_loop_at_all()
    {
        var compiler = BuildCompiler();

        var agent = compiler.Compile(TestData.Definition(harness: new HarnessSettings
        {
            MaxContextWindowTokens = 4_096,
        }));

        // The whole point of K1: a definition written before this member
        // existed compiles to exactly the agent it compiled to before.
        agent.ShouldBeOfType<HarnessAgent>();
        agent.GetService<LoopAgent>().ShouldBeNull();
    }

    [Fact]
    public void A_plain_chat_agent_is_unaffected_by_the_loop_surface()
    {
        var compiler = BuildCompiler();

        var agent = compiler.Compile(TestData.Definition());

        agent.GetService<LoopAgent>().ShouldBeNull();
    }

    [Fact]
    public void A_harness_with_loop_settings_is_wrapped_in_the_framework_loop()
    {
        var compiler = BuildCompiler();

        var agent = compiler.Compile(TestData.Definition(harness: new HarnessSettings
        {
            Loop = new LoopSettings
            {
                Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }],
                MaxIterations = 4,
            },
        }));

        agent.ShouldBeOfType<HarnessAgent>();
        agent.GetService<LoopAgent>().ShouldNotBeNull();
    }

    [Fact]
    public void An_unusable_loop_setting_fails_compilation_rather_than_compiling_a_loop_that_never_stops()
    {
        var compiler = BuildCompiler();
        var definition = TestData.Definition(harness: new HarnessSettings
        {
            Loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = "no-such-thing" }] },
        });

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe(definition.Name);
    }

    [Fact]
    public void A_criterion_registered_in_code_is_resolvable_from_a_declarative_definition()
    {
        var custom = new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()));
        var compiler = BuildCompiler(new TraconLoopEvaluatorRegistration("hasCitations", custom));

        var agent = compiler.Compile(TestData.Definition(harness: new HarnessSettings
        {
            Loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = "hasCitations" }] },
        }));

        agent.GetService<LoopAgent>().ShouldNotBeNull();
    }

    private static AgentDefinitionCompiler BuildCompiler(params TraconLoopEvaluatorRegistration[] registrations)
        => new(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            loopEvaluators: registrations);
}
#pragma warning restore MAAI001
