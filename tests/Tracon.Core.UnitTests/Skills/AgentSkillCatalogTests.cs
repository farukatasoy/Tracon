using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Skills;

public sealed class AgentSkillCatalogTests
{
    [Fact]
    public async Task Bilinmeyen_skill_adi_derlemeyi_durdurur()
    {
        var catalog = CreateCatalog();
        var definition = Definition(["missing-skill"]);

        var exception = await Should.ThrowAsync<TraconCompilationException>(
            async () => await catalog.ResolveAsync(definition, CancellationToken.None));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("missing-skill");
    }

    [Fact]
    public async Task Devre_disi_skill_yukleme_kaynaklarina_girmez()
    {
        var store = new InMemoryAgentSkillStore();
        await store.SaveAsync(Skill("tenant-a", "disabled") with { Enabled = false });
        var catalog = CreateCatalog(store);

        var enabled = await catalog.GetEnabledAsync(["disabled"], CancellationToken.None);

        enabled.ShouldBeEmpty();
    }

    [Fact]
    public async Task Kod_skilli_veritabani_skillinden_onceliklidir()
    {
        var store = new InMemoryAgentSkillStore();
        await store.SaveAsync(Skill("tenant-a", "invoice") with { Description = "Database skill" });
        var catalog = CreateCatalog(store, Skill("code", "invoice") with { Description = "Code skill" });

        var enabled = await catalog.GetEnabledAsync(["invoice"], CancellationToken.None);

        enabled.Single().Description.ShouldBe("Code skill");
    }

    private static AgentSkillCatalog CreateCatalog(
        IAgentSkillStore? store = null,
        params AgentSkillDefinition[] codeSkills)
        => new(
            codeSkills.Select(static skill => new CodeSkillRegistration(skill)),
            store ?? new InMemoryAgentSkillStore(),
            new FixedTenantContext("tenant-a"),
            Options.Create(new TraconOptions()));

    private static AgentDefinition Definition(IReadOnlyList<string> skillNames)
        => new()
        {
            Name = "test-agent",
            Model = new ModelBinding { Provider = "fake", Model = "fake-model" },
            SkillNames = skillNames,
        };

    private static AgentSkillDefinition Skill(string tenantId, string name)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            Description = "Skill description.",
            Instructions = "Skill instructions.",
        };

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }
}
