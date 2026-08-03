namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>Webhook imzasinin testleri.</summary>
public sealed class WebhookSignerTests
{
    private const string Secret = "s3cret-signing-key";
    private const string Body = """{"event":"run.completed","runId":"019fc0"}""";

    private static readonly DateTimeOffset Timestamp =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Imza_sha256_onekiyle_uretilir()
    {
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);

        signature.ShouldStartWith("sha256=");

        // HMAC-SHA256 = 32 bayt = 64 onaltilik karakter.
        signature.Length.ShouldBe(7 + 64);
    }

    [Fact]
    public void Ayni_girdi_ayni_imzayi_uretir()
        => WebhookSigner.Sign(Body, Timestamp, Secret)
            .ShouldBe(WebhookSigner.Sign(Body, Timestamp, Secret));

    [Fact]
    public void Dogru_imza_dogrulanir()
    {
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);

        WebhookSigner.Verify(Body, Timestamp, Secret, signature).ShouldBeTrue();
    }

    [Fact]
    public void Zaman_damgasi_degisince_imza_degisir()
    {
        // 🚨 Zaman damgasi imzaya DAHILDIR; olmasaydi yakalanan bir istek
        // sonsuza kadar yeniden oynatilabilirdi (K-163).
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);
        var later = Timestamp.AddSeconds(1);

        string.Equals(WebhookSigner.Sign(Body, later, Secret), signature, StringComparison.Ordinal)
            .ShouldBeFalse();
        WebhookSigner.Verify(Body, later, Secret, signature).ShouldBeFalse();
    }

    [Fact]
    public void Govde_degisince_imza_dogrulanmaz()
    {
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);

        WebhookSigner.Verify("""{"event":"run.failed"}""", Timestamp, Secret, signature)
            .ShouldBeFalse();
    }

    [Fact]
    public void Yanlis_sir_dogrulanmaz()
    {
        var signature = WebhookSigner.Sign(Body, Timestamp, Secret);

        WebhookSigner.Verify(Body, Timestamp, "wrong-key", signature).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sha256=deadbeef")]
    public void Bos_veya_bozuk_imza_dogrulanmaz(string? signature)
        => WebhookSigner.Verify(Body, Timestamp, Secret, signature).ShouldBeFalse();

    [Fact]
    public void Bos_sir_ile_imzalamak_hata_verir()
        => Should.Throw<ArgumentException>(() => WebhookSigner.Sign(Body, Timestamp, ""));
}
