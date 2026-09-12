using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The resolution order and validation of a sub-agent's wait limits
/// (<see cref="AgentDefinitionCompiler.ResolveSubAgentTimeouts"/>): the
/// definition's own <see cref="SubAgentSettings"/> override the tree-wide
/// <see cref="TraconAgentGraphOptions"/> defaults field by field, and the
/// resolved pair is validated together, at compile time.
/// </summary>
public sealed class SubAgentSettingsResolutionTests
{
    [Fact]
    public void Falls_back_to_the_tree_wide_defaults_when_the_agent_sets_nothing()
    {
        var graph = new TraconAgentGraphOptions
        {
            ChildDeadline = TimeSpan.FromSeconds(45),
            WaitTimeout = TimeSpan.FromSeconds(90),
        };
        var compiler = BuildCompiler(graph);

        var (childDeadline, waitTimeout) = compiler.ResolveSubAgentTimeouts(TestData.Definition());

        childDeadline.ShouldBe(TimeSpan.FromSeconds(45));
        waitTimeout.ShouldBe(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void The_agent_own_ChildDeadline_wins_over_the_tree_wide_default()
    {
        var graph = new TraconAgentGraphOptions
        {
            ChildDeadline = TimeSpan.FromSeconds(45),
            WaitTimeout = TimeSpan.FromSeconds(90),
        };
        var compiler = BuildCompiler(graph);
        var definition = TestData.Definition() with
        {
            SubAgents = new SubAgentSettings { ChildDeadline = TimeSpan.FromSeconds(5) },
        };

        var (childDeadline, waitTimeout) = compiler.ResolveSubAgentTimeouts(definition);

        // The override replaces ONLY ChildDeadline; WaitTimeout still falls
        // back to the tree-wide default, field by field, not all-or-nothing.
        childDeadline.ShouldBe(TimeSpan.FromSeconds(5));
        waitTimeout.ShouldBe(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void The_agent_own_WaitTimeout_wins_over_the_tree_wide_default()
    {
        var graph = new TraconAgentGraphOptions
        {
            ChildDeadline = TimeSpan.FromSeconds(5),
            WaitTimeout = TimeSpan.FromSeconds(10),
        };
        var compiler = BuildCompiler(graph);
        var definition = TestData.Definition() with
        {
            SubAgents = new SubAgentSettings { WaitTimeout = TimeSpan.FromSeconds(60) },
        };

        var (childDeadline, waitTimeout) = compiler.ResolveSubAgentTimeouts(definition);

        childDeadline.ShouldBe(TimeSpan.FromSeconds(5));
        waitTimeout.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_resolved_ChildDeadline_is_rejected_at_compile_time(int seconds)
    {
        var compiler = BuildCompiler(new TraconAgentGraphOptions());
        var definition = TestData.Definition() with
        {
            SubAgents = new SubAgentSettings { ChildDeadline = TimeSpan.FromSeconds(seconds) },
        };

        var exception = Should.Throw<TraconCompilationException>(() => compiler.ResolveSubAgentTimeouts(definition));

        exception.AgentName.ShouldBe(definition.Name);
        exception.Message.ShouldContain(nameof(SubAgentSettings.ChildDeadline));
    }

    [Fact]
    public void A_resolved_WaitTimeout_that_does_not_exceed_ChildDeadline_is_rejected_at_compile_time()
    {
        var compiler = BuildCompiler(new TraconAgentGraphOptions());
        var definition = TestData.Definition() with
        {
            SubAgents = new SubAgentSettings
            {
                ChildDeadline = TimeSpan.FromSeconds(30),
                WaitTimeout = TimeSpan.FromSeconds(30),
            },
        };

        var exception = Should.Throw<TraconCompilationException>(() => compiler.ResolveSubAgentTimeouts(definition));

        exception.AgentName.ShouldBe(definition.Name);
        exception.Message.ShouldContain(nameof(SubAgentSettings.WaitTimeout));
        exception.Message.ShouldContain(nameof(SubAgentSettings.ChildDeadline));
    }

    private static AgentDefinitionCompiler BuildCompiler(TraconAgentGraphOptions graph)
        => new(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            agentGraph: graph);
}
