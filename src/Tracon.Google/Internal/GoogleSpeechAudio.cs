using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Tracon;

/// <summary>
/// The audio a Gemini text-to-speech answer carries, read into 16-bit PCM, and the WAV file that
/// holds it.
/// </summary>
/// <remarks>
/// <para>
/// Measured against the live API (2026-10-25): the container depends on the model. The
/// <c>gemini-3.8</c> models answer a WAV file (<c>audio/wav</c>), and append a <c>C2PA</c> chunk with
/// the provenance manifest after the sample data. <c>gemini-2.5-flash-preview-tts</c> answers raw PCM
/// as <c>audio/L16;codec=pcm;rate=24000</c>, and <c>gemini-3.1-flash-tts-preview</c> as
/// <c>audio/l16; rate=24000; channels=1</c>. A streamed answer is raw PCM for every model measured.
/// Whatever the container, the samples are 16-bit little-endian, though RFC 2586 says
/// <c>L16</c> is big-endian.
/// </para>
/// <para>
/// A WAV file is kept as the model sent it, so the <c>C2PA</c> chunk reaches a caller that asks for
/// <c>wav</c>; raw PCM is wrapped in a 44-byte header here.
/// </para>
/// </remarks>
internal sealed class GoogleSpeechAudio
{
    private const int BytesPerSample = 2;
    private const int HeaderSize = 44;

    private GoogleSpeechAudio()
    {
    }

    /// <summary>Gets the samples: 16-bit little-endian PCM, channels interleaved.</summary>
    public required ReadOnlyMemory<byte> Samples { get; init; }

    /// <summary>Gets the samples per second, per channel.</summary>
    public required int SampleRate { get; init; }

    /// <summary>Gets the number of interleaved channels.</summary>
    public required int Channels { get; init; }

    /// <summary>Gets the whole WAV file when the model answered one; <see langword="null"/> for raw PCM.</summary>
    public ReadOnlyMemory<byte>? Wav { get; init; }

    /// <summary>Gets the length of the audio.</summary>
    public TimeSpan Duration
        => TimeSpan.FromSeconds((double)Samples.Length / (SampleRate * Channels * BytesPerSample));

    /// <summary>Gets the media type of <see cref="Samples"/> as raw PCM.</summary>
    public string PcmMediaType
        => string.Create(CultureInfo.InvariantCulture, $"audio/L16;rate={SampleRate};channels={Channels}");

    /// <summary>Reads the audio of one answer part.</summary>
    /// <param name="mimeType">The MIME type the answer states.</param>
    /// <param name="data">The bytes.</param>
    /// <returns>The audio.</returns>
    /// <exception cref="TraconException">The answer holds audio Tracon cannot read.</exception>
    public static GoogleSpeechAudio Read(string? mimeType, ReadOnlyMemory<byte> data)
    {
        var parts = (mimeType ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mediaType = parts.Length == 0 ? string.Empty : parts[0].ToLowerInvariant();

        return mediaType switch
        {
            "audio/wav" or "audio/x-wav" or "audio/wave" => ReadWav(data),
            "audio/l16" => ReadPcm(parts, data),
            _ => throw Unreadable($"the media type '{Display(mediaType)}' is not WAV or 16-bit PCM"),
        };
    }

    /// <summary>Strips the container from a streamed part, which is raw PCM or a WAV file.</summary>
    /// <param name="mimeType">The MIME type the part states.</param>
    /// <param name="data">The bytes.</param>
    /// <returns>The samples.</returns>
    /// <exception cref="TraconException">The part holds audio Tracon cannot read.</exception>
    public static ReadOnlyMemory<byte> ReadSamples(string? mimeType, ReadOnlyMemory<byte> data)
        => Read(mimeType, data).Samples;

    /// <summary>Gets the audio as a WAV file: the model's own file, or the samples in a new header.</summary>
    /// <returns>The file.</returns>
    public ReadOnlyMemory<byte> ToWav()
    {
        if (Wav is { } wav)
        {
            return wav;
        }

        var file = new byte[HeaderSize + Samples.Length];
        var header = file.AsSpan(0, HeaderSize);

        Encoding.ASCII.GetBytes("RIFF", header[0..4]);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], file.Length - 8);
        Encoding.ASCII.GetBytes("WAVEfmt ", header[8..16]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..22], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..24], (short)Channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..28], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..32], SampleRate * Channels * BytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..34], (short)(Channels * BytesPerSample));
        BinaryPrimitives.WriteInt16LittleEndian(header[34..36], BytesPerSample * 8);
        Encoding.ASCII.GetBytes("data", header[36..40]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..44], Samples.Length);
        Samples.Span.CopyTo(file.AsSpan(HeaderSize));

        return file;
    }

    private static GoogleSpeechAudio ReadPcm(string[] parameters, ReadOnlyMemory<byte> data)
    {
        int? rate = null;
        var channels = 1;

        foreach (var parameter in parameters.Skip(1))
        {
            var separator = parameter.IndexOf('=', StringComparison.Ordinal);

            if (separator < 0)
            {
                continue;
            }

            var name = parameter[..separator].Trim();
            var value = parameter[(separator + 1)..].Trim();

            if (name.Equals("rate", StringComparison.OrdinalIgnoreCase))
            {
                rate = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedRate) ? parsedRate : 0;
            }
            else if (name.Equals("channels", StringComparison.OrdinalIgnoreCase))
            {
                channels = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedChannels) ? parsedChannels : 0;
            }
        }

        // A guessed rate plays the audio at the wrong speed and says nothing: refuse instead.
        if (rate is not > 0)
        {
            throw Unreadable("the PCM media type states no sample rate");
        }

        if (channels < 1)
        {
            throw Unreadable("the PCM media type states a channel count below one");
        }

        return new GoogleSpeechAudio { Samples = data, SampleRate = rate.Value, Channels = channels };
    }

    private static GoogleSpeechAudio ReadWav(ReadOnlyMemory<byte> file)
    {
        var bytes = file.Span;

        if (bytes.Length < 12 || !bytes[0..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
        {
            throw Unreadable("the WAV file has no RIFF header");
        }

        (int Rate, int Channels)? format = null;
        ReadOnlyMemory<byte>? samples = null;
        var offset = 12;

        while (offset + 8 <= bytes.Length)
        {
            var tag = bytes.Slice(offset, 4);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            var body = offset + 8;
            var size = (int)Math.Min(declared, (uint)(bytes.Length - body));

            if (tag.SequenceEqual("fmt "u8) && size >= 16)
            {
                var tagValue = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(body, 2));
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(body + 2, 2));
                var rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(body + 4, 4));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(body + 14, 2));

                // 1 is integer PCM; 0xFFFE is the extensible form, which carries a sub-format Tracon does not read.
                if (tagValue != 1 || bits != BytesPerSample * 8 || channels < 1 || rate < 1)
                {
                    throw Unreadable("the WAV file is not 16-bit integer PCM");
                }

                format = (rate, channels);
            }
            else if (tag.SequenceEqual("data"u8))
            {
                samples = file.Slice(body, size);
            }

            // A chunk is padded to an even size; the C2PA chunk the model appends comes after the samples.
            offset = body + size + (size & 1);
        }

        if (format is not { } found || samples is not { } data)
        {
            throw Unreadable("the WAV file has no format chunk or no sample data");
        }

        return new GoogleSpeechAudio { Samples = data, SampleRate = found.Rate, Channels = found.Channels, Wav = file };
    }

    private static TraconException Unreadable(string reason)
        => new($"The Gemini answer holds audio Tracon cannot read: {reason}.");

    /// <summary>Shortens a MIME type the provider sent before it reaches a message.</summary>
    private static string Display(string mediaType)
        => mediaType.Length == 0 ? "(none)" : mediaType.Length > 64 ? mediaType[..64] : mediaType;
}
