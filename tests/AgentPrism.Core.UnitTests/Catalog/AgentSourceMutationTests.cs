using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Catalog;

/// <summary>
/// Proves the catalog boundary's defensive copy (101.6): even when a source hands back a
/// descriptor backed by mutable collections — a contract violation on the source's part,
/// see the <c>AgentDescriptor</c> remark — a caller that already holds a descriptor from
/// <see cref="CompositeAgentCatalog"/> is unaffected by the source mutating its own
/// backing collections afterward.
/// </summary>
public sealed class AgentSourceMutationTests
{
    [Fact]
    public async Task Mutating_the_sources_backing_collections_after_ListAsync_does_not_change_the_returned_descriptor()
    {
        var source = new MutableSource();
        var catalog = new CompositeAgentCatalog([source], [], NullLogger<CompositeAgentCatalog>.Instance);

        var descriptor = (await catalog.ListAsync()).ShouldHaveSingleItem();

        source.Mutate();

        descriptor.ToolNames.ShouldBe(["initial-tool"]);
        descriptor.SkillNames.ShouldBe(["initial-skill"]);
        descriptor.CallableAgentNames.ShouldBe(["initial-agent"]);
        descriptor.Model!.ProviderSettings.Keys.ShouldBe(["initial-key"]);
        descriptor.Model!.Fallbacks.Select(static f => f.Provider).ShouldBe(["initial-fallback"]);
    }

    [Fact]
    public async Task Mutating_the_sources_backing_collections_after_ResolveAsync_does_not_change_the_decorated_descriptor()
    {
        var source = new MutableSource();
        var decorator = new SpyDecorator();
        var catalog = new CompositeAgentCatalog([source], [decorator], NullLogger<CompositeAgentCatalog>.Instance);

        await catalog.ResolveAsync(MutableSource.AgentName, culture: null, CancellationToken.None);
        var descriptor = decorator.LastDescriptor.ShouldNotBeNull();

        source.Mutate();

        descriptor.ToolNames.ShouldBe(["initial-tool"]);
        descriptor.SkillNames.ShouldBe(["initial-skill"]);
        descriptor.CallableAgentNames.ShouldBe(["initial-agent"]);
        descriptor.Model!.ProviderSettings.Keys.ShouldBe(["initial-key"]);
        descriptor.Model!.Fallbacks.Select(static f => f.Provider).ShouldBe(["initial-fallback"]);
    }

    /// <summary>An <see cref="IAgentSource"/> whose descriptor is backed by mutable collections it keeps a handle to.</summary>
    private sealed class MutableSource : IAgentSource
    {
        public const string AgentName = "agent";

        private readonly List<string> _toolNames = ["initial-tool"];
        private readonly List<string> _skillNames = ["initial-skill"];
        private readonly List<string> _callableAgentNames = ["initial-agent"];
        private readonly Dictionary<string, JsonElement> _providerSettings =
            new(StringComparer.OrdinalIgnoreCase) { ["initial-key"] = JsonDocument.Parse("\"v\"").RootElement };
        private readonly List<ModelFallback> _fallbacks = [new ModelFallback { Provider = "initial-fallback", Model = "initial-model" }];

        public string Name => "mutable";

        public int Priority => 0;

        /// <summary>Mutates every backing collection the last-returned descriptor was built from.</summary>
        public void Mutate()
        {
            _toolNames.Add("added-tool");
            _skillNames.Add("added-skill");
            _callableAgentNames.Add("added-agent");
            _providerSettings["added-key"] = JsonDocument.Parse("\"v2\"").RootElement;
            _fallbacks.Add(new ModelFallback { Provider = "added-fallback", Model = "added-model" });
        }

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor
                {
                    Name = AgentName,
                    Origin = AgentDefinitionOrigin.Custom,
                    SourceName = Name,
                    ToolNames = _toolNames,
                    SkillNames = _skillNames,
                    CallableAgentNames = _callableAgentNames,
                    Model = new ModelBinding
                    {
                        Provider = "fake",
                        Model = "fake-model",
                        ProviderSettings = _providerSettings,
                        Fallbacks = _fallbacks,
                    },
                },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(agentName, AgentName, StringComparison.Ordinal))
            {
                return new ValueTask<AIAgent?>((AIAgent?)null);
            }

            var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
            return new ValueTask<AIAgent?>(compiler.Compile(TestData.Definition(AgentName)));
        }
    }

    private sealed class SpyDecorator : IAgentDecorator
    {
        public int Order => 0;

        public AgentDescriptor? LastDescriptor { get; private set; }

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
        {
            LastDescriptor = descriptor;
            return agent;
        }
    }
}
