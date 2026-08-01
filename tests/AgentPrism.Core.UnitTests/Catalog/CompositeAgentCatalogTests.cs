using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Catalog;

public sealed class CompositeAgentCatalogTests
{
    [Fact]
    public async Task Tum_kaynaklardaki_agentlar_birlestirilir()
    {
        var catalog = CreateCatalog(
            new StubSource("code", priority: 0, "alpha"),
            new StubSource("database", priority: 100, "beta"));

        var descriptors = await catalog.ListAsync();

        descriptors.Select(static d => d.Name).ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public async Task Ad_cakismasinda_oncelikli_kaynak_kazanir()
    {
        var catalog = CreateCatalog(
            new StubSource("database", priority: 100, "ortak"),
            new StubSource("code", priority: 0, "ortak"));

        var descriptor = (await catalog.ListAsync()).ShouldHaveSingleItem();

        descriptor.SourceName.ShouldBe("code");
    }

    [Fact]
    public async Task Cozum_oncelik_sirasina_gore_yapilir()
    {
        var code = new StubSource("code", priority: 0, "ortak");
        var database = new StubSource("database", priority: 100, "ortak");

        var catalog = CreateCatalog(database, code);

        (await catalog.ResolveAsync("ortak")).ShouldNotBeNull();

        code.ResolveCalls.ShouldBe(1);
        database.ResolveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Bulunamayan_agent_null_doner()
    {
        var catalog = CreateCatalog(new StubSource("code", priority: 0, "alpha"));

        (await catalog.ResolveAsync("yok")).ShouldBeNull();
    }

    [Fact]
    public async Task Cozulen_agent_dekoratorlerden_gecer()
    {
        var decorator = new CountingDecorator();
        var catalog = new CompositeAgentCatalog(
            [new StubSource("code", priority: 0, "alpha")],
            [decorator],
            NullLogger<CompositeAgentCatalog>.Instance);

        await catalog.ResolveAsync("alpha");

        decorator.DecorateCalls.ShouldBe(1);
        decorator.LastDescriptor.ShouldNotBeNull();
        decorator.LastDescriptor!.Name.ShouldBe("alpha");
    }

    [Fact]
    public async Task Bos_agent_adi_reddedilir()
    {
        var catalog = CreateCatalog(new StubSource("code", priority: 0, "alpha"));

        await Should.ThrowAsync<ArgumentException>(async () => await catalog.ResolveAsync("  "));
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

        public ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
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
