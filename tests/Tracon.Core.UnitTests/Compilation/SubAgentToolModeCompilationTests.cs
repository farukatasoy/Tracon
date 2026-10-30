using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The compiler refuses a <see cref="SubAgentCallMode.Tool"/> mode definition whose tool names
/// collide, the same refusal the validator reports. A definition written in code
/// never meets the validator.
/// </summary>
public sealed class SubAgentToolModeCompilationTests
{
    [Fact]
    public void A_callable_agent_whose_tool_name_is_taken_fails_the_compile_and_names_both()
    {
        var compiler = BuildCompiler(out var callable);
        var definition = TestData.Definition(toolNames: ["support_agent"]) with
        {
            CallableAgents = ["support-agent"],
            SubAgents = new SubAgentSettings { CallMode = SubAgentCallMode.Tool },
        };

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition, callable));

        exception.AgentName.ShouldBe(definition.Name);
        exception.Message.ShouldContain("'support-agent'");
        exception.Message.ShouldContain("'support_agent'");
    }

    [Fact]
    public void The_same_names_compile_in_background_mode()
    {
        var compiler = BuildCompiler(out var callable);
        var definition = TestData.Definition(toolNames: ["support_agent"]) with
        {
            CallableAgents = ["support-agent"],
        };

        Should.NotThrow(() => compiler.Compile(definition, callable));
    }

    [Fact]
    public void The_agent_tool_is_in_the_options_an_extension_sees()
    {
        // The agent tool joins the tool list before the extensions run, so the guard's baseline
        // holds it. Moving the AddRange behind the extension call would break no other test.
        var seen = new List<string>();
        var compiler = BuildCompiler(
            out var callable,
            new DelegateAgentExtension(
                "probe",
                context => seen.AddRange(context.ChatAgentOptions!.ChatOptions!.Tools!.Select(static tool => tool.Name)),
                null));

        _ = compiler.Compile(ToolModeDefinition(), callable);

        seen.ShouldContain("support_agent", StringComparer.Ordinal);
    }

    [Fact]
    public void An_extension_that_takes_the_agent_tool_out_of_the_options_fails_the_compile()
    {
        var compiler = BuildCompiler(
            out var callable,
            new DelegateAgentExtension(
                "probe",
                static context => context.ChatAgentOptions!.ChatOptions!.Tools!.Clear(),
                null));

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(ToolModeDefinition(), callable));

        exception.Message.ShouldContain("'probe'");
        exception.Message.ShouldContain("ChatOptions.Tools");
    }

    [Fact]
    public void An_extension_that_adds_a_tool_next_to_the_agent_tool_fails_the_compile()
    {
        var compiler = BuildCompiler(
            out var callable,
            new DelegateAgentExtension(
                "probe",
                static context => context.ChatAgentOptions!.ChatOptions!.Tools!.Add(AIFunctionFactory.Create(static () => "extra", "extra")),
                null));

        Should.Throw<TraconCompilationException>(() => compiler.Compile(ToolModeDefinition(), callable))
            .Message.ShouldContain("ChatOptions.Tools");
    }

    private static AgentDefinition ToolModeDefinition()
        => TestData.Definition() with
        {
            CallableAgents = ["support-agent"],
            SubAgents = new SubAgentSettings { CallMode = SubAgentCallMode.Tool },
            ExtensionNames = ["probe"],
        };

    private static AgentDefinitionCompiler BuildCompiler(out ResolvedCallableAgents callable, params IAgentExtension[] extensions)
    {
        var options = Options.Create(new TraconOptions());
        var catalog = new FakeAgentCatalog(
        [
            new AgentDescriptor { Name = "support-agent", Origin = AgentDefinitionOrigin.Database, SourceName = "database" },
        ]);

        callable = new ResolvedCallableAgents([new CallableAgentInfo("support-agent", "Helps.", 1)], "fingerprint");

        return new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(TestData.Tool("support_agent")),
            callableAgents: new CallableAgentResolver(catalog),
            tenantContext: new SingleTenantContext(options),
            agentExtensions: extensions);
    }
}
