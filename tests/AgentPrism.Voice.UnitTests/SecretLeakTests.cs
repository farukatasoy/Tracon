using System.Text.Json;
using AgentPrism.Voice.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace AgentPrism.Voice.UnitTests;

/// <summary>
/// API anahtarinin hicbir cikti yoluna sizmadigini dogrular.
/// </summary>
/// <remarks>
/// Desen dort saglayici paketinde de aynidir. Sir sizintisi derleme veya
/// calisma aninda hata vermez; yalnizca boyle bir test yakalar.
/// </remarks>
public sealed class SecretLeakTests
{
    private const string ApiKey = "SAHTE-SES-ANAHTARI-xyz789";

    [Fact]
    public void Ayar_sinifi_kendi_ToString_metodunu_TANIMLAMAZ()
    {
        // 🚨 K-035. Bir `record` olsaydi derleyicinin urettigi ToString tum
        // ozellikleri yazardi ve tek bir LogDebug cagrisi anahtari ifsa ederdi.
        var declaringType = typeof(VoiceOptions).GetMethod(nameof(ToString))!.DeclaringType;

        declaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public void Ayarin_metin_temsili_anahtari_TASIMAZ()
    {
        var options = new VoiceOptions { ApiKey = ApiKey };

        // Cagri yansimayla yapilir: dogrudan `options.ToString()` yazmak MA0150
        // verir ("varsayilan object.ToString kullanilacak") — ki bu zaten testin
        // KANITLAMAK istedigi sey. Analyzer'i bastirmak yerine cagriyi yansimaya
        // tasimak, hem kurali hem calisma ani davranisini korur.
        var text = (string?)typeof(VoiceOptions).GetMethod(nameof(ToString))!.Invoke(options, null);

        text.ShouldNotBeNull().ShouldNotContain(ApiKey);
    }

    [Fact]
    public void Dogrulama_mesaji_anahtari_ve_adresi_TASIMAZ()
    {
        var validator = new VoiceOptionsValidator();

        var result = validator.Validate(
            name: null,
            new VoiceOptions
            {
                ApiKey = ApiKey,
                Endpoint = new Uri("https://gizli-sunucu.ornek/"),
                Provider = "bilinmeyen-saglayici",
                OutputFormat = "pcm_16000",
            });

        result.Failed.ShouldBeTrue();

        var text = string.Join('\n', result.Failures ?? []);
        text.ShouldNotContain(ApiKey);
        text.ShouldNotContain("gizli-sunucu");
    }

    [Fact]
    public void Saglik_ciktisi_seri_hale_getirildiginde_anahtar_gorunmez()
    {
        var health = new VoiceHealth
        {
            ProviderName = VoiceProviderNames.ElevenLabs,
            IsHealthy = false,
            Latency = TimeSpan.FromMilliseconds(12),
            CheckedAt = DateTimeOffset.UnixEpoch,
            Detail = "Baglanti hatasi (ConnectionError).",
        };

        JsonSerializer.Serialize(health).ShouldNotContain(ApiKey);
    }

    [Fact]
    public async Task Gunluge_yazilan_hicbir_satir_anahtari_TASIMAZ()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger("test");

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
        {
            Content = new StringContent($$"""{"detail":"bad key {{ApiKey}}"}"""),
        });

        var options = new VoiceOptions { ApiKey = ApiKey, DefaultVoiceId = "ses-1" };
        using var client = new ElevenLabsSpeechClient(options, new HttpClient(handler));

        try
        {
            _ = await client.SynthesizeAsync(
                new SpeechRequest { Text = "merhaba" },
                TestContext.Current.CancellationToken);
        }
        catch (AgentPrismException exception)
        {
            // Uygulamanin yapacagi sey: hatayi gunluge yazmak.
            logger.LogError(exception, "Ses uretilemedi.");
        }

        loggerProvider.AllText.ShouldNotContain(ApiKey);
    }

    [Fact]
    public async Task Anahtar_yalnizca_ISTEK_basliginda_gecer_govdede_gecmez()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary([0x49, 0x44, 0x33, 0x04]));
        var options = new VoiceOptions { ApiKey = ApiKey, DefaultVoiceId = "ses-1" };
        using var client = new ElevenLabsSpeechClient(options, new HttpClient(handler));

        _ = await client.SynthesizeAsync(
            new SpeechRequest { Text = "merhaba" },
            TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();

        request.Headers["xi-api-key"].ShouldBe(ApiKey);
        (request.Body ?? string.Empty).ShouldNotContain(ApiKey);
        request.Uri.ToString().ShouldNotContain(ApiKey);
    }
}
