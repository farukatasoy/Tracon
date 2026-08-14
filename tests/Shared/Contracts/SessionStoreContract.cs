
namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="ISessionStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Oturum durumu opaktir; depo icerigi yorumlamadan aynen geri vermelidir.
/// </remarks>
public abstract class SessionStoreContract : TenantIsolationContract<ISessionStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// Kiraci arayuzde bir parametre degildir; <see cref="ITenantContext"/>'ten
    /// okunur. Bu yuzden her kanca once gecerli kiraciyi ayarlar.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        AmbientTenant.TenantId = tenantId;
        await Store.SaveAsync(TestData.Session(name));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return await Store.GetAsync((string)key) is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.QueryAsync(new SessionQuery())).Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return await Store.DeleteAsync((string)key);
    }

    [Fact]
    public async Task Oturum_durumu_bozulmadan_geri_gelir()
    {
        var state = TestData.State(
            """
            {"messages":[{"role":"user","text":"merhaba üği"},{"role":"assistant","text":"selam"}],
             "nested":{"deep":{"value":3.14159}},"flag":true,"nothing":null}
            """);

        await Store.SaveAsync(TestData.Session("s1", state));

        var loaded = await Store.GetAsync("s1");

        loaded.ShouldNotBeNull();
        loaded.State.GetRawText().ShouldBe(state.GetRawText());
        loaded.State.GetProperty("nested").GetProperty("deep").GetProperty("value").GetDouble().ShouldBe(3.14159);
        loaded.State.GetProperty("messages")[0].GetProperty("text").GetString().ShouldBe("merhaba üği");
    }

    [Fact]
    public async Task Ayni_kimlikle_kayit_uzerine_yazar_ve_olusturulma_zamanini_korur()
    {
        var created = DateTimeOffset.UtcNow.AddHours(-1);

        await Store.SaveAsync(TestData.Session("s1") with
        {
            CreatedAt = created,
            UpdatedAt = created,
            State = TestData.State("""{"turn":1}"""),
        });

        var later = DateTimeOffset.UtcNow;

        await Store.SaveAsync(TestData.Session("s1") with
        {
            CreatedAt = later,
            UpdatedAt = later,
            State = TestData.State("""{"turn":2}"""),
        });

        var loaded = await Store.GetAsync("s1");

        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetInt32().ShouldBe(2);
        loaded.CreatedAt.ShouldBe(created, TimeSpan.FromMilliseconds(1));
        loaded.UpdatedAt.ShouldBe(later, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Silme_kaydi_kaldirir()
    {
        await Store.SaveAsync(TestData.Session("s1"));

        (await Store.DeleteAsync("s1")).ShouldBeTrue();
        (await Store.GetAsync("s1")).ShouldBeNull();
        (await Store.DeleteAsync("s1")).ShouldBeFalse();
    }

    [Fact]
    public async Task Olmayan_oturum_null_doner()
        => (await Store.GetAsync("yok")).ShouldBeNull();

    [Fact]
    public async Task Sorgu_en_son_guncellenenden_baslar()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.SaveAsync(TestData.Session("eski") with { UpdatedAt = now.AddMinutes(-10) });
        await Store.SaveAsync(TestData.Session("yeni") with { UpdatedAt = now });
        await Store.SaveAsync(TestData.Session("orta") with { UpdatedAt = now.AddMinutes(-5) });

        var results = await Store.QueryAsync(new SessionQuery());

        results.Select(static session => session.Id).ShouldBe(["yeni", "orta", "eski"]);
    }

    [Fact]
    public async Task Sorgu_agent_adina_gore_filtreler()
    {
        await Store.SaveAsync(TestData.Session("s1") with { AgentName = "alpha" });
        await Store.SaveAsync(TestData.Session("s2") with { AgentName = "beta" });

        var results = await Store.QueryAsync(new SessionQuery { AgentName = "alpha" });

        results.ShouldHaveSingleItem().Id.ShouldBe("s1");
    }

    [Fact]
    public async Task Sorgu_sayfalama_uygular()
    {
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            await Store.SaveAsync(TestData.Session($"s{i}") with { UpdatedAt = now.AddMinutes(-i) });
        }

        var page = await Store.QueryAsync(new SessionQuery { Skip = 1, Take = 2 });

        page.Select(static session => session.Id).ShouldBe(["s1", "s2"]);
    }

    [Fact]
    public async Task TryCreateAsync_yeni_kimlikte_true_doner_ve_kaydeder()
    {
        var record = TestData.Session("yeni") with { State = TestData.State("""{"turn":1}""") };

        (await Store.TryCreateAsync(record)).ShouldBeTrue();

        var loaded = await Store.GetAsync("yeni");
        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task TryCreateAsync_var_olan_kimlikte_false_doner_ve_uzerine_yazmaz()
    {
        await Store.SaveAsync(TestData.Session("var-olan") with { State = TestData.State("""{"turn":1}""") });

        var created = await Store.TryCreateAsync(
            TestData.Session("var-olan") with { State = TestData.State("""{"turn":2}""") });

        created.ShouldBeFalse();

        var loaded = await Store.GetAsync("var-olan");
        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task TryCreateAsync_eszamanli_ayni_kimlikte_yalniz_biri_kazanir()
    {
        // HATA-004: check-then-create yarisinda iki eszamanli ilk istek ayni
        // YENI oturuma farkli birer konusma kimligi uretiyordu. TryCreateAsync
        // atomik olmalidir: N eszamanli cagridan tam olarak biri kazanmalidir.
        const int Concurrency = 8;

        var attempts = Enumerable.Range(0, Concurrency)
            .Select(i => Store.TryCreateAsync(
                    TestData.Session("yaris") with { State = TestData.State($$"""{"turn":{{i}}}""") })
                .AsTask());

        var results = await Task.WhenAll(attempts);

        results.Count(static won => won).ShouldBe(1);

        var loaded = await Store.GetAsync("yaris");
        loaded.ShouldNotBeNull();
    }

    [Fact]
    public async Task TryCreateAsync_ayni_kimlik_iki_kiracida_bagimsiz_kazanir()
    {
        // Birincil anahtar (tenant_id, id)'dir (K-018); benzersizlik ihlali tek
        // basina kimlige degil kiraci+kimlik ciftine bakmalidir.
        (await Store.TryCreateAsync(TestData.Session("paylasilan-id") with { TenantId = TenantA })).ShouldBeTrue();
        (await Store.TryCreateAsync(TestData.Session("paylasilan-id") with { TenantId = TenantB })).ShouldBeTrue();
    }
}
