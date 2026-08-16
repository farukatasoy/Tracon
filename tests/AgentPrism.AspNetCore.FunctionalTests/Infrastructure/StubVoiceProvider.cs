using System.Runtime.CompilerServices;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A fake voice provider that makes no network calls: transcription and synthesis.
/// </summary>
/// <remarks>
/// No test calls a real voice provider (a decision in force since Phase 3).
/// The voice layer only knows about the abstractions; this class proves that
/// the server uses those abstractions correctly.
/// </remarks>
internal sealed class StubVoiceProvider : ISpeechTranscriber, ISpeechSynthesizer
{
    /// <summary>A valid MP3 first frame (ID3 tag + frame sync).</summary>
    private static readonly byte[] Mp3 =
        [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00];

    /// <summary>The text the transcription returns.</summary>
    public string Transcript { get; set; } = "where is my order";

    /// <summary>The MIME type of the audio received for transcription.</summary>
    public string? ReceivedMediaType { get; private set; }

    /// <summary>The byte count of the audio received for transcription.</summary>
    public int ReceivedBytes { get; private set; }

    /// <summary>The chunks that were spoken.</summary>
    public List<string> Spoken { get; } = [];

    /// <summary>How long to wait before starting synthesis.</summary>
    /// <remarks>
    /// The barge-in test needs synthesis to slow down so it can send the
    /// interrupt message while the response is still in progress.
    /// </remarks>
    public TimeSpan SynthesisDelay { get; set; }

    /// <inheritdoc />
    public string ProviderName => "test-voice";

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
            LanguageCode = "en",
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
            [new VoiceDescriptor { VoiceId = "voice-1", Name = "Test" }]);
}
