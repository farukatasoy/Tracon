using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A speech provider that voices dialogue, counts its calls and keeps the credential each
/// call arrived with. No network.
/// </summary>
/// <remarks>
/// The dialogue twin of <see cref="CountingSpeechProvider"/>: a refusal test asserts
/// <see cref="Calls"/> stays <c>0</c>. Single-voice calls are counted too, so a test can
/// show the dialogue path did not fall back to them.
/// </remarks>
internal sealed class CountingDialogueSpeechProvider : ISpeechSynthesizer, ISpeechDialogueSynthesizer
{
    /// <summary>A valid MP3 first frame (ID3 tag + frame sync).</summary>
    private static readonly byte[] Mp3 =
        [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00];

    private int _calls;

    public CountingDialogueSpeechProvider(string providerName, bool supportsTenantCredentials = false)
    {
        ProviderName = providerName;
        SupportsTenantCredentials = supportsTenantCredentials;
    }

    /// <inheritdoc cref="ISpeechSynthesizer.ProviderName" />
    public string ProviderName { get; }

    /// <inheritdoc cref="ISpeechDialogueSynthesizer.SupportsTenantCredentials" />
    public bool SupportsTenantCredentials { get; }

    /// <inheritdoc />
    public int MaxCharactersPerRequest => 5000;

    /// <inheritdoc />
    public SpeechDialogueCapabilities DialogueCapabilities { get; } = new()
    {
        MaxVoices = 2,
        MaxCharactersPerRequest = 2000,
        SupportsSegmentTimestamps = true,
    };

    /// <summary>Gets the number of provider calls of any kind.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Gets each call's text (turns joined by <c>|</c>) with the key it carried.</summary>
    public ConcurrentQueue<(string Text, string? ApiKey)> Received { get; } = new();

    /// <inheritdoc />
    public ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(
        SpeechDialogueRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!SupportsTenantCredentials && request.Credential is not null)
        {
            throw new NotSupportedException("A tenant credential reached a provider that does not support one.");
        }

        Record(string.Join('|', request.Turns.Select(static turn => turn.Text)), request.Credential);

        var characters = request.Turns.Sum(static turn => turn.Text.Length);

        return ValueTask.FromResult(new SpeechDialogueAudio
        {
            Audio = new SpeechAudio
            {
                Data = Mp3,
                MediaType = "audio/mpeg",
                CharactersBilled = characters,
                UsageSource = SpeechUsageSource.Provider,
                ModelId = "counting-dialogue-1",
            },
            Segments = request.IncludeTimestamps
                ? [.. request.Turns.Select(static (turn, index) => new SpeechDialogueSegment
                {
                    TurnIndex = index,
                    VoiceId = turn.VoiceId,
                    Start = TimeSpan.FromSeconds(index),
                    End = TimeSpan.FromSeconds(index + 1),
                })]
                : null,
        });
    }

    /// <inheritdoc />
    public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        Record(request.Text, request.Credential);

        return ValueTask.FromResult(new SpeechAudio { Data = Mp3, MediaType = "audio/mpeg" });
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

        return ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }

    private void Record(string text, ModelProviderCredential? credential)
    {
        Interlocked.Increment(ref _calls);
        Received.Enqueue((text, credential?.ApiKey));
    }
}
