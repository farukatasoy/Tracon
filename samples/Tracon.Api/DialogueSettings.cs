using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace Tracon.Api;

/// <summary>The settings of the <c>dialogue</c> workflow function node.</summary>
/// <remarks>
/// The node names the engine and the voices; the engine's key is never a setting.
/// <see cref="ProviderSettings"/> carries the engine's own request settings under its
/// prefix (for example <c>elevenlabs.stability</c>); an unknown key fails the node.
/// </remarks>
internal sealed record DialogueSettings
{
    /// <summary>Gets the registration name of the synthesizer, or <see langword="null"/> for the default one.</summary>
    public string? Provider { get; init; }

    /// <summary>Gets the voices, in the order they take turns: line 1 is voice 1, line 2 is voice 2, and so on.</summary>
    public required IReadOnlyList<string> Voices { get; init; }

    /// <summary>Gets the dialogue model, or <see langword="null"/> for the synthesizer's default.</summary>
    public string? ModelId { get; init; }

    /// <summary>Gets the language's ISO 639-1 code, or <see langword="null"/> to let the engine detect it.</summary>
    public string? LanguageCode { get; init; }

    /// <summary>Gets the engine's output format (for example <c>pcm_24000</c>), or <see langword="null"/> for its default.</summary>
    public string? OutputFormat { get; init; }

    /// <summary>Gets whether each turn's time range is returned.</summary>
    public bool IncludeTimestamps { get; init; }

    /// <summary>Gets the engine's own request settings, keyed under its prefix.</summary>
    public Dictionary<string, JsonElement>? ProviderSettings { get; init; }
}

/// <summary>Wraps raw 16-bit mono PCM in a WAV header, so the attachment store can keep it.</summary>
/// <remarks>
/// A dialogue engine returns <c>pcm_*</c> without a header; joining several results and
/// adding the header is the caller's job (Tracon does not join audio). The sample rate
/// comes from the format name: <c>pcm_24000</c> is 24 kHz.
/// </remarks>
internal static class PcmWave
{
    /// <summary>Returns a WAV file for raw PCM, or <see langword="null"/> when the format is not raw PCM.</summary>
    /// <param name="outputFormat">The engine's format name.</param>
    /// <param name="pcm">The raw samples.</param>
    /// <returns>The WAV bytes, or <see langword="null"/>.</returns>
    public static byte[]? Wrap(string? outputFormat, ReadOnlySpan<byte> pcm)
    {
        if (outputFormat is null
            || !outputFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(outputFormat.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sampleRate))
        {
            return null;
        }

        var wav = new byte[44 + pcm.Length];

        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + pcm.Length);
        "WAVE"u8.CopyTo(wav.AsSpan(8));
        "fmt "u8.CopyTo(wav.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), pcm.Length);
        pcm.CopyTo(wav.AsSpan(44));

        return wav;
    }
}
