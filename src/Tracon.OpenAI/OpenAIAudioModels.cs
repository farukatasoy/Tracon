namespace Tracon;

/// <summary>Rules both OpenAI speech clients share.</summary>
internal static class OpenAIAudioModels
{
    private static readonly string[] Containers = ["mp3", "opus", "aac", "flac", "wav", "pcm"];

    /// <summary>Refuses a request that names a model other than the one the client is bound to.</summary>
    /// <param name="requested">The model the request names, or empty.</param>
    /// <param name="bound">The model the client is bound to.</param>
    /// <exception cref="TraconException">The request names another model.</exception>
    /// <remarks>
    /// A client is bound to one model for its whole life. Taking the request's model silently would
    /// answer on one model and price the call as another (measured with the Microsoft.Extensions.AI
    /// adapter, which does exactly that).
    /// </remarks>
    public static void RefuseOtherModel(string? requested, string bound)
    {
        if (!string.IsNullOrWhiteSpace(requested)
            && !string.Equals(requested, bound, StringComparison.OrdinalIgnoreCase))
        {
            throw new TraconException(
                $"The OpenAI speech registration is bound to the model '{bound}' and the request names " +
                $"'{requested}'. Register another name with its own model " +
                "(OpenAISpeechOptions.SynthesizerName / TranscriberName), or leave the model out of the request.");
        }
    }

    /// <summary>Refuses provider settings, which OpenAI speech does not read.</summary>
    /// <param name="settings">The settings the request carries.</param>
    /// <exception cref="TraconException">There is at least one setting.</exception>
    /// <remarks>The key is named and the value never: a setting can carry anything.</remarks>
    public static void RefuseSettings(IEnumerable<string> settings)
    {
        foreach (var key in settings)
        {
            throw new TraconException(
                $"OpenAI speech supports no provider settings, and the request carries '{key}'. " +
                "Remove it; a setting the service does not read is refused, never ignored.");
        }
    }

    /// <summary>Gets the media type of a container.</summary>
    /// <param name="container">The container name, or <see langword="null"/> for the service default (<c>mp3</c>).</param>
    /// <returns>The media type.</returns>
    public static string MediaType(string? container)
        => container switch
        {
            "opus" => "audio/ogg",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "wav" => "audio/wav",
            "pcm" => "audio/pcm",
            _ => "audio/mpeg",
        };

    /// <summary>Reads an OpenAI container name from a provider format name.</summary>
    /// <param name="format">The format name, or empty.</param>
    /// <returns>The container name, or <see langword="null"/> when none was asked for.</returns>
    /// <exception cref="TraconException">The name is not an OpenAI container.</exception>
    /// <remarks>
    /// <c>speak</c> sends the configured ElevenLabs-style name (<c>mp3_44100_128</c>) to whatever the
    /// default synthesizer is. OpenAI names the container only, so the part before the first
    /// underscore is read as the format.
    /// </remarks>
    public static string? ContainerName(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        var name = format.AsSpan().Trim();
        var underscore = name.IndexOf('_');
        var container = (underscore < 0 ? name : name[..underscore]).ToString().ToLowerInvariant();

        return Array.IndexOf(Containers, container) >= 0
            ? container
            : throw new TraconException(
                $"OpenAI speech cannot produce the format '{format}'. Use one of: {string.Join(", ", Containers)}. " +
                "A name with a suffix, such as 'mp3_44100_128', is read as its first part.");
    }

    /// <summary>Names an audio file for the service from the first bytes of its content.</summary>
    /// <param name="head">The first bytes of the audio.</param>
    /// <returns>A file name whose extension is the container the bytes hold.</returns>
    /// <remarks>
    /// The Microsoft.Extensions.AI contract hands over a stream and no media type, and the service
    /// reads the container from the file name's extension. The bytes say what the container is.
    /// An unrecognised container is named <c>audio.mp3</c>, the name the Microsoft.Extensions.AI
    /// adapter always sends.
    /// </remarks>
    public static string FileNameFor(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("RIFF"u8) && head.Length >= 12 && head[8..12].SequenceEqual("WAVE"u8))
        {
            return "audio.wav";
        }

        if (head.StartsWith("OggS"u8))
        {
            return "audio.ogg";
        }

        if (head.StartsWith("fLaC"u8))
        {
            return "audio.flac";
        }

        if (head.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
        {
            return "audio.webm";
        }

        if (head.Length >= 8 && head[4..8].SequenceEqual("ftyp"u8))
        {
            return "audio.m4a";
        }

        return "audio.mp3";
    }
}
