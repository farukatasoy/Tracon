namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IRetentionStore"/> veri duzleminin davranis ve kiraci yalitimi
/// testleri.
/// </summary>
/// <remarks>
/// <para>
/// Faz 41'de eklendi. 🚨 Bu sozlesme bir <strong>guvenlik kusurunun</strong>
/// uzerine yazildi: veri duzlemi kiraci suzgeci tasimiyordu ve bir kiracinin
/// saklama politikasi <em>butun</em> kiracilarin satirlarini siliyordu.
/// </para>
/// <para>
/// Sozlesme <see cref="TenantIsolationContract{TStore}"/>'tan turemez: veri
/// duzleminin "kayit" kavrami yoktur (yazma ucu yoktur), tohumlama hedef
/// tablonun kendi deposundan gecer. Yalitim burada dogrudan sinanir.
/// </para>
/// </remarks>
public abstract class RetentionStoreContract : IAsyncLifetime
{
    /// <summary>Veriyi yazan kiraci.</summary>
    protected const string TenantA = "tenant-a";

    /// <summary>Verisi korunmasi gereken kiraci.</summary>
    protected const string TenantB = "tenant-b";

    /// <summary>Sinanan veri duzlemi.</summary>
    protected IRetentionStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir veri duzlemi uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<IRetentionStore> CreateStoreAsync();

    /// <summary>
    /// Verilen kiraci icin <see cref="RetentionTargets.VoiceSessions"/> hedefine
    /// dusen, kesim tarihinden eski bir satir yazar.
    /// </summary>
    /// <param name="tenantId">Satirin sahibi kiraci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    protected abstract ValueTask SeedOldRowAsync(string tenantId);

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

    /// <summary>Tohumlanan satirlarin hepsinden yeni bir kesim tarihi.</summary>
    private static DateTimeOffset Cutoff { get; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sayim_yalnizca_verilen_kiraciyi_kapsar()
    {
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantB);

        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, TenantA, Cutoff)).ShouldBe(1);
        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, TenantB, Cutoff)).ShouldBe(1);

        // Kiraci verilmezse islem kurulum genelindedir.
        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, tenantId: null, Cutoff)).ShouldBe(2);
    }

    [Fact]
    public async Task Silme_digerinin_verisine_DOKUNMAZ()
    {
        // 🚨 Faz 41 oncesi bu test kirmiziydi: kiraci suzgeci yoktu ve tek bir
        // kiracinin politikasi butun kurulumu siliyordu.
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantB);

        var deleted = await Store.DeleteBatchAsync(RetentionTargets.VoiceSessions, TenantA, Cutoff, batchSize: 100);

        deleted.ShouldBe(1);

        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, TenantA, Cutoff)).ShouldBe(0);
        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, TenantB, Cutoff)).ShouldBe(1);
    }

    [Fact]
    public async Task Arsiv_okumasi_yalnizca_verilen_kiraciyi_dondurur()
    {
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantB);

        var rows = await Store.ReadForArchiveAsync(
            RetentionTargets.VoiceSessions,
            TenantA,
            Cutoff,
            batchSize: 100);

        rows.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Hacim_siniri_esigi_yalnizca_verilen_kiraciyi_sayar()
    {
        // A kiracisinda iki, B kiracisinda bir satir. A icin "en fazla 2 satir"
        // esigi hicbir sey silmemelidir; suzgec calismasaydi toplam uc satir
        // sayilir ve esik dolu cikardi.
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantB);

        (await Store.FindRowLimitCutoffAsync(RetentionTargets.VoiceSessions, TenantA, maxRows: 3)).ShouldBeNull();
        (await Store.FindRowLimitCutoffAsync(RetentionTargets.VoiceSessions, TenantA, maxRows: 2)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Bilinmeyen_hedef_hata_verir()
        => await Should.ThrowAsync<ArgumentException>(
            async () => await Store.CountOlderThanAsync("bilinmeyen-hedef", TenantA, Cutoff));
}
