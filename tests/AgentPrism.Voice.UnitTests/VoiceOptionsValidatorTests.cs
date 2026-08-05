using Shouldly;

namespace AgentPrism.Voice.UnitTests;

/// <summary>Ayar dogrulamasi uygulama BASLARKEN calisir; hatalar erken cikar.</summary>
public sealed class VoiceOptionsValidatorTests
{
    [Fact]
    public void Gecerli_ayar_kabul_edilir()
    {
        Validate(new VoiceOptions { ApiKey = "k", DefaultVoiceId = "v" }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Anahtarsiz_kayit_user_secrets_komutunu_soyler()
    {
        var result = Validate(new VoiceOptions());

        result.Failed.ShouldBeTrue();
        string.Join('\n', result.Failures ?? []).ShouldContain("user-secrets");
    }

    [Fact]
    public void Saklanamayan_cikti_bicimi_REDDEDILIR()
    {
        // 🚨 Ham PCM dosya basligi tasimaz; ek deposu turu sihirli bayttan
        // dogrular ve boyle bir icerigi reddeder. Hata calisma aninda degil,
        // uygulama baslarken cikmalidir.
        foreach (var format in (string[])["pcm_16000", "PCM_24000", "ulaw_8000", "alaw_8000"])
        {
            var result = Validate(new VoiceOptions { ApiKey = "k", OutputFormat = format });

            result.Failed.ShouldBeTrue($"'{format}' saklanamaz olmasina ragmen kabul edildi.");
            string.Join('\n', result.Failures ?? []).ShouldContain("sihirli bayt");
        }
    }

    [Fact]
    public void Konteynerli_bicimler_kabul_edilir()
    {
        foreach (var format in (string[])["mp3_44100_128", "opus_48000_128", "wav_44100"])
        {
            VoiceOptionsValidator.IsStorableFormat(format).ShouldBeTrue(format);
        }
    }

    [Fact]
    public void Taninmayan_saglayici_kendi_uygulamanizi_kaydedin_der()
    {
        var result = Validate(new VoiceOptions { ApiKey = "k", Provider = "yokboyle" });

        result.Failed.ShouldBeTrue();

        var text = string.Join('\n', result.Failures ?? []);
        text.ShouldContain("elevenlabs");
        text.ShouldContain("ISpeechSynthesizer");
    }

    [Fact]
    public void Sifir_veya_negatif_sinirlar_reddedilir()
    {
        Validate(new VoiceOptions { ApiKey = "k", MaxCharactersPerRequest = 0 }).Failed.ShouldBeTrue();
        Validate(new VoiceOptions { ApiKey = "k", MaxConcurrentRequests = 0 }).Failed.ShouldBeTrue();
        Validate(new VoiceOptions { ApiKey = "k", Timeout = TimeSpan.Zero }).Failed.ShouldBeTrue();
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(VoiceOptions options)
        => new VoiceOptionsValidator().Validate(name: null, options);
}
