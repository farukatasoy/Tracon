namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>
/// Aga cikmadan gecerli bir MP3 dondurur.
/// </summary>
/// <remarks>
/// <para>
/// Test projesi <c>AgentPrism.Voice</c>'a referans <strong>VERMEZ</strong>.
/// Uclar yalnizca <see cref="ISpeechSynthesizer"/> soyutlamasini bilir; bu
/// sinifin calisiyor olmasi soyutlamanin dogru yerde (Abstractions) durdugunun
/// kanitidir.
/// </para>
/// <para>
/// Dondurulen baytlar bir ID3 etiketiyle baslar: ek deposu turu sihirli
/// bayttan dogrular ve etiketsiz rastgele bir icerik reddedilirdi.
/// </para>
/// </remarks>
internal sealed class StubSpeechSynthesizer : ISpeechSynthesizer, ISpeechTranscriber
{
    private static readonly byte[] Mp3 =
    [
        0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00,
    ];

    public string ProviderName => "test-ses";

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
    /// Gelen sesi sabit bir metne cevirir (Faz 29).
    /// </summary>
    /// <remarks>
    /// Konusma katmani cozum ve sentezi ayni ornekten alabilir; gercek bir
    /// kurulumda da tek bir saglayici ikisini birden sunar.
    /// </remarks>
    public ValueTask<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string mediaType,
        SpeechTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new SpeechTranscript
        {
            Text = "siparisim nerede",
            LanguageCode = "tr",
            AudioDuration = TimeSpan.FromSeconds(1),
        });

    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>(
        [
            new VoiceDescriptor { VoiceId = "ses-1", Name = "Test Sesi", Category = "premade" },
        ]);
}
