using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Catalog;

public sealed class CompositeAgentCatalogTests
{
    [Fact]
    public async Task Agents_from_all_sources_are_merged()
    {
        var catalog = CreateCatalog(
            new StubSource("code", priority: 0, "alpha"),
            new StubSource("database", priority: 100, "beta"));

        var descriptors = await catalog.ListAsync();

        descriptors.Select(static d => d.Name).ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public async Task Higher_priority_source_wins_on_a_name_collision()
    {
        var catalog = CreateCatalog(
            new StubSource("database", priority: 100, "shared"),
            new StubSource("code", priority: 0, "shared"));

        var descriptor = (await catalog.ListAsync()).ShouldHaveSingleItem();

        descriptor.SourceName.ShouldBe("code");
    }

    /// <summary>
    /// 101.9: <c>Enumerable.OrderBy</c> is a documented STABLE sort, so two sources sharing
    /// a priority tie-break on the order they were registered in DI — the order they were
    /// passed to the catalog here.
    /// </summary>
    [Fact]
    public async Task Equal_priority_ties_break_on_registration_order()
    {
        var catalog = CreateCatalog(
            new StubSource("first-registered", priority: 50, "shared"),
            new StubSource("second-registered", priority: 50, "shared"));

        var descriptor = (await catalog.ListAsync()).ShouldHaveSingleItem();

        descriptor.SourceName.ShouldBe("first-registered");
    }

    [Fact]
    public async Task Resolution_follows_priority_order()
    {
        var code = new StubSource("code", priority: 0, "shared");
        var database = new StubSource("database", priority: 100, "shared");

        var catalog = CreateCatalog(database, code);

        (await catalog.ResolveAsync("shared", culture: null, CancellationToken.None)).ShouldNotBeNull();

        code.ResolveCalls.ShouldBe(1);
        database.ResolveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Agent_not_found_returns_null()
    {
        var catalog = CreateCatalog(new StubSource("code", priority: 0, "alpha"));

        (await catalog.ResolveAsync("missing", culture: null, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Resolved_agent_passes_through_the_decorators()
    {
        var decorator = new CountingDecorator();
        var catalog = new CompositeAgentCatalog(
            [new StubSource("code", priority: 0, "alpha")],
            [decorator],
            NullLogger<CompositeAgentCatalog>.Instance);

        await catalog.ResolveAsync("alpha", culture: null, CancellationToken.None);

        decorator.DecorateCalls.ShouldBe(1);
        decorator.LastDescriptor.ShouldNotBeNull();
        decorator.LastDescriptor!.Name.ShouldBe("alpha");
    }

    [Fact]
    public async Task Empty_agent_name_is_rejected()
    {
        var catalog = CreateCatalog(new StubSource("code", priority: 0, "alpha"));

        await Should.ThrowAsync<ArgumentException>(async () => await catalog.ResolveAsync("  ", culture: null, CancellationToken.None));
    }

    // --- Version selection (Phase 19.3) ---

    [Fact]
    public async Task Correct_version_is_resolved_from_a_versioned_source()
    {
        var source = new VersionedStubSource("database", 100, "beta", 1, 2);
        var catalog = CreateCatalog(source);

        var agent = await catalog.ResolveAsync("beta", 2, culture: null, CancellationToken.None);

        agent.ShouldNotBeNull();
        source.LastRequestedVersion.ShouldBe(2);
    }

    [Fact]
    public async Task Latest_version_is_resolved_when_version_is_null()
    {
        var source = new VersionedStubSource("database", 100, "beta", 1, 2);
        var catalog = CreateCatalog(source);

        var agent = await catalog.ResolveAsync("beta", (int?)null, culture: null, CancellationToken.None);

        agent.ShouldNotBeNull();
    }

    [Fact]
    public async Task Requesting_a_version_on_a_code_sourced_agent_throws()
    {
        var catalog = CreateCatalog(new StubSource("code", priority: 0, "alpha"));

        await Should.ThrowAsync<AgentPrismException>(async () => await catalog.ResolveAsync("alpha", 1, culture: null, CancellationToken.None));
    }

    [Fact]
    public async Task Nonexistent_version_throws()
    {
        var source = new VersionedStubSource("database", 100, "beta", 1);
        var catalog = CreateCatalog(source);

        await Should.ThrowAsync<AgentPrismException>(async () => await catalog.ResolveAsync("beta", 99, culture: null, CancellationToken.None));
    }

    private static CompositeAgentCatalog CreateCatalog(params IAgentSource[] sources)
        => new(sources, [], NullLogger<CompositeAgentCatalog>.Instance);

    private sealed class StubSource : IAgentSource
    {
        private readonly string[] _agentNames;

        public StubSource(string name, int priority, params string[] agentNames)
        {
            Name = name;
            Priority = priority;
            _agentNames = agentNames;
        }

        public string Name { get; }

        public int Priority { get; }

        public int ResolveCalls { get; private set; }

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AgentDescriptor> descriptors =
            [
                .. _agentNames.Select(name => new AgentDescriptor
                {
                    Name = name,
                    Origin = AgentDefinitionOrigin.Code,
                    SourceName = Name,
                }),
            ];

            return new ValueTask<IReadOnlyList<AgentDescriptor>>(descriptors);
        }

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
        {
            if (!_agentNames.Contains(agentName, StringComparer.Ordinal))
            {
                return new ValueTask<AIAgent?>((AIAgent?)null);
            }

            ResolveCalls++;

            var compiler = new AgentDefinitionCompiler(
                TestData.Providers(new FakeModelProvider()),
                TestData.Registry());

            return new ValueTask<AIAgent?>(compiler.Compile(TestData.Definition(agentName)));
        }
    }

    private sealed class VersionedStubSource : IVersionedAgentSource
    {
        private readonly string _agentName;
        private readonly HashSet<int> _versions;

        public VersionedStubSource(string name, int priority, string agentName, params int[] versions)
        {
            Name = name;
            Priority = priority;
            _agentName = agentName;
            _versions = [.. versions];
        }

        public string Name { get; }

        public int Priority { get; }

        public int? LastRequestedVersion { get; private set; }

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor { Name = _agentName, Origin = AgentDefinitionOrigin.Database, SourceName = Name },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => ResolveVersionAsync(agentName, _versions.Max(), culture, cancellationToken);

        public ValueTask<AIAgent?> ResolveVersionAsync(string agentName, int version, string? culture = null, CancellationToken cancellationToken = default)
        {
            LastRequestedVersion = version;

            if (!string.Equals(agentName, _agentName, StringComparison.Ordinal) || !_versions.Contains(version))
            {
                return new ValueTask<AIAgent?>((AIAgent?)null);
            }

            var compiler = new AgentDefinitionCompiler(
                TestData.Providers(new FakeModelProvider()),
                TestData.Registry());

            return new ValueTask<AIAgent?>(compiler.Compile(TestData.Definition(agentName) with { Version = version }));
        }
    }

    private sealed class CountingDecorator : IAgentDecorator
    {
        public int Order => 0;

        public int DecorateCalls { get; private set; }

        public AgentDescriptor? LastDescriptor { get; private set; }

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
        {
            DecorateCalls++;
            LastDescriptor = descriptor;
            return agent;
        }
    }
}
