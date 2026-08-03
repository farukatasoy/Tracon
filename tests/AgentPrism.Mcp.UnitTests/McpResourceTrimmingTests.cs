namespace AgentPrism.Mcp.UnitTests;

public sealed class McpResourceTrimmingTests
{
    [Fact]
    public void Sinirin_altindaki_metin_kirpilmaz()
    {
        var (text, truncated) = McpResourceTrimming.Trim("merhaba dunya", maxBytes: 1024);

        text.ShouldBe("merhaba dunya");
        truncated.ShouldBeFalse();
    }

    [Fact]
    public void Sinirin_ustundeki_metin_kirpilir()
    {
        var (text, truncated) = McpResourceTrimming.Trim("0123456789", maxBytes: 5);

        text.ShouldBe("01234");
        truncated.ShouldBeTrue();
    }

    [Fact]
    public void Cok_baytli_karakterin_ortasindan_kesmez()
    {
        // 'ş' UTF-8'de 2 bayttir. Sinir tam ortasina denk gelirse (3. bayt),
        // gecerli bir sinir bulunana kadar geri cekilmelidir.
        var text = "abş"; // a(1) b(1) ş(2) = 4 bayt

        var (trimmed, truncated) = McpResourceTrimming.Trim(text, maxBytes: 3);

        trimmed.ShouldBe("ab");
        truncated.ShouldBeTrue();

        // Sonuc HER ZAMAN gecerli UTF-8 olmalidir.
        System.Text.Encoding.UTF8.GetByteCount(trimmed).ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public void Sifir_sinir_bos_metin_dondurur()
    {
        var (text, truncated) = McpResourceTrimming.Trim("bir seyler", maxBytes: 0);

        text.ShouldBe(string.Empty);
        truncated.ShouldBeTrue();
    }

    [Fact]
    public void Bos_metin_kirpilmis_sayilmaz()
    {
        var (text, truncated) = McpResourceTrimming.Trim(string.Empty, maxBytes: 100);

        text.ShouldBe(string.Empty);
        truncated.ShouldBeFalse();
    }
}
