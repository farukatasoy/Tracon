using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="VoiceOptions"/> values while the application STARTS.
/// </summary>
/// <remarks>
/// Validation is hand-written: <c>ValidateDataAnnotations()</c> uses reflection and
/// breaks AOT compatibility.
/// <para>
/// The error message <strong>never carries</strong> the API key. Writing the value
/// while saying the key is wrong would hand the secret to whoever sees the error.
/// </para>
/// </remarks>
public sealed class VoiceOptionsValidator : IValidateOptions<VoiceOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, VoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: provider name cannot be empty.");
        }
        else if (!string.Equals(options.Provider, VoiceProviderNames.ElevenLabs, StringComparison.OrdinalIgnoreCase))
        {
            (failures ??= []).Add(
                $"{nameof(VoiceOptions)}: provider '{options.Provider}' is not recognized. " +
                $"Built-in provider: '{VoiceProviderNames.ElevenLabs}'. " +
                "To register your own implementation, register the ISpeechSynthesizer/ISpeechTranscriber " +
                "services BEFORE calling UseVoice; a registered implementation is preserved.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(VoiceOptions)}: no API key was given. " +
                "The key is a secret: `dotnet user-secrets set \"AgentPrism:Voice:ApiKey\" \"...\"`.");
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: the address must be absolute.");
        }

        if (options.MaxCharactersPerRequest <= 0)
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: character limit must be greater than zero.");
        }

        if (options.MaxConcurrentRequests <= 0)
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: concurrent request limit must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputFormat))
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: output format cannot be empty.");
        }
        else if (!IsStorableFormat(options.OutputFormat))
        {
            (failures ??= []).Add(
                $"{nameof(VoiceOptions)}: format '{options.OutputFormat}' cannot be stored as an attachment. " +
                "Raw PCM and u-law outputs carry no file header; the attachment store checks the type from " +
                $"the magic byte and rejects such content. Use a format that carries a container " +
                $"(for example '{VoiceOptions.DefaultOutputFormat}').");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: timeout must be greater than zero.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// States whether the format carries a container the attachment store can write.
    /// </summary>
    /// <param name="outputFormat">The provider's format name.</param>
    /// <returns><see langword="true"/> when storable.</returns>
    /// <remarks>
    /// <c>internal</c>: unit tests verify this without a network call. The rule
    /// looks at the format name's prefix — providers use names like
    /// <c>mp3_44100_128</c>, <c>pcm_16000</c>, <c>ulaw_8000</c>.
    /// </remarks>
    internal static bool IsStorableFormat(string outputFormat)
        => !outputFormat.StartsWith("pcm", StringComparison.OrdinalIgnoreCase)
           && !outputFormat.StartsWith("ulaw", StringComparison.OrdinalIgnoreCase)
           && !outputFormat.StartsWith("alaw", StringComparison.OrdinalIgnoreCase);
}
