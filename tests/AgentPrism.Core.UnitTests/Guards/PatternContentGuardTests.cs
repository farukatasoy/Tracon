using System.Diagnostics;
using System.Text.RegularExpressions;
using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Guards;

/// <summary>
/// Yerlesik desen tabanli guard'in kararlarini dogrular.
/// </summary>
/// <remarks>
/// En onemli testler <strong>yanlis pozitif</strong> testleridir: kontrol basamagi
/// dogrulamasi olmadan bir siparis numarasi kart sayilir ve guard ilk gunde
/// kapatilir.
/// </remarks>
public sealed class PatternContentGuardTests
{
    [Fact]
    public async Task Hicbir_kural_tanimli_degilse_hicbir_sey_denetlenmez()
    {
        var guard = Guard();

        var result = await Inspect(guard, "kart numaram 4539578763621486, e-posta ali@ornek.com");

        result.Action.ShouldBe(ContentGuardAction.Allow);
    }

    [Fact]
    public async Task Yasak_sozcuk_engellenir()
    {
        var guard = Guard(options => options.DeniedTerms.Add("gizli-proje"));

        var result = await Inspect(guard, "GIZLI-PROJE hakkinda bilgi ver");

        result.Action.ShouldBe(ContentGuardAction.Block);
        result.RuleName.ShouldBe("denied-term");
    }

    [Fact]
    public async Task Engelleme_sebebi_yasak_sozcugu_tasimaz()
    {
        // 🚨 Yasak sozcuk listesi de kurumsal bir sirdir: "gizli-proje" bir kod
        // adi olabilir ve sebep metni ProblemDetails icinde istemciye doner.
        var guard = Guard(options => options.DeniedTerms.Add("gizli-proje"));

        var result = await Inspect(guard, "gizli-proje nedir");

        result.Reason.ShouldNotBeNull();
        result.Reason!.ShouldNotContain("gizli-proje", Case.Insensitive);
    }

    [Fact]
    public async Task Gecerli_kart_numarasi_maskelenir()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.CreditCard);

        // 4539578763621486 gecerli bir Luhn dizisidir.
        var result = await Inspect(guard, "kart numaram 4539578763621486");

        result.Action.ShouldBe(ContentGuardAction.Mask);
        result.MaskedText.ShouldBe("kart numaram [redacted]");
        result.RuleName.ShouldBe("credit-card");
    }

    [Fact]
    public async Task Gruplanmis_kart_numarasi_da_maskelenir()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.CreditCard);

        var result = await Inspect(guard, "kart: 4539 5787 6362 1486");

        result.MaskedText.ShouldBe("kart: [redacted]");
    }

    [Fact]
    public async Task Gecersiz_Luhn_kontrollu_on_alti_hane_maskelenmez()
    {
        // 🚨 Siparis numarasi vakasi. Bu test dusmezse guard kullanilamaz.
        var guard = Guard(options => options.MaskedPii = PiiPatterns.CreditCard);

        var result = await Inspect(guard, "siparis numaram 1234567812345678");

        result.Action.ShouldBe(ContentGuardAction.Allow);
    }

    [Fact]
    public async Task Gecerli_TC_kimlik_numarasi_maskelenir()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.TurkishNationalId);

        // 10000000146 kontrol basamagi kurallarina uyar.
        var result = await Inspect(guard, "kimlik no 10000000146");

        result.Action.ShouldBe(ContentGuardAction.Mask);
        result.MaskedText.ShouldBe("kimlik no [redacted]");
        result.RuleName.ShouldBe("turkish-national-id");
    }

    [Fact]
    public async Task Rastgele_on_bir_hane_maskelenmez()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.TurkishNationalId);

        var result = await Inspect(guard, "takip numarasi 12345678901");

        result.Action.ShouldBe(ContentGuardAction.Allow);
    }

    [Fact]
    public async Task Sifirla_baslayan_on_bir_hane_maskelenmez()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.TurkishNationalId);

        var result = await Inspect(guard, "kod 01234567890");

        result.Action.ShouldBe(ContentGuardAction.Allow);
    }

    [Fact]
    public async Task E_posta_maskelenir()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.Email);

        var result = await Inspect(guard, "bana ali.veli@ornek.com.tr adresinden yaz");

        result.MaskedText.ShouldBe("bana [redacted] adresinden yaz");
    }

    [Fact]
    public async Task Iban_maskelenir()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.Iban);

        var result = await Inspect(guard, "hesap TR330006100519786457841326 numarali");

        result.MaskedText.ShouldBe("hesap [redacted] numarali");
    }

    [Theory]
    [InlineData("anahtar sk-abcdefghijklmnopqrstuvwx")]
    [InlineData("token ghp_abcdefghijklmnopqrstuvwxyz01")]
    [InlineData("erisim AKIAIOSFODNN7EXAMPLE")]
    public async Task Saglayici_anahtari_maskelenir(string text)
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.ProviderApiKey);

        var result = await Inspect(guard, text);

        result.Action.ShouldBe(ContentGuardAction.Mask);
        result.MaskedText.ShouldNotBeNull();
        result.MaskedText!.ShouldContain("[redacted]", Case.Sensitive);
    }

    [Fact]
    public async Task Birden_cok_aile_eslesirse_kural_adlari_birlestirilir()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.Email | PiiPatterns.CreditCard);

        var result = await Inspect(guard, "ali@ornek.com ve 4539578763621486");

        result.MaskedText.ShouldBe("[redacted] ve [redacted]");
        result.RuleName.ShouldNotBeNull();
        result.RuleName!.ShouldContain("email", Case.Sensitive);
        result.RuleName.ShouldContain("credit-card", Case.Sensitive);
    }

    [Fact]
    public async Task Yasak_sozcuk_maskelemeden_once_denetlenir()
    {
        // Block > Mask: ikisi de eslesiyorsa engelleme kazanir.
        var guard = Guard(options =>
        {
            options.DeniedTerms.Add("gizli");
            options.MaskedPii = PiiPatterns.CreditCard;
        });

        var result = await Inspect(guard, "gizli kart 4539578763621486");

        result.Action.ShouldBe(ContentGuardAction.Block);
    }

    [Fact]
    public async Task Maske_metni_yapilandirilabilir()
    {
        var guard = Guard(options =>
        {
            options.MaskedPii = PiiPatterns.Email;
            options.MaskReplacement = "<PII>";
        });

        var result = await Inspect(guard, "ali@ornek.com");

        result.MaskedText.ShouldBe("<PII>");
    }

    [Fact]
    public async Task Patolojik_girdi_calistirmayi_kilitlemez()
    {
        // Her desen 1000 ms zaman asimi tasir; ReDoS'a karsi tek savunma budur.
        // Bu test asilmayi degil, sicak yolun makul surede DONMESINI olcer:
        // desen ya eslesir ya zaman asimina ugrar, ama sonsuza kadar donmez.
        var guard = Guard(options => options.MaskedPii =
            PiiPatterns.Email | PiiPatterns.Iban | PiiPatterns.CreditCard |
            PiiPatterns.TurkishNationalId | PiiPatterns.ProviderApiKey);

        var pathological = new string('a', 20_000) + "@" + new string('b', 20_000);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Inspect(guard, pathological);
        }
        catch (RegexMatchTimeoutException)
        {
            // Kabul edilebilir sonuc: zaman asimi calistirmayi dusurur, kilitlemez.
        }

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    private static PatternContentGuard Guard(Action<PatternContentGuardOptions>? configure = null)
    {
        var options = new PatternContentGuardOptions();
        configure?.Invoke(options);

        return new PatternContentGuard(new StaticOptionsMonitor<PatternContentGuardOptions>(options));
    }

    private static async Task<ContentGuardResult> Inspect(PatternContentGuard guard, string text)
        => await guard.InspectAsync(
            new ContentGuardContext { Direction = ContentGuardDirection.Input, Text = text },
            TestContext.Current.CancellationToken);
}
