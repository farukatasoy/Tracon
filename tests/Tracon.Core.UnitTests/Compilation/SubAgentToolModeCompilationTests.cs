using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The compiler refuses a <see cref="SubAgentCallMode.Tool"/> mode definition whose tool names
/// collide, the same refusal the validator reports (Phase 228). A definition written in code
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

    private static AgentDefinitionCompiler BuildCompiler(out ResolvedCallableAgents callable)
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
            tenantContext: new SingleTenantContext(options));
    }
}
