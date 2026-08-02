namespace AgentPrism.Core.UnitTests.Storage;

public sealed class InMemoryAgentSkillStoreTests
{
    [Fact]
    public async Task Kayit_kimlik_zaman_damgasi_ve_surumu_atar()
    {
        var store = new InMemoryAgentSkillStore();

        var saved = await store.SaveAsync(CreateSkill("tenant-a", "invoicing"));

        saved.Id.ShouldNotBe(Guid.Empty);
        saved.Version.ShouldBe(1);
        saved.CreatedAt.ShouldNotBe(default);
        saved.UpdatedAt.ShouldBe(saved.CreatedAt);
    }

    [Fact]
    public async Task Guncelleme_kimligi_ve_olusturma_zamanini_korur()
    {
        var store = new InMemoryAgentSkillStore();
        var first = await store.SaveAsync(CreateSkill("tenant-a", "invoicing"));

        var updated = await store.SaveAsync(first with { Description = "Yeni aciklama" });

        updated.Id.ShouldBe(first.Id);
        updated.CreatedAt.ShouldBe(first.CreatedAt);
        updated.Version.ShouldBe(2);
        updated.Description.ShouldBe("Yeni aciklama");
    }

    [Fact]
    public async Task Listeleme_kiraciya_ait_ve_ada_gore_siralidir()
    {
        var store = new InMemoryAgentSkillStore();
        await store.SaveAsync(CreateSkill("tenant-a", "zeta"));
        await store.SaveAsync(CreateSkill("tenant-a", "alpha"));
        await store.SaveAsync(CreateSkill("tenant-b", "hidden"));

        var skills = await store.ListAsync("tenant-a");

        skills.Select(static skill => skill.Name).ShouldBe(["alpha", "zeta"]);
    }

    [Fact]
    public async Task Silme_yalnizca_hedef_kiracinin_skillini_kaldirir()
    {
        var store = new InMemoryAgentSkillStore();
        await store.SaveAsync(CreateSkill("tenant-a", "invoicing"));
        await store.SaveAsync(CreateSkill("tenant-b", "invoicing"));

        (await store.DeleteAsync("tenant-a", "invoicing")).ShouldBeTrue();

        (await store.GetAsync("tenant-a", "invoicing")).ShouldBeNull();
        (await store.GetAsync("tenant-b", "invoicing")).ShouldNotBeNull();
    }

    private static AgentSkillDefinition CreateSkill(string tenantId, string name)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            Description = "Fatura analizi yapar.",
            Instructions = "Faturalari dikkatle incele.",
        };
}
