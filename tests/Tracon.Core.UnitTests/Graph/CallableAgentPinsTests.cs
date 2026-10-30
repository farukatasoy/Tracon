using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Graph;

/// <summary>
/// The rules a definition's <see cref="AgentDefinition.CallableAgents"/> entries must meet before the definition is
/// saved: a name appears once, a version is at least 1, the callee has that version and keeps a history.
/// </summary>
public sealed class CallableAgentPinsTests
{
    private const string TenantId = "tenant-a";

    [Fact]
    public async Task Entries_with_no_pin_raise_no_problem()
    {
        var problems = await FindAsync([Ref("a"), Ref("b")], Descriptors(("a", AgentDefinitionOrigin.Code), ("b", AgentDefinitionOrigin.Database)));

        problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_name_listed_twice_is_a_duplicate_at_the_second_entry()
    {
        var problems = await FindAsync([Ref("a"), Ref("a", 1)], Descriptors(("a", AgentDefinitionOrigin.Database)));

        var problem = problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe("callable_agent_duplicate");
        problem.Path.ShouldBe("callableAgents[1].name");
        problem.Message.ShouldContain("'a' more than once", Case.Sensitive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task A_version_below_one_is_invalid_even_when_the_agent_is_unknown(int version)
    {
        var problems = await FindAsync([Ref("a", version)], []);

        var problem = problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe("callable_agent_version_invalid");
        problem.Path.ShouldBe("callableAgents[0].version");
        problem.Message.ShouldContain($"version {version}", Case.Sensitive);
    }

    [Fact]
    public async Task A_pin_on_an_agent_defined_in_code_is_invalid()
    {
        var problems = await FindAsync([Ref("a", 1)], Descriptors(("a", AgentDefinitionOrigin.Code)));

        var problem = problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe("callable_agent_version_invalid");
        problem.Message.ShouldContain("keeps no version history", Case.Sensitive);
    }

    [Fact]
    public async Task A_version_the_stored_agent_does_not_have_is_not_found()
    {
        var store = await StoreWithAsync("a", versions: 2);

        var problems = await FindAsync([Ref("a", 3)], Descriptors(("a", AgentDefinitionOrigin.Database)), store);

        var problem = problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe("callable_agent_version_not_found");
        problem.Path.ShouldBe("callableAgents[0].version");
        problem.Message.ShouldContain("has no version 3", Case.Sensitive);
        problem.Message.ShouldContain("GET /api/agents/a/versions", Case.Sensitive);
    }

    [Fact]
    public async Task A_version_the_stored_agent_has_is_accepted()
    {
        var store = await StoreWithAsync("a", versions: 2);

        (await FindAsync([Ref("a", 2)], Descriptors(("a", AgentDefinitionOrigin.Database)), store)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_agent_is_left_to_the_call_graph_check()
    {
        (await FindAsync([Ref("missing", 4)], [])).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agent_of_a_custom_source_is_not_judged_here()
    {
        (await FindAsync([Ref("a", 4)], Descriptors(("a", AgentDefinitionOrigin.Custom)))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_problem_is_reported_in_list_order()
    {
        var store = await StoreWithAsync("a", versions: 1);

        var problems = await FindAsync(
            [Ref("a", 0), Ref("a", 1), Ref("b", 1), Ref("a", 7)],
            Descriptors(("a", AgentDefinitionOrigin.Database), ("b", AgentDefinitionOrigin.Code)),
            store);

        problems.Select(static problem => problem.Path).ShouldBe(
            ["callableAgents[0].version", "callableAgents[1].name", "callableAgents[2].version", "callableAgents[3].name"]);
    }

    private static CallableAgentReference Ref(string name, int? version = null)
        => new() { Name = name, Version = version };

    private static ValueTask<IReadOnlyList<CallableAgentPinProblem>> FindAsync(
        IReadOnlyList<CallableAgentReference> references,
        IReadOnlyList<AgentDescriptor> descriptors,
        IAgentDefinitionStore? store = null)
        => CallableAgentPins.FindProblemsAsync("caller", references, descriptors, store, CancellationToken.None);

    private static IReadOnlyList<AgentDescriptor> Descriptors(params (string Name, AgentDefinitionOrigin Origin)[] agents)
        => [.. agents.Select(static agent => new AgentDescriptor { Name = agent.Name, Origin = agent.Origin, SourceName = "test", Version = 1 })];

    private static async Task<InMemoryAgentDefinitionStore> StoreWithAsync(string name, int versions)
    {
        var store = new InMemoryAgentDefinitionStore(new FixedTenantContext(TenantId));

        for (var version = 0; version < versions; version++)
        {
            await store.SaveAsync(TestData.Definition(name) with { Instructions = $"text {version}" });
        }

        return store;
    }
}
