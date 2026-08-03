using System.Net;

namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>
/// SSRF korumasinin testleri — bu fazin en buyuk guvenlik riski.
/// </summary>
/// <remarks>
/// Testler ag erisimi gerektirmez: IP edebi adresleri dogrudan verilir, bu
/// yuzden DNS cozumlemesi devreye girmez ve testler cevrimdisi calisir.
/// </remarks>
public sealed class WebhookUrlValidatorTests
{
    private static readonly AgentPrismWebhookOptions Strict = new();

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]  // 🚨 bulut metadata ucu
    [InlineData("100.64.0.1")]       // CGNAT
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]        // multicast
    public void Ozel_ipv4_adresleri_ozel_sayilir(string address)
        => WebhookUrlValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]   // 172.16/12'nin hemen disi
    [InlineData("172.32.0.1")]   // 172.16/12'nin hemen disi
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    public void Genel_ipv4_adresleri_ozel_sayilmaz(string address)
        => WebhookUrlValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeFalse();

    [Theory]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("::")]
    public void Ozel_ipv6_adresleri_ozel_sayilir(string address)
        => WebhookUrlValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeTrue();

    [Fact]
    public void Ipv4e_eslenmis_ipv6_metadata_adresi_ozel_sayilir()
    {
        // 🚨 Denetimi atlatmanin klasik yolu: ::ffff:169.254.169.254
        var mapped = IPAddress.Parse("::ffff:169.254.169.254");

        WebhookUrlValidator.IsPrivate(mapped).ShouldBeTrue();
    }

    [Fact]
    public void Genel_ipv6_adresi_ozel_sayilmaz()
        => WebhookUrlValidator.IsPrivate(IPAddress.Parse("2001:4860:4860::8888")).ShouldBeFalse();

    [Fact]
    public void Https_adresi_kabul_edilir()
        => WebhookUrlValidator.ValidateFormat("https://example.com/hook", Strict)
            .IsAllowed.ShouldBeTrue();

    [Fact]
    public void Http_varsayilan_olarak_reddedilir()
    {
        var verdict = WebhookUrlValidator.ValidateFormat("http://example.com/hook", Strict);

        verdict.IsAllowed.ShouldBeFalse();
        verdict.Reason.ShouldNotBeNull().ShouldContain("https");
    }

    [Fact]
    public void Http_izin_verilse_bile_yalniz_loopback_kabul_edilir()
    {
        var permissive = new AgentPrismWebhookOptions { AllowInsecureHttp = true };

        WebhookUrlValidator.ValidateFormat("http://localhost:5080/hook", permissive)
            .IsAllowed.ShouldBeTrue();
        WebhookUrlValidator.ValidateFormat("http://127.0.0.1:5080/hook", permissive)
            .IsAllowed.ShouldBeTrue();

        // Disari acik bir adres, izin acik olsa bile reddedilir.
        var verdict = WebhookUrlValidator.ValidateFormat("http://example.com/hook", permissive);

        verdict.IsAllowed.ShouldBeFalse();
        verdict.Reason.ShouldNotBeNull().ShouldContain("loopback");
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com")]
    [InlineData("gopher://example.com")]
    public void Http_disi_semalar_reddedilir(string url)
        => WebhookUrlValidator.ValidateFormat(url, Strict).IsAllowed.ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    public void Bicimsiz_adres_reddedilir(string url)
        => WebhookUrlValidator.ValidateFormat(url, Strict).IsAllowed.ShouldBeFalse();

    [Fact]
    public async Task Ozel_ag_adresine_cozumlenen_hedef_reddedilir()
    {
        // IP edebi adresi: DNS'e cikilmaz.
        var verdict = await WebhookUrlValidator.ValidateResolvedAsync(
            "https://169.254.169.254/latest/meta-data/",
            Strict);

        verdict.IsAllowed.ShouldBeFalse();
        verdict.Reason.ShouldNotBeNull().ShouldContain("169.254.169.254");
    }

    [Fact]
    public async Task Ozel_ag_izni_acikken_hedef_kabul_edilir()
    {
        var permissive = new AgentPrismWebhookOptions { AllowPrivateNetworkTargets = true };

        var verdict = await WebhookUrlValidator.ValidateResolvedAsync(
            "https://10.0.0.5/hook",
            permissive);

        verdict.IsAllowed.ShouldBeTrue();
        verdict.ResolvedAddress.ShouldBe(IPAddress.Parse("10.0.0.5"));
    }

    [Fact]
    public async Task Loopback_hedefi_AllowInsecureHttp_acikken_teslim_edilebilir()
    {
        // 🚨 Regresyon (K-167): AllowInsecureHttp yalnizca SEMAYA izin verip
        // adresi reddederse yerel bir dinleyiciyi sinamak imkansizdir ve
        // kullanici tum ozel agi acan AllowPrivateNetworkTargets'a zorlanir.
        // Ornek uygulama calistirilinca yakalandi: teslim "Dropped" oldu.
        var localDev = new AgentPrismWebhookOptions { AllowInsecureHttp = true };

        var verdict = await WebhookUrlValidator.ValidateResolvedAsync(
            "http://127.0.0.1:5099/hook",
            localDev);

        verdict.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Loopback_izni_diger_ozel_araliklari_acmaz()
    {
        var localDev = new AgentPrismWebhookOptions { AllowInsecureHttp = true };

        // Loopback acildi; ama 10/8 ve metadata ucu HALA reddedilmelidir.
        (await WebhookUrlValidator.ValidateResolvedAsync("https://10.0.0.5/hook", localDev))
            .IsAllowed.ShouldBeFalse();
        (await WebhookUrlValidator.ValidateResolvedAsync("https://169.254.169.254/", localDev))
            .IsAllowed.ShouldBeFalse();
        (await WebhookUrlValidator.ValidateResolvedAsync("https://192.168.1.1/hook", localDev))
            .IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public void Loopback_varsayilan_kurulumda_reddedilir()
    {
        // AllowInsecureHttp kapaliyken loopback de ozel bir adrestir.
        WebhookUrlValidator.IsAllowedTarget(IPAddress.Loopback, Strict).ShouldBeFalse();
    }

    [Fact]
    public async Task Genel_adres_cozumlendiginde_donen_ip_tasinir()
    {
        var verdict = await WebhookUrlValidator.ValidateResolvedAsync("https://8.8.8.8/hook", Strict);

        verdict.IsAllowed.ShouldBeTrue();

        // 🚨 Donen adres baglantida KULLANILMALIDIR; adi yeniden cozmek DNS
        // yeniden baglama saldirisina kapi acar.
        verdict.ResolvedAddress.ShouldBe(IPAddress.Parse("8.8.8.8"));
    }
}
