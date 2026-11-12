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
    public async Task Empty_list_is_valid()
        => (await Validate("a", Array.Empty<string>(), Descriptors())).ShouldBeNull();

    [Fact]
    public async Task Calling_itself_is_rejected()
    {
        var problem = await Validate("a", ["a"], Descriptors(("a", [])));

        problem.ShouldNotBeNull();
        problem.ShouldContain("cannot call itself", Case.Sensitive);
    }

    [Fact]
    public async Task Unknown_name_is_rejected()
    {
        var problem = await Validate("a", ["none"], Descriptors(("a", [])));

        problem.ShouldNotBeNull();
        problem.ShouldContain("no such agent exists in the catalog", Case.Sensitive);
    }

    [Fact]
    public async Task Indirect_cycle_is_rejected()
    {
        // The chain b -> c -> a already exists in the catalog; adding a -> b closes the cycle.
        var descriptors = Descriptors(("a", []), ("b", ["c"]), ("c", ["a"]));

        var problem = await Validate("a", ["b"], descriptors);

        problem.ShouldNotBeNull();
        problem.ShouldContain("cycle", Case.Sensitive);
        problem.ShouldContain("a -> b -> c -> a", Case.Sensitive);
    }

    [Fact]
    public async Task Acyclic_deep_chain_is_accepted()
    {
        var descriptors = Descriptors(("a", []), ("b", ["c"]), ("c", ["d"]), ("d", []));

        (await Validate("a", ["b"], descriptors)).ShouldBeNull();
    }

    [Fact]
    public async Task Diamond_shaped_graph_is_not_counted_as_a_cycle()
    {
        // a -> b, a -> c, both call d. Reaching the same node by two paths is
        // NOT a cycle; an algorithm that mistakes a visited node for a cycle
        // would reject this valid graph.
        var descriptors = Descriptors(("a", []), ("b", ["d"]), ("c", ["d"]), ("d", []));

        (await Validate("a", ["b", "c"], descriptors)).ShouldBeNull();
    }

    [Fact]
    public async Task The_new_list_is_checked_not_the_catalogs_stale_state()
    {
        // In the catalog, "a" does not yet call any agent. If the check used
        // the catalog's stale state, the newly added edge would never be
        // seen and the cycle would slip through.
        var descriptors = Descriptors(("a", []), ("b", ["a"]));

        (await Validate("a", ["b"], descriptors)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Very_long_chain_does_not_cause_a_stack_overflow()
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

        (await Validate("root", ["n0"], chain)).ShouldBeNull();
    }

    [Fact]
    public async Task A_cycle_that_exists_only_through_a_pinned_version_is_rejected()
    {
        // b's version 1 calls a; its current version calls nobody. a -> b@1 -> a runs until the depth limit,
        // and the descriptor (the current version) shows no cycle.
        var store = new InMemoryAgentDefinitionStore();
        await SaveAsync(store, "a");
        await SaveAsync(store, "b", "a");
        await SaveAsync(store, "b");

        var problem = await Validate("a", [Pinned("b", 1)], Descriptors(("a", []), ("b", [])), store);

        problem.ShouldNotBeNull();
        problem.ShouldContain("cycle", Case.Sensitive);
        problem.ShouldContain("a -> b@1 -> a", Case.Sensitive);
    }

    [Fact]
    public async Task A_cycle_that_exists_only_through_the_current_version_is_not_reported_for_a_pin()
    {
        // The reverse: b's version 1 calls nobody and its current version calls a. Pinned to version 1, a -> b@1 ends
        // there; the name-based graph reported a cycle that cannot run.
        var store = new InMemoryAgentDefinitionStore();
        await SaveAsync(store, "a");
        await SaveAsync(store, "b");
        await SaveAsync(store, "b", "a");

        var descriptors = Descriptors(("a", []), ("b", ["a"]));

        (await Validate("a", [Pinned("b", 1)], descriptors, store)).ShouldBeNull();

        // The control: the same graph without the pin follows the current version and finds the cycle.
        (await Validate("a", ["b"], descriptors, store)).ShouldNotBeNull().ShouldContain("a -> b -> a", Case.Sensitive);
    }

    [Fact]
    public async Task The_pins_of_a_current_callee_are_followed_too()
    {
        // a -> b (current) and b's current version pins c@1, whose version 1 calls a. The descriptor of b names c and
        // nothing more; the pin lives in the stored definition.
        var store = new InMemoryAgentDefinitionStore();
        await SaveAsync(store, "a");
        await SaveAsync(store, "c", "a");
        await SaveAsync(store, "c");
        await store.SaveAsync(Definition("b", Pinned("c", 1)));

        var problem = await Validate("a", ["b"], Descriptors(("a", []), ("b", ["c"]), ("c", [])), store);

        problem.ShouldNotBeNull();
        problem.ShouldContain("a -> b -> c@1 -> a", Case.Sensitive);
    }

    [Fact]
    public async Task A_pinned_version_that_does_not_exist_ends_the_path_and_is_the_pin_checks_finding()
    {
        var store = new InMemoryAgentDefinitionStore();
        await SaveAsync(store, "a");
        await SaveAsync(store, "b");

        (await Validate("a", [Pinned("b", 9)], Descriptors(("a", []), ("b", [])), store)).ShouldBeNull();
    }

    [Fact]
    public async Task A_host_without_a_store_follows_the_names_the_catalog_reports()
    {
        var descriptors = Descriptors(("a", []), ("b", ["a"]));

        (await Validate("a", ["b"], descriptors)).ShouldNotBeNull();
        (await Validate("a", [Pinned("b", 1)], descriptors)).ShouldBeNull();
    }

    private static async ValueTask<string?> Validate(
        string agentName,
        string[] calls,
        IReadOnlyList<AgentDescriptor> descriptors,
        IAgentDefinitionStore? store = null)
        => await Validate(agentName, [.. calls.Select(static name => new CallableAgentReference { Name = name })], descriptors, store);

    private static async ValueTask<string?> Validate(
        string agentName,
        CallableAgentReference[] calls,
        IReadOnlyList<AgentDescriptor> descriptors,
        IAgentDefinitionStore? store = null)
        => (await AgentCallGraph.ValidateAsync(agentName, calls, descriptors, store, CancellationToken.None)
            .ConfigureAwait(false))?.Message;

    private static CallableAgentReference Pinned(string name, int version) => new() { Name = name, Version = version };

    private static AgentDefinition Definition(string name, params CallableAgentReference[] calls)
        => new()
        {
            Name = name,
            Instructions = "Delegate.",
            Model = new ModelBinding { Provider = "fake", Model = "fake-model" },
            CallableAgents = calls,
        };

    private static ValueTask<AgentDefinition> SaveAsync(InMemoryAgentDefinitionStore store, string name, params string[] calls)
        => store.SaveAsync(Definition(name, [.. calls.Select(static callee => new CallableAgentReference { Name = callee })]));

    private static AgentDescriptor[] Descriptors(params (string Name, string[] Calls)[] entries)
        => [.. entries.Select(static entry => new AgentDescriptor
        {
            Name = entry.Name,
            Origin = AgentDefinitionOrigin.Database,
            SourceName = "database",
            CallableAgentNames = entry.Calls,
        })];
}
