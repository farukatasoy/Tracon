namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>
/// Returns a valid MP3 without reaching the network.
/// </summary>
/// <remarks>
/// <para>
/// The test project does <strong>NOT</strong> reference <c>Tracon.Voice</c>.
/// The endpoints only know the <see cref="ISpeechSynthesizer"/> abstraction; this
/// class working proves that the abstraction sits in the right place
/// (Abstractions).
/// </para>
/// <para>
/// The returned bytes start with an ID3 tag: the attachment store validates the
/// type from the magic byte, and untagged random content would be rejected.
/// </para>
/// </remarks>
internal sealed class StubSpeechSynthesizer : ISpeechSynthesizer, ISpeechTranscriber
{
    private static readonly byte[] Mp3 =
    [
        0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00,
    ];

    public string ProviderName => "test-speech";

    public int MaxCharactersPerRequest => 5000;

    public ValueTask<SpeechAudio> SynthesizeAsync(
        SpeechRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ValueTask.FromResult(new SpeechAudio
        {
            Data = Mp3,
            MediaType = "audio/mpeg",
            Duration = TimeSpan.FromSeconds(1),
            CharactersBilled = request.Text.Length,
            UsageSource = SpeechUsageSource.Provider,
        });
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        yield return Mp3;
    }

    /// <summary>
    /// Translates incoming audio into a fixed text (phase 29).
    /// </summary>
    /// <remarks>
    /// The voice layer can take transcription and synthesis from the same
    /// instance; a real setup also has a single provider offering both.
    /// </remarks>
    public ValueTask<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string mediaType,
        SpeechTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new SpeechTranscript
        {
            Text = "where is my order",
            LanguageCode = "en",
            AudioDuration = TimeSpan.FromSeconds(1),
        });

    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>(
        [
            new VoiceDescriptor { VoiceId = "voice-1", Name = "Test Voice", Category = "premade" },
        ]);
}
