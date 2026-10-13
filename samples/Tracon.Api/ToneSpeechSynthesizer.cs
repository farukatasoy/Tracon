using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Tracon.Api;

/// <summary>
/// A local speech synthesizer that needs no key: it "speaks" a text as a sine tone,
/// one short beep per word, and returns a WAV file.
/// </summary>
/// <remarks>
/// <para>
/// It stands in for a model a host runs itself (a GPU text-to-speech server, for
/// example) and shows the named registration: <c>AddSpeechSynthesizer("tone", ...)</c>
/// makes it selectable by name next to the default synthesizer that
/// <c>UseVoice()</c> binds. A request selects it with <c>"provider": "tone"</c>.
/// </para>
/// <para>
/// The tenant's egress policy names it by <see cref="ProviderName"/>. A local engine
/// is not exempt: a tenant whose policy does not name <c>tone</c> is refused before
/// a single sample is produced.
/// </para>
/// </remarks>
internal sealed class ToneSpeechSynthesizer : ISpeechSynthesizer
{
    /// <summary>The provider name the egress policy and the price table use.</summary>
    public const string Name = "tone";

    private const int SampleRate = 16_000;
    private const int MaxWords = 200;

    private static readonly IReadOnlyList<VoiceDescriptor> Voices =
    [
        new() { VoiceId = "low", Name = "Low tone (220 Hz)", Category = "local" },
        new() { VoiceId = "mid", Name = "Middle tone (440 Hz)", Category = "local" },
        new() { VoiceId = "high", Name = "High tone (880 Hz)", Category = "local" },
    ];

    /// <inheritdoc />
    public string ProviderName => Name;

    /// <inheritdoc />
    public int MaxCharactersPerRequest => 2_000;

    /// <inheritdoc />
    public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var wav = Render(request);

        return ValueTask.FromResult(new SpeechAudio
        {
            Data = wav,
            MediaType = "audio/wav",
            Duration = TimeSpan.FromSeconds((double)(wav.Length - 44) / (SampleRate * 2)),
            CharactersBilled = request.Text.Length,
            UsageSource = SpeechUsageSource.Provider,
            ModelId = "tone-1",
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IncludeTimestamps)
        {
            throw new NotSupportedException("The tone synthesizer does not report character timing.");
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        yield return Render(request);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Voices);

    private static byte[] Render(SpeechRequest request)
    {
        var frequency = request.VoiceId switch
        {
            "low" => 220d,
            "high" => 880d,
            _ => 440d,
        };

        var words = Math.Clamp(
            request.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
            1,
            MaxWords);

        // 120 ms of tone and 60 ms of silence per word.
        var toneSamples = SampleRate * 120 / 1000;
        var gapSamples = SampleRate * 60 / 1000;
        var samples = words * (toneSamples + gapSamples);
        var wav = new byte[44 + (samples * 2)];

        WriteHeader(wav, samples);

        var offset = 44;

        for (var word = 0; word < words; word++)
        {
            for (var index = 0; index < toneSamples; index++)
            {
                var value = (short)(Math.Sin(2 * Math.PI * frequency * index / SampleRate) * short.MaxValue * 0.3);

                BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(offset), value);
                offset += 2;
            }

            offset += gapSamples * 2;
        }

        return wav;
    }

    private static void WriteHeader(Span<byte> wav, int samples)
    {
        var dataBytes = samples * 2;

        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(wav[8..]);
        "fmt "u8.CopyTo(wav[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(wav[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav[34..], 16);
        "data"u8.CopyTo(wav[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(wav[40..], dataBytes);
    }
}

/// <summary>The settings of the <c>synthesize</c> workflow function node.</summary>
/// <remarks>
/// The node names the engine; the engine's key, if it has one, is never a setting.
/// </remarks>
internal sealed record SynthesizeSettings
{
    /// <summary>Gets the registration name of the synthesizer, or <see langword="null"/> for the default one.</summary>
    public string? Provider { get; init; }

    /// <summary>Gets the voice to use, or <see langword="null"/> for the synthesizer's default voice.</summary>
    public string? Voice { get; init; }
}
