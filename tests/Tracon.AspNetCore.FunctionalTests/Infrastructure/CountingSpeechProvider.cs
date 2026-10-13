using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A speech provider that counts its calls and keeps the credential each call
/// arrived with. No network.
/// </summary>
/// <remarks>
/// A refusal test asserts <see cref="Calls"/> stays <c>0</c>: a check that answers
/// <c>403</c> after the provider already received the text proves nothing.
/// </remarks>
internal sealed class CountingSpeechProvider : ISpeechSynthesizer, ISpeechTranscriber
{
    /// <summary>A valid MP3 first frame (ID3 tag + frame sync).</summary>
    private static readonly byte[] Mp3 =
        [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00];

    private int _calls;

    public CountingSpeechProvider(string providerName, bool supportsTenantCredentials = false, string? reportedModel = null)
    {
        ProviderName = providerName;
        SupportsTenantCredentials = supportsTenantCredentials;
        ReportedModel = reportedModel;
    }

    /// <inheritdoc />
    public string ProviderName { get; }

    /// <inheritdoc />
    public bool SupportsTenantCredentials { get; }

    /// <summary>Gets the model the provider reports on its results.</summary>
    public string? ReportedModel { get; }

    /// <inheritdoc />
    public int MaxCharactersPerRequest => 5000;

    /// <summary>Gets the number of provider calls (synthesis, streaming, voice list, transcription).</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Gets each call's text (or <c>transcribe</c>/<c>voices</c>) with the key it carried.</summary>
    public ConcurrentQueue<(string Text, string? ApiKey)> Received { get; } = new();

    /// <inheritdoc />
    public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        Record(request.Text, request.Credential);

        return ValueTask.FromResult(new SpeechAudio
        {
            Data = Mp3,
            MediaType = "audio/mpeg",
            CharactersBilled = request.Text.Length,
            UsageSource = SpeechUsageSource.Provider,
            ModelId = ReportedModel,
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Record(request.Text, request.Credential);

        await Task.Yield();

        yield return Mp3;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
    {
        Record("voices", null);

        return ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>(
            [new VoiceDescriptor { VoiceId = ProviderName + "-voice", Name = ProviderName }]);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesWithCredentialAsync(
        ModelProviderCredential? credential,
        CancellationToken cancellationToken = default)
    {
        if (!SupportsTenantCredentials && credential is not null)
        {
            throw new NotSupportedException("A tenant credential reached a provider that does not support one.");
        }

        Record("voices", credential);

        return ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>(
            [new VoiceDescriptor { VoiceId = ProviderName + "-voice", Name = ProviderName }]);
    }

    /// <inheritdoc />
    public async ValueTask<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string mediaType,
        SpeechTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await audio.CopyToAsync(copy, cancellationToken);

        Record("transcribe", options?.Credential);

        return new SpeechTranscript
        {
            Text = "where is my order",
            LanguageCode = "en",
            AudioDuration = TimeSpan.FromSeconds(1),
            ModelId = ReportedModel,
        };
    }

    private void Record(string text, ModelProviderCredential? credential)
    {
        Interlocked.Increment(ref _calls);
        Received.Enqueue((text, credential?.ApiKey));
    }
}
