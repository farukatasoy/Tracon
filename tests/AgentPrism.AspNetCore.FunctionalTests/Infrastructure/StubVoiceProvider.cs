using System.Runtime.CompilerServices;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Aga cikmayan sahte ses saglayicisi: cozum ve sentez.
/// </summary>
/// <remarks>
/// Gercek ses saglayicisina cagri yapan test <strong>yoktur</strong> (Faz 3'ten
/// beri gecerli karar). Konusma katmani yalnizca soyutlamalari bilir; bu sinif
/// o soyutlamalarin sunucu tarafindan dogru kullanildigini kanitlar.
/// </remarks>
internal sealed class StubVoiceProvider : ISpeechTranscriber, ISpeechSynthesizer
{
    /// <summary>Gecerli bir MP3 ilk cercevesi (ID3 etiketi + cerceve senkronu).</summary>
    private static readonly byte[] Mp3 =
        [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00];

    /// <summary>Cozumun dondurecegi metin.</summary>
    public string Transcript { get; set; } = "siparisim nerede";

    /// <summary>Cozume gelen sesin MIME turu.</summary>
    public string? ReceivedMediaType { get; private set; }

    /// <summary>Cozume gelen sesin bayt sayisi.</summary>
    public int ReceivedBytes { get; private set; }

    /// <summary>Seslendirilen parcalar.</summary>
    public List<string> Spoken { get; } = [];

    /// <summary>Sentez baslamadan once beklenecek sure.</summary>
    /// <remarks>
    /// Kesinti (barge-in) testi, kesme mesajini yanit surerken gonderebilmek
    /// icin sentezin yavaslamasina ihtiyac duyar.
    /// </remarks>
    public TimeSpan SynthesisDelay { get; set; }

    /// <inheritdoc />
    public string ProviderName => "test-ses";

    public int MaxCharactersPerRequest => 5000;

    /// <inheritdoc />
    public async ValueTask<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string mediaType,
        SpeechTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await audio.CopyToAsync(copy, cancellationToken);

        ReceivedMediaType = mediaType;
        ReceivedBytes = (int)copy.Length;

        return new SpeechTranscript
        {
            Text = Transcript,
            LanguageCode = "tr",
            AudioDuration = TimeSpan.FromSeconds(1.5),
        };
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (SynthesisDelay > TimeSpan.Zero)
        {
            await Task.Delay(SynthesisDelay, cancellationToken);
        }

        lock (Spoken)
        {
            Spoken.Add(request.Text);
        }

        yield return Mp3;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>(
            [new VoiceDescriptor { VoiceId = "ses-1", Name = "Test" }]);
}
