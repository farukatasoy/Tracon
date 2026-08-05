namespace AgentPrism.Core.UnitTests.Voice;

/// <summary>
/// Akan metnin seslendirilebilir parcalara bolunmesini dogrular.
/// </summary>
/// <remarks>
/// Gecikmenin kaynagi bu boludur: ilk parca ne kadar erken cikarsa kullanici
/// sesi o kadar erken duyar.
/// </remarks>
public sealed class VoiceSpeechSegmenterTests
{
    [Fact]
    public void Cumle_sonunda_parca_uretilir()
    {
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append("Merhaba, nasil").ShouldBeEmpty();

        var ready = segmenter.Append(" yardimci olabilirim? Sonraki cumle");

        ready.Count.ShouldBe(1);
        ready[0].ShouldBe("Merhaba, nasil yardimci olabilirim?");
    }

    [Fact]
    public void Cumle_sonu_karakteri_METNIN_SONUNDA_ise_beklenir()
    {
        // "3." veya bir kisaltma da nokta ile biter; sonraki karakter gelmeden
        // cumle sonu oldugu bilinemez.
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append("Siparis numaranız 12345.").ShouldBeEmpty();
        segmenter.Append(" Kargoya verildi.").Count.ShouldBe(1);
    }

    [Fact]
    public void Cok_kisa_parca_SONRAKI_ile_birlestirilir()
    {
        // "Evet." tek basina seslendirilirse ses kopuk cikar.
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append("Evet. ").ShouldBeEmpty();

        var ready = segmenter.Append("Siparisiniz hazir. ");

        ready.Count.ShouldBe(1);
        ready[0].ShouldBe("Evet. Siparisiniz hazir.");
    }

    [Fact]
    public void Noktalama_hic_gelmezse_uzunluk_sinirinda_bolunur()
    {
        // Noktalama kullanmayan bir model aksi halde hic bolunmez ve ilk ses
        // yanitin sonunu beklerdi.
        var segmenter = new VoiceSpeechSegmenter();
        var ready = segmenter.Append(string.Join(' ', Enumerable.Repeat("kelime", 80)));

        ready.ShouldNotBeEmpty();
        ready[0].Length.ShouldBeLessThanOrEqualTo(240);
        ready[0].ShouldEndWith("kelime");
    }

    [Fact]
    public void Flush_kalani_dondurur_ve_tamponu_bosaltir()
    {
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append("Yarim kalan bir cumle");

        segmenter.Flush().ShouldBe("Yarim kalan bir cumle");
        segmenter.Flush().ShouldBeNull();
    }

    [Fact]
    public void Satir_sonu_da_bir_parca_sinirıdır()
    {
        var segmenter = new VoiceSpeechSegmenter();
        var ready = segmenter.Append("Birinci madde budur\nIkinci madde");

        ready.Count.ShouldBe(1);
        ready[0].ShouldBe("Birinci madde budur");
    }

    [Fact]
    public void Bos_parca_yok_sayilir()
    {
        var segmenter = new VoiceSpeechSegmenter();

        segmenter.Append(null).ShouldBeEmpty();
        segmenter.Append(string.Empty).ShouldBeEmpty();
        segmenter.Flush().ShouldBeNull();
    }
}
