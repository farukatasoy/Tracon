using System.Buffers.Binary;

namespace AgentPrism;

/// <summary>The resolution-ready form of an accumulated speech utterance.</summary>
/// <param name="Data">The audio bytes. A WAV header has been added for PCM input.</param>
/// <param name="MediaType">The MIME type.</param>
/// <param name="Duration">
/// The audio's duration; <strong>computable</strong> only for raw PCM
/// (bytes/rate). In a compressed container the duration can only be found by
/// decoding, so it is <see langword="null"/> and the provider-reported
/// duration is used.
/// </param>
internal readonly record struct VoiceUtterance(byte[] Data, string MediaType, TimeSpan? Duration);

/// <summary>
/// Accumulates binary audio frames from the client into a speech utterance.
/// </summary>
/// <remarks>
/// <para>
/// Two limits are enforced, and both are needed: the <em>duration</em> limit
/// engages when the client's VAD never triggers, and the <em>byte</em> limit
/// engages when the client sends random data instead of audio. Either one
/// alone fails to catch the other's failure mode.
/// </para>
/// <para>
/// The class is concurrent and carries no lock; it is fed from a single receive loop.
/// </para>
/// </remarks>
internal sealed class VoiceUtteranceBuffer
{
    /// <summary>Bytes per PCM sample (16-bit mono).</summary>
    private const int PcmBytesPerSample = 2;

    private readonly List<byte[]> _chunks = [];
    private readonly string _format;
    private readonly int _sampleRate;
    private readonly int _maxBytes;
    private readonly long _maxPcmBytes;

    /// <summary>Sets up a new buffer.</summary>
    /// <param name="format">See <see cref="VoiceAudioFormats"/>.</param>
    /// <param name="sampleRate">The raw PCM's sample rate (Hz).</param>
    /// <param name="maxBytes">The maximum bytes per utterance.</param>
    /// <param name="maxDuration">The maximum duration per utterance.</param>
    public VoiceUtteranceBuffer(string format, int sampleRate, int maxBytes, TimeSpan maxDuration)
    {
        _format = string.IsNullOrWhiteSpace(format) ? VoiceAudioFormats.WebmOpus : format;
        _sampleRate = sampleRate > 0 ? sampleRate : 16_000;
        _maxBytes = Math.Max(1024, maxBytes);
        _maxPcmBytes = (long)(maxDuration.TotalSeconds * _sampleRate * PcmBytesPerSample);
    }

    /// <summary>The number of accumulated bytes.</summary>
    public int Length { get; private set; }

    /// <summary>Whether the buffer holds any audio.</summary>
    public bool HasAudio => Length > 0;

    /// <summary>
    /// Whether one of the limits has been exceeded.
    /// </summary>
    /// <remarks>
    /// The duration limit can only be computed from the byte count for raw
    /// PCM. In compressed containers we accept that the duration is
    /// <em>unknown</em>; protection there comes only from the byte limit, and
    /// this is documented.
    /// </remarks>
    public bool IsFull => Length >= _maxBytes || (IsPcm && _maxPcmBytes > 0 && Length >= _maxPcmBytes);

    private bool IsPcm => string.Equals(_format, VoiceAudioFormats.Pcm16, StringComparison.OrdinalIgnoreCase);

    /// <summary>Appends an audio chunk.</summary>
    /// <param name="chunk">The raw bytes.</param>
    /// <returns>
    /// <see langword="true"/> if the chunk was appended; <see langword="false"/>
    /// if the limit was already full (the chunk is <strong>discarded</strong>).
    /// </returns>
    public bool Append(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty || IsFull)
        {
            return false;
        }

        var remaining = _maxBytes - Length;
        var take = Math.Min(remaining, chunk.Length);

        _chunks.Add(chunk[..take].ToArray());
        Length += take;

        return true;
    }

    /// <summary>Clears the buffer.</summary>
    public void Clear()
    {
        _chunks.Clear();
        Length = 0;
    }

    /// <summary>
    /// Converts the accumulated audio into a resolution-ready file and clears the buffer.
    /// </summary>
    /// <returns>The utterance; <see langword="null"/> if there is no audio.</returns>
    public VoiceUtterance? Take()
    {
        if (Length == 0)
        {
            return null;
        }

        var payload = new byte[Length];
        var offset = 0;

        foreach (var chunk in _chunks)
        {
            chunk.CopyTo(payload, offset);
            offset += chunk.Length;
        }

        Clear();

        if (!IsPcm)
        {
            // Concatenating WebM chunks produces a valid container; the
            // container's header is in the first chunk.
            return new VoiceUtterance(payload, "audio/webm", null);
        }

        var seconds = (double)payload.Length / (_sampleRate * PcmBytesPerSample);

        return new VoiceUtterance(
            WriteWaveFile(payload, _sampleRate),
            "audio/wav",
            TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// Writes a 44-byte WAV (RIFF) header in front of raw 16-bit mono PCM.
    /// </summary>
    /// <param name="pcm">The raw samples.</param>
    /// <param name="sampleRate">The sample rate (Hz).</param>
    /// <returns>A valid WAV file.</returns>
    /// <remarks>
    /// <para>
    /// Raw PCM alone is <strong>not</strong> a file: the resolution
    /// endpoint receives it as a file inside <c>multipart/form-data</c> and
    /// recognizes its type from the header. If the header is not written,
    /// the provider either rejects the audio or decodes it at the wrong speed.
    /// </para>
    /// <para>
    /// The header is written by hand; no audio library is <strong>taken on</strong>.
    /// The 44 bytes are fixed, and the format has not changed in thirty years.
    /// </para>
    /// </remarks>
    internal static byte[] WriteWaveFile(ReadOnlySpan<byte> pcm, int sampleRate)
    {
        const int headerLength = 44;
        const short channels = 1;
        const short bitsPerSample = 16;

        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var file = new byte[headerLength + pcm.Length];
        var span = file.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)(36 + pcm.Length));
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], bitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)pcm.Length);

        pcm.CopyTo(span[headerLength..]);

        return file;
    }
}
