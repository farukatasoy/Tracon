using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Builds the built-in voice client for the options a host gave.</summary>
/// <remarks>
/// A host that names the configuration keys of the key and the address gets a client that reads them when a request is
/// made; every other host gets the client it always had.
/// </remarks>
internal static class ElevenLabsClientFactory
{
    /// <summary>Creates the client.</summary>
    /// <param name="options">The voice options.</param>
    /// <param name="configuration">The configuration the names are read from.</param>
    /// <param name="loggerFactory">The logger factory a warning is written with.</param>
    /// <param name="httpClient">The HTTP client to use, or <see langword="null"/> to build one; a test passes a fake handler.</param>
    /// <returns>The client.</returns>
    internal static ElevenLabsSpeechClient Create(
        VoiceOptions options,
        IConfiguration? configuration,
        ILoggerFactory? loggerFactory,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKeyConfigurationName) && string.IsNullOrWhiteSpace(options.EndpointConfigurationName))
        {
            return new ElevenLabsSpeechClient(options, httpClient);
        }

        var source = new SetupCredentialSource(
            VoiceProviderNames.ElevenLabs,
            ElevenLabsSpeechClient.ApiKeyOptionName,
            options.ApiKey,
            options.Endpoint ?? ElevenLabsSpeechClient.DefaultEndpoint,
            options.ApiKeyConfigurationName,
            options.EndpointConfigurationName,
            configuration,
            loggerFactory?.CreateLogger("Tracon.Voice"));

        return new ElevenLabsSpeechClient(
            options,
            httpClient,
            target: () =>
            {
                // One read: the key and the address of a request belong together.
                var snapshot = source.Current();

                return new ElevenLabsSpeechClient.ElevenLabsTarget(
                    snapshot.ApiKey,
                    Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : ElevenLabsSpeechClient.DefaultEndpoint);
            });
    }
}
