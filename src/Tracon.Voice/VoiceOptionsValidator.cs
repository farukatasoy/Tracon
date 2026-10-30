using Microsoft.Extensions.Options;

namespace Tracon;

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
/// <para>
/// The provider name, API key and address rules belong to the built-in ElevenLabs
/// client. They apply only while that client serves synthesis or transcription
/// (<see cref="VoiceProviderBinding"/>). A consumer that registered both contracts
/// before <c>UseVoice</c> needs no key. The format, limit, concurrency and
/// timeout rules always apply: the tools pass those values to any implementation.
/// </para>
/// </remarks>
internal sealed class VoiceOptionsValidator : IValidateOptions<VoiceOptions>
{
    private const string RegisterFirst =
        "To use your own provider, register ISpeechSynthesizer and ISpeechTranscriber BEFORE calling UseVoice; " +
        "a registration added after UseVoice is not seen.";

    private readonly VoiceProviderBinding _binding;

    /// <summary>Creates a validator.</summary>
    /// <param name="binding">Which contracts the consumer bound before <c>UseVoice</c>.</param>
    public VoiceOptionsValidator(VoiceProviderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        _binding = binding;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, VoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (_binding.UsesBuiltInClient)
        {
            ValidateBuiltInClient(options, ref failures);
        }

        if (options.MaxCharactersPerRequest <= 0)
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: character limit must be greater than zero.");
        }

        if (options.MaxDialogueCharactersPerRequest <= 0)
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: dialogue character limit must be greater than zero.");
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

        // The value becomes HttpClient.Timeout, which refuses more than
        // int.MaxValue milliseconds when the client is built.
        if (options.Timeout is { } timeout && !TimerPeriod.IsValidHttpTimeout(timeout))
        {
            (failures ??= []).Add(TimerPeriod.HttpTimeoutFailure(
                $"{nameof(VoiceOptions)}.{nameof(VoiceOptions.Timeout)}",
                timeout));
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Applies the rules of the built-in ElevenLabs client.</summary>
    private void ValidateBuiltInClient(VoiceOptions options, ref List<string>? failures)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: provider name cannot be empty.");
        }
        else if (!string.Equals(options.Provider, VoiceProviderNames.ElevenLabs, StringComparison.OrdinalIgnoreCase))
        {
            (failures ??= []).Add(
                $"{nameof(VoiceOptions)}: provider '{options.Provider}' is not recognized. " +
                $"Built-in provider: '{VoiceProviderNames.ElevenLabs}'. " +
                RegisterFirst);
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(VoiceOptions)}: no API key was given, and {DescribeBuiltInUse()}. " +
                "The key is a secret: `dotnet user-secrets set \"Tracon:Voice:ApiKey\" \"...\"`. " +
                RegisterFirst);
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: the address must be absolute.");
        }
    }

    private string DescribeBuiltInUse()
        => (_binding.CustomSynthesizer, _binding.CustomTranscriber) switch
        {
            (true, false) => "the built-in ElevenLabs client serves ISpeechTranscriber",
            (false, true) => "the built-in ElevenLabs client serves ISpeechSynthesizer",
            _ => "the built-in ElevenLabs client serves both ISpeechSynthesizer and ISpeechTranscriber",
        };

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
