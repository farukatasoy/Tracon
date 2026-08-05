using System.Net;
using AgentPrism.Voice.UnitTests.Infrastructure;
using Shouldly;

namespace AgentPrism.Voice.UnitTests;

/// <summary>
/// ElevenLabs istemcisinin HTTP sozlesmesini sahte bir isleyiciyle dogrular.
/// Gercek saglayiciya HICBIR test cikmaz.
/// </summary>
public sealed class ElevenLabsSpeechClientTests
{
    /// <summary>Gecerli bir MP3 basligi: ID3 etiketi + en az bir bayt.</summary>
    private static readonly byte[] Mp3Bytes = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public async Task Seslendirme_dogru_yola_ve_basliga_gider()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler, options => options.ApiKey = "SAHTE-ANAHTAR-123");

        _ = await client.SynthesizeAsync(new SpeechRequest { Text = "merhaba" }, TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsolutePath.ShouldBe("/v1/text-to-speech/ses-1");
        request.Uri.Query.ShouldContain("output_format=mp3_44100_128");

        // 🚨 Kimlik dogrulama basligi `xi-api-key`; `Authorization: Bearer` DEGIL.
        request.Headers.ShouldContainKey("xi-api-key");
        request.Headers.ShouldNotContainKey("Authorization");

        request.ContentType.ShouldBe("application/json");
        request.Body.ShouldNotBeNull();
        request.Body.ShouldContain("\"text\":\"merhaba\"");
    }

    [Fact]
    public async Task Akisli_seslendirme_stream_yoluna_gider_ve_parcalari_dondurur()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler);

        var chunks = new List<int>();

        await foreach (var chunk in client.SynthesizeStreamingAsync(
                           new SpeechRequest { Text = "merhaba" },
                           TestContext.Current.CancellationToken))
        {
            chunks.Add(chunk.Length);
        }

        chunks.Sum().ShouldBe(Mp3Bytes.Length);
        handler.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/v1/text-to-speech/ses-1/stream");
    }

    [Fact]
    public async Task Ses_listesi_v2_ucundan_okunur_ve_ada_gore_siralanir()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """
            {"voices":[
              {"voice_id":"b","name":"Zeynep","category":"premade"},
              {"voice_id":"a","name":"Ahmet","category":"cloned"}
            ]}
            """));

        using var client = CreateClient(handler);

        var voices = await client.ListVoicesAsync(TestContext.Current.CancellationToken);

        // 🚨 v2: `/v1/voices` eski yuzeydir.
        handler.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/v2/voices");

        voices.Count.ShouldBe(2);
        voices[0].Name.ShouldBe("Ahmet");
        voices[1].Name.ShouldBe("Zeynep");
        voices[1].VoiceId.ShouldBe("b");
    }

    [Fact]
    public async Task Cozum_multipart_govdeyle_gonderilir()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """{"text":"merhaba dunya","language_code":"tr","language_probability":0.98,"audio_duration_secs":3.5}"""));

        using var client = CreateClient(handler);
        using var audio = new MemoryStream(Mp3Bytes);

        var transcript = await client.TranscribeAsync(
            audio,
            "audio/mpeg",
            cancellationToken: TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldBe("/v1/speech-to-text");

        // 🚨 Uc JSON govdesi KABUL ETMEZ; multipart zorunludur.
        request.ContentType.ShouldBe("multipart/form-data");
        request.Body.ShouldNotBeNull();
        request.Body.ShouldContain("model_id");

        transcript.Text.ShouldBe("merhaba dunya");
        transcript.LanguageCode.ShouldBe("tr");
        transcript.AudioDuration.ShouldBe(TimeSpan.FromSeconds(3.5));
    }

    [Fact]
    public async Task Ses_kimligi_yoksa_hata_acik_olur()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler, options => options.DefaultVoiceId = null);

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await client.SynthesizeAsync(
                new SpeechRequest { Text = "merhaba" },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("DefaultVoiceId");
        exception.Message.ShouldContain("list_voices");
    }

    [Fact]
    public async Task Basarisiz_yanit_govdeyi_hataya_TASIMAZ()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            // Saglayicinin govdesi istegi (ve bazen anahtar parcasini) yankilar.
            Content = new StringContent("""{"detail":"invalid api key SAHTE-ANAHTAR-123 for gizli-metin"}"""),
        });

        using var client = CreateClient(handler, options => options.ApiKey = "SAHTE-ANAHTAR-123");

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await client.SynthesizeAsync(
                new SpeechRequest { Text = "gizli-metin" },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("401");
        exception.Message.ShouldNotContain("SAHTE-ANAHTAR-123");
        exception.Message.ShouldNotContain("gizli-metin");
    }

    [Fact]
    public async Task Saglayici_karakter_bildirmezse_olcum_TAHMIN_isaretlenir()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler);

        var audio = await client.SynthesizeAsync(
            new SpeechRequest { Text = "yedi harf" },
            TestContext.Current.CancellationToken);

        audio.UsageSource.ShouldBe(SpeechUsageSource.Estimated);
        audio.CharactersBilled.ShouldBe("yedi harf".Length);
    }

    [Fact]
    public async Task Saglayici_karakter_bildirirse_olcum_SAGLAYICI_isaretlenir()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = StubHttpMessageHandler.Binary(Mp3Bytes);
            response.Headers.TryAddWithoutValidation("character-cost", "42");
            return response;
        });

        using var client = CreateClient(handler);

        var audio = await client.SynthesizeAsync(
            new SpeechRequest { Text = "merhaba" },
            TestContext.Current.CancellationToken);

        audio.UsageSource.ShouldBe(SpeechUsageSource.Provider);
        audio.CharactersBilled.ShouldBe(42);
    }

    [Fact]
    public async Task Saglik_denetimi_ses_listesini_okur_ve_ucret_uretmez()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """{"voices":[{"voice_id":"a","name":"Ahmet"}]}"""));

        using var client = CreateClient(handler);

        var health = await client.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.IsHealthy.ShouldBeTrue();
        health.VoiceCount.ShouldBe(1);
        handler.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/v2/voices");
    }

    [Fact]
    public async Task Saglik_denetimi_basarisizken_ne_anahtar_ne_adres_sizdirir()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("nope"),
        });

        using var client = CreateClient(
            handler,
            options =>
            {
                options.ApiKey = "SAHTE-ANAHTAR-123";
                options.Endpoint = new Uri("https://gizli-sunucu.ornek/");
            });

        var health = await client.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.IsHealthy.ShouldBeFalse();
        health.Detail.ShouldNotBeNull();
        health.Detail.ShouldNotContain("SAHTE-ANAHTAR-123");
        health.Detail.ShouldNotContain("gizli-sunucu");
    }

    [Fact]
    public void Bicim_MIME_turune_dogru_cevrilir()
    {
        ElevenLabsSpeechClient.MediaTypeForFormat("mp3_44100_128").ShouldBe("audio/mpeg");
        ElevenLabsSpeechClient.MediaTypeForFormat("opus_48000_128").ShouldBe("audio/ogg");
        ElevenLabsSpeechClient.MediaTypeForFormat("wav_44100").ShouldBe("audio/wav");

        // Bilinmeyen bicim sessizce yanlis bir tur DEGIL, saklanamaz bir tur verir.
        ElevenLabsSpeechClient.MediaTypeForFormat("pcm_16000").ShouldBe("application/octet-stream");
    }

    [Fact]
    public void Taban_adres_egik_cizgisiz_verilse_de_son_parca_korunur()
    {
        var combined = ElevenLabsSpeechClient.Combine(new Uri("https://ornek.test/vekil"), "v2/voices");

        combined.ToString().ShouldBe("https://ornek.test/vekil/v2/voices");
    }

    private static ElevenLabsSpeechClient CreateClient(
        StubHttpMessageHandler handler,
        Action<VoiceOptions>? configure = null)
    {
        var options = new VoiceOptions
        {
            ApiKey = "SAHTE-ANAHTAR",
            DefaultVoiceId = "ses-1",
        };

        configure?.Invoke(options);

        return new ElevenLabsSpeechClient(options, new HttpClient(handler));
    }
}
