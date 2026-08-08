namespace AgentPrism.Core.UnitTests.Security;

/// <summary>
/// <see cref="ApiKeyGenerator"/>'in testleri.
/// </summary>
/// <remarks>
/// Anahtarin geri donduruleyemez oldugunu ve ozetin tahmin edilemez oldugunu
/// dogrular (docs/53-KIRACI-API-ANAHTARLARI.md, bolum 53.2).
/// </remarks>
public sealed class ApiKeyGeneratorTests
{
    [Fact]
    public void Uretilen_deger_onek_ve_kiraci_parcasi_tasir()
    {
        var generated = ApiKeyGenerator.Generate("acme-corp");

        generated.PlaintextKey.ShouldStartWith("ap_acmecorp_");
        generated.KeyPrefix.ShouldBe(generated.PlaintextKey[..12]);
    }

    [Fact]
    public void Iki_uretim_ayni_ham_degeri_vermez()
    {
        var first = ApiKeyGenerator.Generate("tenant");
        var second = ApiKeyGenerator.Generate("tenant");

        string.Equals(first.PlaintextKey, second.PlaintextKey, StringComparison.Ordinal).ShouldBeFalse();
        first.KeyHash.ShouldNotBe(second.KeyHash);
    }

    [Fact]
    public void Ayni_ham_deger_ayni_ozeti_uretir()
    {
        var generated = ApiKeyGenerator.Generate("tenant");

        var hash1 = ApiKeyGenerator.ComputeHash(generated.PlaintextKey);
        var hash2 = ApiKeyGenerator.ComputeHash(generated.PlaintextKey);

        hash1.ShouldBe(hash2);
        hash1.ShouldBe(generated.KeyHash);
    }

    [Fact]
    public void Ozet_32_bayt_sha256dir()
        => ApiKeyGenerator.ComputeHash("herhangi-bir-deger").Length.ShouldBe(32);

    [Fact]
    public void Ozetten_ham_degere_donulemez()
    {
        // Ozetin kendisi geri donduruleyemez bir tek yonlu fonksiyon
        // ciktisidir; burada dogrulanan sey ozetin ham degerle AYNI
        // OLMADIGIDIR -- tersine cevrilebilirlik matematiksel olarak
        // test edilemez, ama yanlislikla ham degeri saklama regresyonunu
        // yakalar.
        var generated = ApiKeyGenerator.Generate("tenant");

        var decoded = System.Text.Encoding.UTF8.GetString(generated.KeyHash);
        string.Equals(decoded, generated.PlaintextKey, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Tenant_kimliginde_ozel_karakterler_temizlenir()
    {
        var generated = ApiKeyGenerator.Generate("Acme.Corp/Prod!!");

        generated.PlaintextKey.ShouldStartWith("ap_acmecorpprod_");
    }

    [Fact]
    public void Bos_kiraci_segmenti_default_olur()
    {
        var generated = ApiKeyGenerator.Generate("---");

        generated.PlaintextKey.ShouldStartWith("ap_default_");
    }

    [Fact]
    public void Bos_kiraci_kimligi_reddedilir()
        => Should.Throw<ArgumentException>(() => ApiKeyGenerator.Generate(" "));

    [Fact]
    public void Bos_ham_deger_ozetlenemez()
        => Should.Throw<ArgumentException>(() => ApiKeyGenerator.ComputeHash(string.Empty));
}
