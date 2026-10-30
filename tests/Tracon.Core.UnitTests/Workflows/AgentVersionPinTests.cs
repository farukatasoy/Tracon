using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Workflows;

/// <summary>
/// <see cref="AgentVersionPin"/> judges a definition's agent version pins against the agents
/// that exist: it refuses a pin to an agent defined in code and to a version a stored
/// agent does not have, and it leaves everything else to the compiler.
/// </summary>
public sealed class AgentVersionPinTests
{
    [Fact]
    public async Task A_definition_with_no_pin_never_reads_the_catalog()
    {
        var catalog = new CountingCatalog([Stored("writer")]);

        var problem = await AgentVersionPin.FindProblemAsync(
            Chain(new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent }),
            catalog,
            new InMemoryAgentDefinitionStore(),
            CancellationToken.None);

        problem.ShouldBeNull();
        catalog.Lists.ShouldBe(0, "a workflow without a pin pays nothing for the check");
    }

    [Fact]
    public async Task A_pin_to_an_agent_defined_in_code_is_refused_by_node_and_agent()
    {
        var problem = await AgentVersionPin.FindProblemAsync(
            Chain(Pinned("coded", 1)),
            new CountingCatalog([Described("coded", AgentDefinitionOrigin.Code, "code")]),
            new InMemoryAgentDefinitionStore(),
            CancellationToken.None);

        problem.ShouldNotBeNull();
        problem.ShouldContain("Node 'coded' in workflow 'chain' pins agent 'coded' to version 1", Case.Sensitive);
        problem.ShouldContain("defined in code and keeps no version history", Case.Sensitive);
    }

    [Fact]
    public async Task A_pin_to_a_version_a_stored_agent_does_not_have_is_refused_and_one_it_has_is_not()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(Agent("writer", "one"));
        await store.SaveAsync(Agent("writer", "two"));

        var catalog = new CountingCatalog([Stored("writer")]);

        (await AgentVersionPin.FindProblemAsync(Chain(Pinned("writer", 2)), catalog, store, CancellationToken.None)).ShouldBeNull();

        var problem = await AgentVersionPin.FindProblemAsync(Chain(Pinned("writer", 3)), catalog, store, CancellationToken.None);

        problem.ShouldNotBeNull();
        problem.ShouldContain("the agent has no version 3", Case.Sensitive);
        problem.ShouldContain("GET /api/agents/writer/versions", Case.Sensitive);
    }

    [Fact]
    public async Task An_agent_the_catalog_does_not_list_is_left_to_the_compiler()
    {
        var problem = await AgentVersionPin.FindProblemAsync(
            Chain(Pinned("later", 4)),
            new CountingCatalog([]),
            new InMemoryAgentDefinitionStore(),
            CancellationToken.None);

        problem.ShouldBeNull();
    }

    [Fact]
    public async Task An_agent_from_a_custom_source_is_left_to_the_catalog_that_resolves_it()
    {
        var problem = await AgentVersionPin.FindProblemAsync(
            Chain(Pinned("plugin", 4)),
            new CountingCatalog([Described("plugin", AgentDefinitionOrigin.Custom, "plugin-source")]),
            new InMemoryAgentDefinitionStore(),
            CancellationToken.None);

        problem.ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_store_the_existence_of_a_version_is_not_judged()
    {
        var problem = await AgentVersionPin.FindProblemAsync(
            Chain(Pinned("writer", 9)),
            new CountingCatalog([Stored("writer")]),
            agentDefinitions: null,
            CancellationToken.None);

        problem.ShouldBeNull();
    }

    [Fact]
    public async Task The_first_failing_pin_is_the_one_named()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(Agent("writer", "one"));

        var problem = await AgentVersionPin.FindProblemAsync(
            Chain(Pinned("writer", 1), Pinned("editor", 2), Pinned("mixer", 8)),
            new CountingCatalog([Stored("writer"), Stored("editor"), Stored("mixer")]),
            store,
            CancellationToken.None);

        problem.ShouldNotBeNull().ShouldContain("Node 'editor'", Case.Sensitive);
    }

    private static WorkflowDefinition Chain(params WorkflowNodeReference[] nodes)
        => new() { Name = "chain", Kind = WorkflowKind.Sequential, Nodes = nodes };

    private static WorkflowNodeReference Pinned(string name, int version)
        => new() { Name = name, Kind = WorkflowNodeKind.Agent, AgentVersion = version };

    private static AgentDefinition Agent(string name, string instructions)
        => new()
        {
            Name = name,
            Instructions = instructions,
            Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
        };

    private static AgentDescriptor Stored(string name) => Described(name, AgentDefinitionOrigin.Database, "database");

    private static AgentDescriptor Described(string name, AgentDefinitionOrigin origin, string source)
        => new() { Name = name, Origin = origin, SourceName = source };

    private sealed class CountingCatalog(IReadOnlyList<AgentDescriptor> descriptors) : IAgentCatalog
    {
        public int Lists { get; private set; }

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        {
            Lists++;

            return new ValueTask<IReadOnlyList<AgentDescriptor>>(descriptors);
        }

        public ValueTask<Microsoft.Agents.AI.AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => throw new NotSupportedException("the pin check never compiles an agent");

        public ValueTask<Microsoft.Agents.AI.AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("the pin check never compiles an agent");
    }
}
