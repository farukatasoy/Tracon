using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The skill chain is built in one place for both agent types. Whatever the agent
/// is, a compiled agent that outlives a tenant switch lists the current tenant's
/// skill and offers the skill tool once (F-578, Phase 219).
/// </summary>
/// <remarks>
/// A compiled agent is cached per tenant by <see cref="CompiledAgentCache"/>, so no
/// ordinary path hands one agent to two tenants. The chain's own cache is a second
/// line: it keys the skill list by tenant too, and this is the test that proves it.
/// </remarks>
public sealed class SkillChainTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string SkillName = "tone";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_compiled_agent_lists_each_tenants_own_skill(bool harness)
    {
        var tenant = new MutableTenantContext(TenantA);
        var store = new InMemoryAgentSkillStore();
        await store.SaveAsync(Skill(TenantA, "Tone of tenant A only."));
        await store.SaveAsync(Skill(TenantB, "Tone of tenant B only."));
        var client = new FakeChatClient();
        var agent = CreateCompiler(client, store, tenant).Compile(Definition(harness));

        var first = await PromptAsync(agent, client);

        tenant.TenantId = TenantB;
        var second = await PromptAsync(agent, client);

        first.ShouldContain("Tone of tenant A only.");
        first.ShouldNotContain("Tone of tenant B only.");
        second.ShouldContain("Tone of tenant B only.");
        second.ShouldNotContain("Tone of tenant A only.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_compiled_agent_offers_the_skill_tool_once(bool harness)
    {
        var store = new InMemoryAgentSkillStore();
        await store.SaveAsync(Skill(TenantA, "Tone of tenant A only."));
        var client = new FakeChatClient();
        var agent = CreateCompiler(client, store, new MutableTenantContext(TenantA)).Compile(Definition(harness));

        await PromptAsync(agent, client);

        (client.LastOptions?.Tools ?? [])
            .Count(static tool => string.Equals(tool.Name, AgentSkillsProvider.LoadSkillToolName, StringComparison.Ordinal))
            .ShouldBe(1);
    }

    private static AgentDefinitionCompiler CreateCompiler(FakeChatClient client, InMemoryAgentSkillStore store, MutableTenantContext tenant)
        => new(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry(),
            skills: new AgentSkillCatalog([], store, tenant, Options.Create(new TraconOptions())));

    private static AgentDefinition Definition(bool harness)
        => TestData.Definition(harness: harness ? new HarnessSettings() : null) with { SkillNames = [SkillName] };

    private static AgentSkillDefinition Skill(string tenantId, string description)
        => new()
        {
            TenantId = tenantId,
            Name = SkillName,
            Description = description,
            Instructions = "Follow the tone.",
        };

    /// <summary>Runs the agent once and returns everything the model was shown: messages and instructions.</summary>
    private static async Task<string> PromptAsync(AIAgent agent, FakeChatClient client)
    {
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        await agent.RunAsync("Hello.", session, cancellationToken: TestContext.Current.CancellationToken);

        return string.Join('\n', client.LastRequest.Select(static message => message.Text)) + '\n' + client.LastOptions?.Instructions;
    }

    /// <summary>A tenant context a test can switch between two runs of one agent.</summary>
    private sealed class MutableTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; set; } = tenantId;
    }
}
