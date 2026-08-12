namespace AgentPrism.StoreContracts;

/// <summary>
/// Kiracisi calisma aninda degistirilebilen bir <see cref="ITenantContext"/>.
/// </summary>
/// <remarks>
/// Depolarin bir kismi kiraciyi metot parametresi olarak alir, bir kismi ise
/// <see cref="ITenantContext"/>'ten okur (ornegin <c>IRunStore</c>,
/// <c>ISessionStore</c>, <c>IAgentDefinitionStore</c>). Ikinci gruba iki kiracili
/// bir senaryo yazabilmek icin ya iki ayri depo ornegi ya da kiracisi
/// degistirilebilen tek bir baglam gerekir. Ikincisi secildi: ayni depo ornegi
/// ayni arka uca bakar ve "A'nin yazdigini B goruyor mu" sorusu tek bir depo
/// uzerinde sorulabilir.
/// </remarks>
/// <param name="tenantId">Baslangic kiracisi.</param>
public sealed class MutableTenantContext(string tenantId) : ITenantContext
{
    /// <inheritdoc />
    public string TenantId { get; set; } = tenantId;
}

/// <summary>
/// Bir deponun kiraci yalitimini <strong>iki yonlu</strong> sinayan ortak taban.
/// </summary>
/// <typeparam name="TStore">Sinanan depo tipi.</typeparam>
/// <remarks>
/// <para>
/// Faz 41'de eklendi. Bu taban ayni zamanda butun depo sozlesmelerinin ortak
/// yasam dongusu tesisatini tasir; turemis sozlesmeler yalnizca kendi
/// senaryolarini ve dort yalitim kancasini yazar.
/// </para>
/// <para>
/// 🚨 <strong>Iki yonlu denetim atlanamaz.</strong> Yalnizca "B gormemeli"
/// denetlenirse, hicbir sey dondurmeyen kirik bir sorgu da testi gecerdi.
/// Her senaryo ayrica "A kendi verisini goruyor mu" sorusunu da sorar; boylece
/// filtrenin hem yeterli hem de fazla dar olmadigi kanitlanir.
/// </para>
/// </remarks>
public abstract class TenantIsolationContract<TStore> : IAsyncLifetime
{
    /// <summary>Veriyi yazan kiraci.</summary>
    protected const string TenantA = "tenant-a";

    /// <summary>Veriyi gormemesi gereken kiraci.</summary>
    protected const string TenantB = "tenant-b";

    /// <summary>
    /// Depolarin <see cref="ITenantContext"/>'ten okudugu kiraci. Kiraciyi
    /// parametre olarak almayan depolarin sozlesmeleri, kancalarinin ilk
    /// satirinda bu ozelligi ilgili kiraciya ayarlar.
    /// </summary>
    protected MutableTenantContext AmbientTenant { get; private set; } = new(TenantA);

    /// <summary>Test edilen depo.</summary>
    protected TStore Store { get; private set; } = default!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<TStore> CreateStoreAsync();

    /// <summary>
    /// Sinif basina paylasilan (birden fazla test tarafindan kullanilan) bir
    /// kiraci baglamina baglanir ve kiraciyi <see cref="TenantA"/>'ya geri alir.
    /// </summary>
    /// <param name="tenant">Sema/test SINIFI fixture'inin sagladigi paylasilan baglam.</param>
    /// <remarks>
    /// Store ornekleri sinif basina bir kez kuruldugunda (bkz. sema fixture'lari)
    /// hepsi AYNI <see cref="ITenantContext"/> nesnesini yakalar; bu yuzden her
    /// test <see cref="CreateStoreAsync"/> icinde bu metodu cagirarak o nesneye
    /// baglanmali ve onceki testin kiraciyi <see cref="TenantB"/>'de birakmis
    /// olabilecegi durumu sifirlamalidir.
    /// </remarks>
    protected void UseAmbientTenant(MutableTenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        AmbientTenant = tenant;
        AmbientTenant.TenantId = TenantA;
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    // --- Kiraci yalitimi kancalari (Faz 41) ---

    /// <summary>Verilen kiraci icin ornek bir kayit yazar.</summary>
    /// <param name="tenantId">Kaydin sahibi kiraci.</param>
    /// <param name="name">Kaydi ayirt eden ad. Ayni ad iki kiracida kullanilabilir.</param>
    /// <returns>Kaydi geri okumak icin kullanilacak anahtar.</returns>
    protected abstract ValueTask<object> SeedAsync(string tenantId, string name);

    /// <summary>Kaydin verilen kiraci gozunden gorunup gorunmedigini soyler.</summary>
    /// <param name="tenantId">Okuyan kiraci.</param>
    /// <param name="key"><see cref="SeedAsync"/>'in dondurdugu anahtar.</param>
    /// <returns>Kayit goruluyorsa <see langword="true"/>.</returns>
    protected abstract ValueTask<bool> ExistsAsync(string tenantId, object key);

    /// <summary>Verilen kiracinin listeleme ucundan kac kayit gordugunu soyler.</summary>
    /// <param name="tenantId">Okuyan kiraci.</param>
    /// <returns>Gorulen kayit sayisi.</returns>
    protected abstract ValueTask<int> CountAsync(string tenantId);

    /// <summary>
    /// Kaydi verilen kiraci adina silmeyi dener.
    /// </summary>
    /// <param name="tenantId">Silmeyi deneyen kiraci.</param>
    /// <param name="key"><see cref="SeedAsync"/>'in dondurdugu anahtar.</param>
    /// <returns>
    /// Silme gerceklestiyse <see langword="true"/>, kayit bulunamadiysa
    /// <see langword="false"/>; depo silme sunmuyorsa <see langword="null"/>.
    /// </returns>
    protected virtual ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => new((bool?)null);

    /// <summary>
    /// Kaydin icerigini verilen kiraci adina degistirmeyi dener.
    /// </summary>
    /// <param name="tenantId">Guncellemeyi deneyen kiraci.</param>
    /// <param name="name">Kaydin adi.</param>
    /// <returns>
    /// Guncelleme denendiyse <see langword="true"/>; depo bu senaryoyu
    /// desteklemiyorsa <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Varsayilan uygulama <see cref="SeedAsync"/>'i ikinci kez cagirir: adi
    /// ayni olan bir kayit yazmak, iki kiracinin ayni adi bagimsiz
    /// tasiyabildigini dogrular.
    /// </remarks>
    protected virtual async ValueTask<bool> TryOverwriteAsync(string tenantId, string name)
    {
        await SeedAsync(tenantId, name);
        return true;
    }

    // --- Yalitim testleri ---

    [Fact]
    public async Task Kiraci_digerinin_kaydini_okuyamaz()
    {
        var key = await SeedAsync(TenantA, "gizli");

        (await ExistsAsync(TenantB, key)).ShouldBeFalse(
            "B kiracisi A kiracisinin kaydini okuyabiliyor.");
    }

    [Fact]
    public async Task Kiraci_kendi_kaydini_okur()
    {
        // 🚨 Ikinci yon. Bu denetim olmadan hicbir sey dondurmeyen kirik bir
        // sorgu da yalitim testini gecerdi.
        var key = await SeedAsync(TenantA, "gizli");

        (await ExistsAsync(TenantA, key)).ShouldBeTrue(
            "A kiracisi kendi kaydini okuyamiyor; filtre fazla dar.");
    }

    [Fact]
    public async Task Kiraci_digerinin_kaydini_listede_gormez()
    {
        await SeedAsync(TenantA, "gizli");

        (await CountAsync(TenantB)).ShouldBe(0, "Listeleme ucu kiracilar arasinda siziyor.");
        (await CountAsync(TenantA)).ShouldBeGreaterThan(0, "Kiraci kendi kaydini listeleyemiyor.");
    }

    [Fact]
    public async Task Kiraci_digerinin_kaydini_silemez()
    {
        var key = await SeedAsync(TenantA, "gizli");

        var deleted = await TryDeleteAsync(TenantB, key);

        if (deleted is null)
        {
            // Depo silme sunmuyor; senaryo gecerli degil.
            return;
        }

        deleted.Value.ShouldBeFalse("B kiracisi A kiracisinin kaydini silebiliyor.");
        (await ExistsAsync(TenantA, key)).ShouldBeTrue("Kayit basarisiz silme denemesinden sonra kaybolmus.");

        // Kendi kaydini silebilmelidir; aksi halde silme sorgusu tumden kirik olurdu.
        (await TryDeleteAsync(TenantA, key))!.Value.ShouldBeTrue("Kiraci kendi kaydini silemiyor.");
    }

    [Fact]
    public async Task Ayni_ad_iki_kiracida_bagimsiz_yasar()
    {
        var keyA = await SeedAsync(TenantA, "ortak-ad");

        if (!await TryOverwriteAsync(TenantB, "ortak-ad"))
        {
            return;
        }

        (await ExistsAsync(TenantA, keyA)).ShouldBeTrue(
            "B kiracisinin ayni adla yazmasi A kiracisinin kaydini ezdi.");
        (await CountAsync(TenantA)).ShouldBe(1);
        (await CountAsync(TenantB)).ShouldBe(1);
    }
}
