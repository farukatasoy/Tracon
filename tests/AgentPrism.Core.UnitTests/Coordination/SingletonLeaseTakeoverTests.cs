using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Coordination;

/// <summary>
/// Iki <see cref="SingletonGuard"/> orneginin AYNI kira deposunu paylastigi
/// uctan uca senaryo (Faz 42): yalniz biri kirayi tutar, sahibi dusunce
/// digeri devralir. Gercek zaman kullanir (kisa kira + kisa bekleme) —
/// <c>JobStoreContract</c>'in kira suresi dolma testiyle ayni gerekce.
/// </summary>
public sealed class SingletonLeaseTakeoverTests
{
    private const string LeaseName = "shared-lease";

    [Fact]
    public async Task Iki_ornekten_yalniz_biri_kirayi_tutar()
    {
        var store = new InMemorySingletonLeaseStore();
        var options = Options(new SingletonExecutionOptions { Enabled = true, LeaseDuration = TimeSpan.FromMinutes(5) });

        var guardA = new SingletonGuard(store, options, LeaseName);
        var guardB = new SingletonGuard(store, options, LeaseName);

        await guardA.TickAsync(CancellationToken.None);
        await guardB.TickAsync(CancellationToken.None);

        guardA.IsHeld.ShouldBeTrue();
        guardB.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Sahip_durunca_diger_ornek_devralir()
    {
        var store = new InMemorySingletonLeaseStore();
        var shortLease = Options(new SingletonExecutionOptions { Enabled = true, LeaseDuration = TimeSpan.FromMilliseconds(20) });

        var guardA = new SingletonGuard(store, shortLease, LeaseName);
        var guardB = new SingletonGuard(store, shortLease, LeaseName);

        await guardA.TickAsync(CancellationToken.None);
        guardA.IsHeld.ShouldBeTrue();

        (await store.TryAcquireAsync(LeaseName, "someone-else", TimeSpan.FromMinutes(5))).ShouldBeFalse();

        // A durur (bir daha hic yenilemez). Kira suresi dolana kadar bekle.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        await guardB.TickAsync(CancellationToken.None);

        guardB.IsHeld.ShouldBeTrue();
    }

    private static StaticOptionsMonitor<SingletonExecutionOptions> Options(SingletonExecutionOptions value) => new(value);
}
