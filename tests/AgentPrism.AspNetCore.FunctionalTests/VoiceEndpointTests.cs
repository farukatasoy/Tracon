using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// <c>/api/voice/*</c> uclarini dogrular.
/// </summary>
/// <remarks>
/// Test projesi <c>AgentPrism.Voice</c>'a referans <strong>VERMEZ</strong>: uclar
/// yalnizca <see cref="ISpeechSynthesizer"/> soyutlamasini bilir. Bu, soyutlamanin
/// dogru pakette (Abstractions) durdugunun kanitidir — K-174 deseni.
/// </remarks>
public sealed class VoiceEndpointTests
{
    [Fact]
    public async Task Saglayici_yoksa_uclar_501_doner()
    {
        // 404 DEGIL: yanlis adres ile eksik yapilandirma birbirine karismamalidir.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var voices = await host.Client.GetAsync(new Uri("/agentprism/api/voice/voices", UriKind.Relative));
        voices.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        using var health = await host.Client.GetAsync(new Uri("/agentprism/api/voice/health", UriKind.Relative));
        health.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Ses_listesi_soyutlamadan_okunur()
    {
        await using var host = await StartWithVoiceAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/voice/voices", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetArrayLength().ShouldBe(1);
        body[0].GetProperty("voiceId").GetString().ShouldBe("ses-1");
    }

    [Fact]
    public async Task Seslendirme_eki_yazar_ve_olcumu_yanitta_dondurur()
    {
        await using var host = await StartWithVoiceAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/voice/speak", UriKind.Relative),
            new { text = "merhaba dunya", sessionId = "oturum-1" });

        response.EnsureSuccessStatusCode();

        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        // 🚨 Oturum kimligi eke YAZILMALIDIR: bos birakilirsa saklama politikasi
        // eki sahipsiz sayar ve siler.
        body.GetProperty("attachment").GetProperty("sessionId").GetString().ShouldBe("oturum-1");
        body.GetProperty("attachment").GetProperty("mediaType").GetString().ShouldBe("audio/mpeg");

        // Olcum tool_invocations'a YAZILMAZ (calistirma yok) ama gorunmez de degildir.
        body.GetProperty("characters").GetInt32().ShouldBe("merhaba dunya".Length);
        body.GetProperty("isEstimated").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Yazilan_ek_indirilebilir_ve_ses_turunde_gelir()
    {
        await using var host = await StartWithVoiceAsync();

        using var speak = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/voice/speak", UriKind.Relative),
            new { text = "merhaba", sessionId = (string?)null });

        speak.EnsureSuccessStatusCode();

        var id = (await AgentPrismTestHost.ReadJsonAsync(speak))
            .GetProperty("attachment").GetProperty("id").GetString();

        using var download = await host.Client.GetAsync(
            new Uri($"/agentprism/api/attachments/{id}", UriKind.Relative));

        download.EnsureSuccessStatusCode();
        download.Content.Headers.ContentType?.MediaType.ShouldBe("audio/mpeg");
    }

    [Fact]
    public async Task Bos_metin_400_doner()
    {
        await using var host = await StartWithVoiceAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/voice/speak", UriKind.Relative),
            new { text = "   " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sinir_asan_metin_400_doner_ve_saglayiciya_ULASMAZ()
    {
        // HTTP operator ucu, SpeakTool'un (agent cagrisi) zaten uyguladigi
        // MaxCharactersPerRequest sinirini KENDI de uygulamaliydi — onceden
        // uygulamiyordu (bkz. ISpeechSynthesizer.MaxCharactersPerRequest).
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<ISpeechSynthesizer>(new LimitedSynthesizer()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/voice/speak", UriKind.Relative),
            new { text = "bu-metin-on-karakterden-uzun" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Contains("MaxCharactersPerRequest", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Saglayici_reddederse_govdesi_yanita_TASINMAZ()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<ISpeechSynthesizer>(new FailingSynthesizer()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/voice/speak", UriKind.Relative),
            new { text = "gizli-metin" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Contains("SAHTE-ANAHTAR", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task Basliksiz_ses_ek_olarak_YAZILMAZ()
    {
        // Saglayici beklenmeyen bir bicim dondurdugunde hata YAZMA aninda cikar.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<ISpeechSynthesizer>(new HeaderlessSynthesizer()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/voice/speak", UriKind.Relative),
            new { text = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    private static Task<AgentPrismTestHost> StartWithVoiceAsync()
        => AgentPrismTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<ISpeechSynthesizer>(new StubSynthesizer()));

    /// <summary>Gecerli bir MP3 dondurur (ID3 etiketi + cerceve).</summary>
    private sealed class StubSynthesizer : ISpeechSynthesizer
    {
        private static readonly byte[] Mp3 =
            [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFB];

        public string ProviderName => "test-ses";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new SpeechAudio
            {
                Data = Mp3,
                MediaType = "audio/mpeg",
                CharactersBilled = request.Text.Length,
                UsageSource = SpeechUsageSource.Provider,
            });

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return Mp3;
        }

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>(
                [new VoiceDescriptor { VoiceId = "ses-1", Name = "Test" }]);
    }

    /// <summary>Saglayicinin govdesini yankilayan bir hata firlatir.</summary>
    private sealed class FailingSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "test-ses";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new AgentPrismException("Ses uretilemedi: HTTP 401. API anahtari gecersiz.");

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new AgentPrismException("Ses uretilemedi: HTTP 401.");

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }

    /// <summary>Ham PCM dondurur: hicbir sihirli bayta uymaz.</summary>
    private sealed class HeaderlessSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "test-ses";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new SpeechAudio
            {
                Data = new byte[] { 0x01, 0x02, 0x03, 0x04 },
                MediaType = "audio/mpeg",
            });

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new byte[] { 0x01 };
        }

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }

    /// <summary>10 karakterlik yapay dusuk sinir tasir; sinir denetimini test eder.</summary>
    private sealed class LimitedSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "test-ses";

        public int MaxCharactersPerRequest => 10;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "Sinirin uzerindeki bir istek saglayiciya hic ULASMAMALIYDI.");

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "Sinirin uzerindeki bir istek saglayiciya hic ULASMAMALIYDI.");

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }
}
