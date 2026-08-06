namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IAgentSkillStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Faz 41'de eklendi. Skill deposu bu tarihe kadar uc SQL saglayicisinda hic
/// karsilastirilmamisti; sozlesme hem davranis esitligini hem kiraci yalitimini
/// korur.
/// </remarks>
public abstract class AgentSkillStoreContract : TenantIsolationContract<IAgentSkillStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.SaveAsync(Skill(tenantId, name));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    [Fact]
    public async Task Kaydedilen_skill_geri_okunur()
    {
        var saved = await Store.SaveAsync(Skill("kiraci-a", "fatura"));

        saved.Id.ShouldNotBe(Guid.Empty);

        var loaded = await Store.GetAsync("kiraci-a", "fatura");

        loaded.ShouldNotBeNull();
        loaded.Description.ShouldBe("Fatura inceleme.");
        loaded.Instructions.ShouldBe("Her faturayi kontrol et.");
        loaded.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Ayni_ad_ikinci_kez_kaydedilince_surum_artar()
    {
        var first = await Store.SaveAsync(Skill("kiraci-a", "fatura"));
        var second = await Store.SaveAsync(Skill("kiraci-a", "fatura") with { Instructions = "guncellendi" });

        second.Id.ShouldBe(first.Id);
        second.Version.ShouldBeGreaterThan(first.Version);

        (await Store.GetAsync("kiraci-a", "fatura"))!.Instructions.ShouldBe("guncellendi");
        (await Store.ListAsync("kiraci-a")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Olmayan_skill_null_doner()
        => (await Store.GetAsync("kiraci-a", "yok")).ShouldBeNull();

    [Fact]
    public async Task Silme_olmayan_kayitta_false_doner()
        => (await Store.DeleteAsync("kiraci-a", "yok")).ShouldBeFalse();

    [Fact]
    public async Task Liste_ada_gore_siralanir()
    {
        await Store.SaveAsync(Skill("kiraci-a", "zeta"));
        await Store.SaveAsync(Skill("kiraci-a", "alfa"));

        var all = await Store.ListAsync("kiraci-a");

        all.Count.ShouldBe(2);
        all[0].Name.ShouldBe("alfa");
    }

    [Fact]
    public async Task Kapatilan_skill_kapali_okunur()
    {
        await Store.SaveAsync(Skill("kiraci-a", "fatura") with { Enabled = false });

        (await Store.GetAsync("kiraci-a", "fatura"))!.Enabled.ShouldBeFalse();
    }

    private static AgentSkillDefinition Skill(string tenantId, string name)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            Description = "Fatura inceleme.",
            Instructions = "Her faturayi kontrol et.",
        };
}
