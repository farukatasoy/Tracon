namespace AgentPrism.Core.UnitTests.Voice;

/// <summary>
/// Kiraci basina es zamanli konusma baglantisi sinirini dogrular.
/// </summary>
public sealed class VoiceConnectionLimiterTests
{
    [Fact]
    public void Sinir_dolunca_yeni_baglanti_REDDEDILIR()
    {
        var limiter = new VoiceConnectionLimiter(2);

        using var first = limiter.TryAcquire("kiraci-a").ShouldNotBeNull();
        using var second = limiter.TryAcquire("kiraci-a").ShouldNotBeNull();

        limiter.TryAcquire("kiraci-a").ShouldBeNull();
        limiter.CountFor("kiraci-a").ShouldBe(2);
    }

    [Fact]
    public void Sinir_KIRACI_BASINADIR()
    {
        var limiter = new VoiceConnectionLimiter(1);

        using var first = limiter.TryAcquire("kiraci-a").ShouldNotBeNull();

        limiter.TryAcquire("kiraci-b").ShouldNotBeNull().Dispose();
    }

    [Fact]
    public void Bertaraf_edilen_yer_serbest_kalir()
    {
        var limiter = new VoiceConnectionLimiter(1);

        var lease = limiter.TryAcquire("kiraci-a").ShouldNotBeNull();
        limiter.TryAcquire("kiraci-a").ShouldBeNull();

        lease.Dispose();

        limiter.CountFor("kiraci-a").ShouldBe(0);
        limiter.TryAcquire("kiraci-a").ShouldNotBeNull().Dispose();
    }

    [Fact]
    public void Ikinci_bertaraf_sayaci_EKSIYE_dusurmez()
    {
        var limiter = new VoiceConnectionLimiter(1);
        var lease = limiter.TryAcquire("kiraci-a").ShouldNotBeNull();

        lease.Dispose();
        lease.Dispose();

        limiter.CountFor("kiraci-a").ShouldBe(0);
    }

    [Fact]
    public void Yarisan_istekler_siniri_ASMAZ()
    {
        // 🚨 Karsilastir-ve-degistir olmadan iki istek ayni degeri okur ve
        // ikisi de siniri asmayan bir sonuc gorurdu.
        var limiter = new VoiceConnectionLimiter(10);
        var leases = new VoiceConnectionLease?[64];

        Parallel.For(0, leases.Length, index => leases[index] = limiter.TryAcquire("kiraci-a"));

        leases.Count(static lease => lease is not null).ShouldBe(10);
        limiter.CountFor("kiraci-a").ShouldBe(10);

        foreach (var lease in leases)
        {
            lease?.Dispose();
        }

        limiter.CountFor("kiraci-a").ShouldBe(0);
    }

    [Fact]
    public void Sifir_veya_negatif_sinir_EN_AZ_BIRE_yukseltilir()
    {
        // Sifir sinir yetenegi sessizce kapatirdi; yanlis yapilandirma
        // gorunmez bir kesintiye donusmemelidir.
        new VoiceConnectionLimiter(0).Limit.ShouldBe(1);
        new VoiceConnectionLimiter(-5).Limit.ShouldBe(1);
    }
}
