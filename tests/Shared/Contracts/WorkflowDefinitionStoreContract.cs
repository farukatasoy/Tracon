namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IWorkflowDefinitionStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bellek ici depo ile PostgreSQL deposu ayni senaryolari gecmelidir. Ozellikle
/// surum artisi ve kiraci yalitimi iki uygulamada da ayni davranmalidir.
/// </remarks>
public abstract class WorkflowDefinitionStoreContract : TenantIsolationContract<IWorkflowDefinitionStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.SaveAsync(tenantId, Definition() with { Name = name });
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
    public async Task Kaydedilen_tanim_geri_okunur()
    {
        var saved = await Store.SaveAsync("tenant-a", Definition());

        saved.Version.ShouldBe(1);
        saved.TenantId.ShouldBe("tenant-a");

        var loaded = await Store.GetAsync("tenant-a", "inceleme");

        loaded.ShouldNotBeNull();
        loaded.Kind.ShouldBe(WorkflowKind.Sequential);
        loaded.AgentNames.ShouldBe(["arastirmaci", "yazar", "editor"]);
        loaded.Description.ShouldBe("Uc adimli inceleme.");
    }

    [Fact]
    public async Task Butun_alanlar_gidis_donuste_korunur()
    {
        // 🚨 jsonb yuku ELLE yazilmis bir DTO uzerinden gider. Tanima yeni bir
        // alan eklendiginde DTO guncellenmezse alan sessizce kaybolur; ne
        // derleme ne baska bir test kirilir. Bu testin varlik sebebi budur.
        var definition = new WorkflowDefinition
        {
            Name = "tam",
            DisplayName = "Tam Tanim",
            Description = "Butun alanlar dolu.",
            Kind = WorkflowKind.Handoff,
            AgentNames = ["destek", "uzman"],
            MaxIterations = 5,
            HandoffInstructions = "Teknik soruda uzmana devret.",
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = await Store.GetAsync("tenant-a", "tam");

        loaded.ShouldNotBeNull();
        loaded.DisplayName.ShouldBe("Tam Tanim");
        loaded.Description.ShouldBe("Butun alanlar dolu.");
        loaded.Kind.ShouldBe(WorkflowKind.Handoff);
        loaded.AgentNames.ShouldBe(["destek", "uzman"]);
        loaded.MaxIterations.ShouldBe(5);
        loaded.HandoffInstructions.ShouldBe("Teknik soruda uzmana devret.");

        // Faz 16'da eklendi. Varsayilan false oldugu icin eksik bir DTO alani
        // bu testte "false donduruldu" olarak gorunur - bilerek true yaziliyor.
        loaded.RequirePlanApproval.ShouldBeFalse();
    }

    [Fact]
    public async Task Magentic_yonetici_adi_korunur()
    {
        var definition = Definition() with
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "yonetici",
            RequirePlanApproval = true,
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = (await Store.GetAsync("tenant-a", "magentic"))!;

        loaded.ManagerAgentName.ShouldBe("yonetici");

        // Plan onayi jsonb yukunun bir parcasidir; elle yazilmis DTO'ya
        // eklenmezse sessizce kaybolurdu.
        loaded.RequirePlanApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task Her_kayit_surumu_artirir()
    {
        await Store.SaveAsync("tenant-a", Definition());
        var second = await Store.SaveAsync("tenant-a", Definition() with { Description = "Guncellendi." });

        second.Version.ShouldBe(2);

        var loaded = await Store.GetAsync("tenant-a", "inceleme");

        loaded!.Version.ShouldBe(2);
        loaded.Description.ShouldBe("Guncellendi.");
    }

    [Fact]
    public async Task Gelen_surum_degeri_yok_sayilir()
    {
        // Surumu depo belirler. Istemcinin gonderdigi degere guvenmek, iki
        // kullanicinin ayni surum numarasini yazmasina izin verirdi.
        var saved = await Store.SaveAsync("tenant-a", Definition() with { Version = 99 });

        saved.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Baska_kiracinin_tanimi_gorulmez()
    {
        await Store.SaveAsync("tenant-a", Definition());

        (await Store.GetAsync("tenant-b", "inceleme")).ShouldBeNull();
        (await Store.ListAsync("tenant-b")).ShouldBeEmpty();
        (await Store.DeleteAsync("tenant-b", "inceleme")).ShouldBeFalse();

        (await Store.GetAsync("tenant-a", "inceleme")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Ayni_ad_farkli_kiracilarda_bagimsizdir()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Description = "A kiracisi." });
        await Store.SaveAsync("tenant-b", Definition() with { Description = "B kiracisi." });

        (await Store.GetAsync("tenant-a", "inceleme"))!.Description.ShouldBe("A kiracisi.");
        (await Store.GetAsync("tenant-b", "inceleme"))!.Description.ShouldBe("B kiracisi.");
    }

    [Fact]
    public async Task Listeleme_ada_gore_siralar()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Name = "zeta" });
        await Store.SaveAsync("tenant-a", Definition() with { Name = "alfa" });
        await Store.SaveAsync("tenant-a", Definition() with { Name = "beta" });

        var names = (await Store.ListAsync("tenant-a")).Select(static definition => definition.Name).ToList();

        names.ShouldBe(["alfa", "beta", "zeta"]);
    }

    [Fact]
    public async Task Silme_var_olmayan_tanimda_false_doner()
        => (await Store.DeleteAsync("tenant-a", "yok")).ShouldBeFalse();

    [Fact]
    public async Task Silinen_tanim_geri_okunmaz()
    {
        await Store.SaveAsync("tenant-a", Definition());

        (await Store.DeleteAsync("tenant-a", "inceleme")).ShouldBeTrue();
        (await Store.GetAsync("tenant-a", "inceleme")).ShouldBeNull();
    }

    private static WorkflowDefinition Definition()
        => new()
        {
            Name = "inceleme",
            Description = "Uc adimli inceleme.",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["arastirmaci", "yazar", "editor"],
        };
}
