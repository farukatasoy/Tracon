using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The definition decides whether the two skill tools that read text ask for approval; the
/// tool that runs a script always asks.
/// </summary>
/// <remarks>
/// Microsoft Agent Framework wraps all three skill tools for approval by default. The proof is
/// the tool list the model receives from a compiled agent: a tool the framework wrapped
/// carries <see cref="ApprovalRequiredAIFunction"/>, and the function-invoking client
/// reads exactly that to decide whether to stop the run. Both agent types are covered
/// because they share one provider chain and one of them could still drop the setting.
/// </remarks>
public sealed class SkillReadApprovalTests
{
    private const string SkillName = "tone";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task The_definition_decides_whether_the_read_tools_ask_and_the_script_tool_always_asks(
        bool harness,
        bool disableReadApproval)
    {
        var skills = new SkillSettings { DisableReadApproval = disableReadApproval };

        var tools = await OfferedToolsAsync(harness, skills);

        Asks(tools, AgentSkillsProvider.LoadSkillToolName).ShouldBe(!disableReadApproval);
        Asks(tools, "read_skill_resource").ShouldBe(!disableReadApproval);
        Asks(tools, "run_skill_script").ShouldBeTrue("no member of the definition turns the script approval off");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_agent_without_skill_settings_keeps_the_approval_on_for_all_three_tools(bool harness)
    {
        var tools = await OfferedToolsAsync(harness, skills: null);

        Asks(tools, AgentSkillsProvider.LoadSkillToolName).ShouldBeTrue();
        Asks(tools, "read_skill_resource").ShouldBeTrue();
        Asks(tools, "run_skill_script").ShouldBeTrue();
    }

    /// <summary>Compiles an agent that names one skill, runs it once, and returns the tools the model was offered.</summary>
    private static async Task<IList<AITool>> OfferedToolsAsync(bool harness, SkillSettings? skills)
    {
        var store = new InMemoryAgentSkillStore();
        await store.SaveAsync(new AgentSkillDefinition
        {
            TenantId = "default",
            Name = SkillName,
            Description = "Tone of the replies.",
            Instructions = "Follow the tone.",
        });

        var client = new FakeChatClient();
        var options = Options.Create(new TraconOptions());
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry(),
            skills: new AgentSkillCatalog([], store, new SingleTenantContext(options), options));

        var definition = TestData.Definition(harness: harness ? new HarnessSettings() : null) with
        {
            SkillNames = [SkillName],
            Skills = skills,
        };

        var agent = compiler.Compile(definition);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        await agent.RunAsync("Hello.", session, cancellationToken: TestContext.Current.CancellationToken);

        return client.LastOptions?.Tools ?? [];
    }

    private static bool Asks(IList<AITool> tools, string name)
    {
        var tool = tools.SingleOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

        tool.ShouldNotBeNull($"the model was not offered '{name}'");

        return tool.GetService<ApprovalRequiredAIFunction>() is not null;
    }
}
