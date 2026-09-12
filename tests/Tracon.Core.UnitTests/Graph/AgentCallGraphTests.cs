namespace Tracon.Core.UnitTests.Graph;

/// <summary>
/// The static cycle check performed at save time.
/// </summary>
/// <remarks>
/// The check cannot be left to run time: a cyclic graph is only noticed
/// after the depth counter runs out, and by that point tokens are already spent.
/// </remarks>
public sealed class AgentCallGraphTests
{
    [Fact]
    public void Empty_list_is_valid()
        => AgentCallGraph.Validate("a", [], Descriptors()).ShouldBeNull();

    [Fact]
    public void Calling_itself_is_rejected()
    {
        var problem = AgentCallGraph.Validate("a", ["a"], Descriptors(("a", [])));

        problem.ShouldNotBeNull();
        problem.ShouldContain("cannot call itself", Case.Sensitive);
    }

    [Fact]
    public void Unknown_name_is_rejected()
    {
        var problem = AgentCallGraph.Validate("a", ["none"], Descriptors(("a", [])));

        problem.ShouldNotBeNull();
        problem.ShouldContain("no such agent exists in the catalog", Case.Sensitive);
    }

    [Fact]
    public void Indirect_cycle_is_rejected()
    {
        // The chain b -> c -> a already exists in the catalog; adding a -> b closes the cycle.
        var descriptors = Descriptors(("a", []), ("b", ["c"]), ("c", ["a"]));

        var problem = AgentCallGraph.Validate("a", ["b"], descriptors);

        problem.ShouldNotBeNull();
        problem.ShouldContain("cycle", Case.Sensitive);
        problem.ShouldContain("a -> b -> c -> a", Case.Sensitive);
    }

    [Fact]
    public void Acyclic_deep_chain_is_accepted()
    {
        var descriptors = Descriptors(("a", []), ("b", ["c"]), ("c", ["d"]), ("d", []));

        AgentCallGraph.Validate("a", ["b"], descriptors).ShouldBeNull();
    }

    [Fact]
    public void Diamond_shaped_graph_is_not_counted_as_a_cycle()
    {
        // a -> b, a -> c, both call d. Reaching the same node by two paths is
        // NOT a cycle; an algorithm that mistakes a visited node for a cycle
        // would reject this valid graph.
        var descriptors = Descriptors(("a", []), ("b", ["d"]), ("c", ["d"]), ("d", []));

        AgentCallGraph.Validate("a", ["b", "c"], descriptors).ShouldBeNull();
    }

    [Fact]
    public void The_new_list_is_checked_not_the_catalogs_stale_state()
    {
        // In the catalog, "a" does not yet call any agent. If the check used
        // the catalog's stale state, the newly added edge would never be
        // seen and the cycle would slip through.
        var descriptors = Descriptors(("a", []), ("b", ["a"]));

        AgentCallGraph.Validate("a", ["b"], descriptors).ShouldNotBeNull();
    }

    [Fact]
    public void Very_long_chain_does_not_cause_a_stack_overflow()
    {
        // The call graph is user data; a recursive traversal would kill the
        // process on a sufficiently long chain.
        var chain = new List<AgentDescriptor>();

        for (var index = 0; index < 20_000; index++)
        {
            chain.Add(new AgentDescriptor
            {
                Name = $"n{index}",
                Origin = AgentDefinitionOrigin.Database,
                SourceName = "database",
                CallableAgentNames = index + 1 < 20_000 ? [$"n{index + 1}"] : [],
            });
        }

        AgentCallGraph.Validate("root", ["n0"], chain).ShouldBeNull();
    }

    private static AgentDescriptor[] Descriptors(params (string Name, string[] Calls)[] entries)
        => [.. entries.Select(static entry => new AgentDescriptor
        {
            Name = entry.Name,
            Origin = AgentDefinitionOrigin.Database,
            SourceName = "database",
            CallableAgentNames = entry.Calls,
        })];
}
