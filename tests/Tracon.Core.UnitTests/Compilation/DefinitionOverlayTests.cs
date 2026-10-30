using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The overlay is a read-only view: the package's agents are laid over what the target holds, and nothing the overlay
/// answers can be written or resolved into a catalog.
/// </summary>
public sealed class DefinitionOverlayTests
{
    [Fact]
    public void A_stored_agent_of_the_package_replaces_the_targets_and_a_new_one_is_added()
    {
        var overlay = new DefinitionOverlay([Agent("kept", version: 5), Agent("fresh", version: 1)], []);
        var catalog = new[] { Descriptor("kept", AgentDefinitionOrigin.Database, version: 2), Descriptor("other", AgentDefinitionOrigin.Database, version: 1) };

        var merged = overlay.MergeDescriptors(catalog);

        merged.Select(static descriptor => $"{descriptor.Name}@{descriptor.Version}").Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(["fresh@1", "kept@5", "other@1"]);
    }

    [Fact]
    public void An_agent_the_target_defines_in_code_is_not_replaced_because_code_wins_the_name()
    {
        var overlay = new DefinitionOverlay([Agent("code-agent", version: 9)], []);

        var merged = overlay.MergeDescriptors([Descriptor("code-agent", AgentDefinitionOrigin.Code, version: 1)]);

        var kept = merged.ShouldHaveSingleItem();

        kept.Origin.ShouldBe(AgentDefinitionOrigin.Code);
        kept.Version.ShouldBe(1);
    }

    [Fact]
    public async Task The_store_adapter_reads_the_package_first_and_the_version_it_will_have()
    {
        var inner = new InMemoryAgentDefinitionStore();

        await inner.SaveAsync(Agent("a", instructions: "old"));

        var overlay = new DefinitionOverlay([Agent("a", instructions: "new", version: 2)], []);
        var store = new OverlayAgentDefinitionStore(overlay, inner);

        (await store.GetAsync("a"))!.Instructions.ShouldBe("new");
        (await store.GetVersionAsync("a", 2))!.Instructions.ShouldBe("new");

        // The version the target already has is still the target's.
        (await store.GetVersionAsync("a", 1))!.Instructions.ShouldBe("old");
        (await store.GetVersionAsync("a", 3)).ShouldBeNull();
    }

    [Fact]
    public async Task The_adapters_refuse_to_write_or_resolve()
    {
        var overlay = DefinitionOverlay.Empty;
        var store = new OverlayAgentDefinitionStore(overlay, new InMemoryAgentDefinitionStore());
        var catalog = new OverlayAgentCatalog(overlay, new EmptyCatalog());

        await Should.ThrowAsync<NotSupportedException>(async () => await store.SaveAsync(Agent("a")));
        await Should.ThrowAsync<NotSupportedException>(async () => await store.DeleteAsync("a"));
        await Should.ThrowAsync<NotSupportedException>(async () => await store.RollbackAsync("a", 1));
        await Should.ThrowAsync<NotSupportedException>(async () => await catalog.ResolveAsync("a", culture: null, CancellationToken.None));
        await Should.ThrowAsync<NotSupportedException>(async () => await catalog.ResolveAsync("a", version: 1));
    }

    [Fact]
    public void The_overlay_knows_the_skills_of_the_package()
    {
        var overlay = new DefinitionOverlay([], ["policy"]);

        overlay.HasSkill("policy").ShouldBeTrue();
        overlay.HasSkill("other").ShouldBeFalse();
    }

    private static AgentDefinition Agent(string name, int version = 1, string instructions = "Reply.")
        => TestData.Definition() with { Name = name, Version = version, Instructions = instructions, Origin = AgentDefinitionOrigin.Database };

    private static AgentDescriptor Descriptor(string name, AgentDefinitionOrigin origin, int version)
        => new() { Name = name, Origin = origin, SourceName = "test", Version = version };

    private sealed class EmptyCatalog : IAgentCatalog
    {
        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<AgentDescriptor>>([]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => ValueTask.FromResult<AIAgent?>(null);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AIAgent?>(null);
    }
}
