namespace AgentPrism.Core.UnitTests.Knowledge;

public sealed class TextChunkerTests
{
    [Fact]
    public void Bos_metin_bos_liste_dondurur()
    {
        TextChunker.Split(string.Empty, 100, 10).ShouldBeEmpty();
    }

    [Fact]
    public void Kisa_metin_tek_parca_dondurur()
    {
        var chunks = TextChunker.Split("merhaba dunya", 100, 10);

        chunks.ShouldHaveSingleItem().ShouldBe("merhaba dunya");
    }

    [Fact]
    public void Uzun_metin_ortusmeli_parcalara_bolunur()
    {
        var text = new string('a', 25);

        var chunks = TextChunker.Split(text, chunkSize: 10, chunkOverlap: 3);

        chunks.Count.ShouldBeGreaterThan(1);

        // Her parca (son haric) tam chunkSize uzunlugundadir; parcalar arti
        // adimla ilerler (chunkSize - chunkOverlap) ve son karakter kaybolmaz.
        var reconstructed = string.Concat(chunks.Select(static (c, i) => i == 0 ? c : c[3..]));
        reconstructed.ShouldBe(text);
    }

    [Fact]
    public void Sinirda_karakter_kaybi_olmaz()
    {
        var text = string.Concat(Enumerable.Range(0, 37).Select(static i => (char)('a' + (i % 26))));

        var chunks = TextChunker.Split(text, chunkSize: 12, chunkOverlap: 4);

        chunks[^1].ShouldEndWith(text[^1].ToString());

        // Toplam benzersiz kapsanan karakter sayisi metnin tamamini kapsamali.
        var coveredEnd = 0;
        var step = 12 - 4;

        for (var i = 0; i < chunks.Count; i++)
        {
            var start = i * step;
            coveredEnd = Math.Max(coveredEnd, start + chunks[i].Length);
        }

        coveredEnd.ShouldBe(text.Length);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public void Gecersiz_chunkSize_hata_verir(int chunkSize, int overlap)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TextChunker.Split("x", chunkSize, overlap));
    }

    [Fact]
    public void Ortusme_chunkSize_esit_veya_buyukse_hata_verir()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TextChunker.Split("x", chunkSize: 10, chunkOverlap: 10));
        Should.Throw<ArgumentOutOfRangeException>(() => TextChunker.Split("x", chunkSize: 10, chunkOverlap: 11));
    }
}
