namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="ISingletonLeaseStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// <para>
/// Bellek ici depo ile uc SQL saglayicisi ayni senaryolari gecmelidir. Kira
/// suresi dolma testi gercek zamanla calisir (kisa bir kira + kisa bir
/// bekleme): SQL uygulamalari kendi saatlerini enjekte edilebilir bir
/// <see cref="TimeProvider"/> uzerinden almaz — <c>JobStoreContract</c> ile
/// ayni gerekce.
/// </para>
/// <para>
/// Kiraci kavrami yoktur (Faz 42): tek yurutucu secimi kurulum genelinde bir
/// isletim kavramidir. Bu yuzden sozlesme <see cref="TenantIsolationContract{TStore}"/>'tan
/// degil, dogrudan <see cref="IAsyncLifetime"/>'dan turer —
/// <c>RetentionStoreContract</c> ile ayni desen.
/// </para>
/// </remarks>
public abstract class SingletonLeaseStoreContract : IAsyncLifetime
{
    /// <summary>Sinanan kira deposu.</summary>
    protected ISingletonLeaseStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir kira deposu uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<ISingletonLeaseStore> CreateStoreAsync();

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

    private static string Lease() => $"lease-{Guid.NewGuid():N}";

    private static string Owner() => $"owner-{Guid.NewGuid():N}";

    [Fact]
    public async Task Bos_kira_alinabilir()
    {
        var acquired = await Store.TryAcquireAsync(Lease(), Owner(), TimeSpan.FromMinutes(5));

        acquired.ShouldBeTrue();
    }

    [Fact]
    public async Task Kira_tutulurken_ikinci_sahip_alamaz()
    {
        var lease = Lease();
        await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5));

        (await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task Ayni_sahip_kendi_kirasini_yeniden_alabilir()
    {
        var lease = Lease();
        var owner = Owner();

        await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5));

        (await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task Kira_suresi_dolunca_baskasi_alabilir()
    {
        var lease = Lease();

        await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMilliseconds(20));
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        (await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task Sahip_kirasini_yenileyebilir()
    {
        var lease = Lease();
        var owner = Owner();

        await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5));

        (await Store.RenewAsync(lease, owner, TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task Kira_baskasina_gectikten_sonra_RenewAsync_false_doner()
    {
        var lease = Lease();
        var firstOwner = Owner();

        await Store.TryAcquireAsync(lease, firstOwner, TimeSpan.FromMilliseconds(20));
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5));

        // 🚨 Eski sahip kirasini yenilemeye calisirsa false donmelidir — cagiran
        // isi BIRAKMALIDIR (Testler tablosu, docs/42-TEK-YURUTUCU-SECIMI.md).
        (await Store.RenewAsync(lease, firstOwner, TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task Sahip_olmayan_RenewAsync_false_doner()
    {
        var lease = Lease();

        (await Store.RenewAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task Birakilan_kira_hemen_baskasina_verilebilir()
    {
        var lease = Lease();
        var owner = Owner();

        await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5));
        await Store.ReleaseAsync(lease, owner);

        (await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task Sahip_olmayan_ReleaseAsync_hicbir_sey_yapmaz()
    {
        var lease = Lease();
        var owner = Owner();

        await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5));

        // Baska bir 'sahibin' birakma denemesi mevcut kirayi ETKILEMEMELIDIR.
        await Store.ReleaseAsync(lease, Owner());

        (await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }
}
