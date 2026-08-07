namespace AgentPrism.Core.UnitTests.Runs;

/// <summary>
/// <see cref="ErrorFingerprint"/>'in normallestirme adimlarini ve
/// kumeleme kararliligini dogrular.
/// </summary>
public sealed class ErrorFingerprintTests
{
    [Fact]
    public void Guid_iceren_iki_benzer_mesaj_ayni_parmak_izini_uretir()
    {
        var first = ErrorFingerprint.Compute("run 3f2a1c4e-8b9d-4e11-9a2f-7c6d5e4f3a2b bulunamadi");
        var second = ErrorFingerprint.Compute("run 9d8e7f6a-1b2c-4d3e-8f9a-0b1c2d3e4f5a bulunamadi");

        first.ShouldBe(second);
    }

    [Fact]
    public void Sayi_iceren_iki_benzer_mesaj_ayni_parmak_izini_uretir()
    {
        var first = ErrorFingerprint.Compute("cagri 4823 ms sonra zaman asimina ugradi");
        var second = ErrorFingerprint.Compute("cagri 91 ms sonra zaman asimina ugradi");

        first.ShouldBe(second);
    }

    [Fact]
    public void Tarih_ve_saat_iceren_iki_benzer_mesaj_ayni_parmak_izini_uretir()
    {
        var first = ErrorFingerprint.Compute("istek 2026-08-06T10:15:30Z zamaninda basladi ve dustu");
        var second = ErrorFingerprint.Compute("istek 2019-01-02T03:04:05Z zamaninda basladi ve dustu");

        first.ShouldBe(second);
    }

    [Fact]
    public void Farkli_arizalar_farkli_parmak_izi_uretir()
    {
        var timeout = ErrorFingerprint.Compute("islem zaman asimina ugradi");
        var refused = ErrorFingerprint.Compute("baglanti reddedildi");

        string.Equals(timeout, refused, StringComparison.Ordinal).ShouldBeFalse();
    }

    /// <summary>
    /// Acik Soru 3 -> C: tirnak ici metin BILEREK silinmez. Tool adi ayirt
    /// edicidir; iki farkli tool'un hatasi ayni kumede TOPLANMAMALIDIR.
    /// </summary>
    [Fact]
    public void Farkli_tool_adlari_tasiyan_mesajlar_ayri_kumede_kalir()
    {
        var refundFailure = ErrorFingerprint.Compute("Tool 'refund_order' calisirken hata olustu");
        var shipmentFailure = ErrorFingerprint.Compute("Tool 'track_shipment' calisirken hata olustu");

        string.Equals(refundFailure, shipmentFailure, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Ayni_mesaj_her_zaman_ayni_parmak_izini_uretir()
    {
        const string message = "Sağlayıcı 429 Too Many Requests döndürdü, run 3f2a1c4e-8b9d-4e11-9a2f-7c6d5e4f3a2b";

        var first = ErrorFingerprint.Compute(message);
        var second = ErrorFingerprint.Compute(message);
        var third = ErrorFingerprint.Compute(message);

        first.ShouldBe(second);
        second.ShouldBe(third);
    }

    [Fact]
    public void Parmak_izi_kucuk_harf_onaltilik_sha256_ozetidir()
    {
        var fingerprint = ErrorFingerprint.Compute("basit bir hata");

        fingerprint.Length.ShouldBe(64);
        fingerprint.ShouldBe(fingerprint.ToLowerInvariant());
    }
}
