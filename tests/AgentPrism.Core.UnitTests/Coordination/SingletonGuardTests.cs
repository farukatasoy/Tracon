using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Coordination;

/// <summary>
/// <see cref="SingletonGuard"/>'in kira/yenileme durum gecislerini dogrular
/// (Faz 42). Gercek zamanlayici gerektirmez: <c>TickAsync</c> dogrudan
/// cagrilir, boylece durum makinesi PeriodicTimer'dan bagimsiz sinanir.
/// </summary>
public sealed class SingletonGuardTests
{
    private const string LeaseName = "test-lease";

    [Fact]
    public async Task Kapaliyken_depoya_hicbir_sorgu_gitmez_ve_IsHeld_daima_true()
    {
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = false }), LeaseName);

        guard.IsHeld.ShouldBeTrue();

        await guard.RunAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
        store.AcquireCalls.ShouldBe(0);
        store.RenewCalls.ShouldBe(0);
        store.ReleaseCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Bos_kirayi_ilk_TickAsync_alir()
    {
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        guard.IsHeld.ShouldBeFalse();

        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
        store.AcquireCalls.ShouldBe(1);
        store.RenewCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Tutulan_kira_sonraki_turde_yenilenir_yeniden_alinmaz()
    {
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        await guard.TickAsync(CancellationToken.None);
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
        store.AcquireCalls.ShouldBe(1);
        store.RenewCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Baskasi_alamaz_IsHeld_false_kalir()
    {
        var store = new CountingSingletonLeaseStore { AcquireResult = false };
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Kira_kaybedilince_IsHeld_false_olur_ve_bir_sonraki_turde_yeniden_denenir()
    {
        var store = new CountingSingletonLeaseStore();
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        await guard.TickAsync(CancellationToken.None);
        guard.IsHeld.ShouldBeTrue();

        // 🚨 Kira baskasina gecti: RenewAsync artik false doner.
        store.RenewResult = false;
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeFalse();

        // Sonraki turde TryAcquireAsync yeniden denenir (RenewAsync degil).
        store.RenewResult = true;
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
        store.AcquireCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Depo_hata_firlatirsa_dongu_olmez_bir_sonraki_turde_devam_eder()
    {
        var store = new CountingSingletonLeaseStore { ThrowOnAcquire = true };
        var guard = new SingletonGuard(store, Options(new SingletonExecutionOptions { Enabled = true }), LeaseName);

        await guard.TickAsync(CancellationToken.None);
        guard.IsHeld.ShouldBeFalse();

        store.ThrowOnAcquire = false;
        await guard.TickAsync(CancellationToken.None);

        guard.IsHeld.ShouldBeTrue();
    }

    private static StaticOptionsMonitor<SingletonExecutionOptions> Options(SingletonExecutionOptions value) => new(value);

    /// <summary>Cagri sayacini tutan ve davranisi ayarlanabilen sahte kira deposu.</summary>
    private sealed class CountingSingletonLeaseStore : ISingletonLeaseStore
    {
        public int AcquireCalls { get; private set; }

        public int RenewCalls { get; private set; }

        public int ReleaseCalls { get; private set; }

        public bool AcquireResult { get; set; } = true;

        public bool RenewResult { get; set; } = true;

        public bool ThrowOnAcquire { get; set; }

        public ValueTask<bool> TryAcquireAsync(
            string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            AcquireCalls++;

            if (ThrowOnAcquire)
            {
                throw new InvalidOperationException("Test: kasitli hata.");
            }

            return new ValueTask<bool>(AcquireResult);
        }

        public ValueTask<bool> RenewAsync(
            string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            RenewCalls++;

            return new ValueTask<bool>(RenewResult);
        }

        public ValueTask ReleaseAsync(string name, string ownerId, CancellationToken cancellationToken = default)
        {
            ReleaseCalls++;

            return default;
        }
    }
}
