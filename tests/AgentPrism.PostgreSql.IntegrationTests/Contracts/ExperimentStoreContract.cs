namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// <see cref="IExperimentStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bu testler <strong>her uygulama icin</strong> calistirilir. Bellek ici depo ile
/// PostgreSQL deposu arasindaki davranis farki hatadir; bu sinif o farki yakalar.
/// </remarks>
public abstract class ExperimentStoreContract : IAsyncLifetime
{
    private const string TenantId = "default";

    /// <summary>Test edilen depo.</summary>
    protected IExperimentStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    protected abstract ValueTask<IExperimentStore> CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    protected virtual ValueTask OnDisposeAsync() => default;

    [Fact]
    public async Task Kayit_ve_getirme_gidip_gelir()
    {
        var saved = await Store.SaveAsync(Experiment("d1"));

        var fetched = await Store.GetAsync(TenantId, "d1");

        fetched.ShouldNotBeNull();
        fetched.AgentName.ShouldBe(saved.AgentName);
        fetched.Status.ShouldBe(ExperimentStatus.Draft);
        fetched.Variants.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Draft_deney_guncellenebilir()
    {
        await Store.SaveAsync(Experiment("d1"));

        var updated = await Store.SaveAsync(Experiment("d1") with
        {
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 30 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 70 },
            ],
        });

        updated.Variants.Single(static v => string.Equals(v.Name, "v2", StringComparison.Ordinal)).Weight.ShouldBe(70);
    }

    [Fact]
    public async Task Calisan_deney_guncellenemez()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.SaveAsync(Experiment("d1")));
    }

    [Fact]
    public async Task Baslatma_calisan_deneyi_dondurur()
    {
        await Store.SaveAsync(Experiment("d1"));

        var started = await Store.StartAsync(TenantId, "d1");

        started.Status.ShouldBe(ExperimentStatus.Running);
        started.StartedAt.ShouldNotBeNull();

        var running = await Store.GetRunningAsync(TenantId, "agent-a");
        running.ShouldNotBeNull();
        running!.Name.ShouldBe("d1");
    }

    [Fact]
    public async Task Ayni_agent_icin_ikinci_baslatma_reddedilir()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        await Store.SaveAsync(Experiment("d2"));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StartAsync(TenantId, "d2"));
    }

    [Fact]
    public async Task Durdurma_deneyi_durdurur_ve_calisan_slotu_bosaltir()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        var stopped = await Store.StopAsync(TenantId, "d1");

        stopped.Status.ShouldBe(ExperimentStatus.Stopped);
        stopped.EndedAt.ShouldNotBeNull();

        (await Store.GetRunningAsync(TenantId, "agent-a")).ShouldBeNull();

        // Slot bosaldigi icin baska bir deney ayni agent'ta baslatilabilir.
        await Store.SaveAsync(Experiment("d2"));
        var secondStart = await Store.StartAsync(TenantId, "d2");
        secondStart.Status.ShouldBe(ExperimentStatus.Running);
    }

    [Fact]
    public async Task Calismayan_deney_durdurulamaz()
    {
        await Store.SaveAsync(Experiment("d1"));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StopAsync(TenantId, "d1"));
    }

    [Fact]
    public async Task Olmayan_deney_baslatilamaz()
        => await Should.ThrowAsync<AgentPrismException>(async () => await Store.StartAsync(TenantId, "yok"));

    [Fact]
    public async Task Calisan_deney_silinemez()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.DeleteAsync(TenantId, "d1"));
    }

    [Fact]
    public async Task Draft_deney_silinebilir()
    {
        await Store.SaveAsync(Experiment("d1"));

        (await Store.DeleteAsync(TenantId, "d1")).ShouldBeTrue();
        (await Store.GetAsync(TenantId, "d1")).ShouldBeNull();
    }

    [Fact]
    public async Task Olmayan_deney_silinirken_false_doner()
        => (await Store.DeleteAsync(TenantId, "yok")).ShouldBeFalse();

    [Fact]
    public async Task Listeleme_yalnizca_o_kiraciyi_getirir()
    {
        await Store.SaveAsync(Experiment("d1", tenantId: "kiraci-a"));
        await Store.SaveAsync(Experiment("d2", tenantId: "kiraci-b"));

        var listA = await Store.ListAsync("kiraci-a");

        listA.ShouldHaveSingleItem().Name.ShouldBe("d1");
    }

    [Fact]
    public async Task Calisan_deney_yoksa_null_doner()
        => (await Store.GetRunningAsync(TenantId, "agent-a")).ShouldBeNull();

    private static Experiment Experiment(string name, string tenantId = TenantId)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            AgentName = "agent-a",
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 },
            ],
        };
}
