using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// An agent carries at most <see cref="TraconToolOptions.MaxToolsPerAgent"/> tools, and one over it does not compile.
/// </summary>
/// <remarks>
/// Every tool's definition goes into every model request: 5 000 tools put about 690 KB of definitions into each one, and OpenAI
/// refuses a request with more than 128. The count is what the model would be sent, so the tools a definition selects and
/// the ones the compiler adds are counted alike.
/// </remarks>
public sealed class AgentToolLimitTests
{
    [Fact]
    public void An_agent_with_exactly_the_default_limit_of_tools_compiles()
    {
        var (compiler, definition) = Setup(TraconToolOptions.DefaultMaxToolsPerAgent, limit: null);

        var agent = compiler.Compile(definition);

        agent.GetService<ChatClientAgentOptions>()!.ChatOptions!.Tools!.Count.ShouldBe(TraconToolOptions.DefaultMaxToolsPerAgent);
    }

    [Fact]
    public void An_agent_with_one_tool_over_the_default_does_not_compile_and_the_error_names_the_option_and_the_count()
    {
        var (compiler, definition) = Setup(TraconToolOptions.DefaultMaxToolsPerAgent + 1, limit: null);

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("many");
        exception.Message.ShouldContain("'many'");
        exception.Message.ShouldContain("129 tools");
        exception.Message.ShouldContain("128");
        exception.Message.ShouldContain("Tracon:Tools:MaxToolsPerAgent");
    }

    [Fact]
    public void A_raised_limit_lets_a_larger_agent_compile_and_a_lowered_one_stops_a_smaller()
    {
        var (large, largeDefinition) = Setup(200, limit: 200);
        var (small, smallDefinition) = Setup(5, limit: 4);

        large.Compile(largeDefinition).GetService<ChatClientAgentOptions>()!.ChatOptions!.Tools!.Count.ShouldBe(200);
        Should.Throw<TraconCompilationException>(() => small.Compile(smallDefinition)).Message.ShouldContain("5 tools");
    }

    [Fact]
    public void An_agent_with_no_tool_compiles_under_the_smallest_limit()
    {
        var (compiler, definition) = Setup(0, limit: 1);

        Should.NotThrow(() => compiler.Compile(definition));
    }

    [Fact]
    public void The_limit_defaults_to_128_and_a_value_below_one_stops_the_host()
    {
        new TraconToolOptions().MaxToolsPerAgent.ShouldBe(128);

        var options = new TraconOptions();
        options.Tools.MaxToolsPerAgent = 0;

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(failure => failure.Contains(nameof(TraconToolOptions.MaxToolsPerAgent), StringComparison.Ordinal));
    }

    private static (AgentDefinitionCompiler Compiler, AgentDefinition Definition) Setup(int tools, int? limit)
    {
        var names = Enumerable.Range(0, tools).Select(static index => $"tool_{index}").ToArray();
        var registry = TestData.Registry([.. names.Select(static name => AIFunctionFactory.Create(() => name, name, "A tool."))]);
        var providers = TestData.Providers(new FakeModelProvider());
        var compiler = limit is { } value
            ? new AgentDefinitionCompiler(providers, registry, new AgentCompilationServices { MaxToolsPerAgent = value })
            : new AgentDefinitionCompiler(providers, registry);

        return (compiler, TestData.Definition("many", names));
    }
}
