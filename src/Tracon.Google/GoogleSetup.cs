using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The chat client factory the Google provider was set up with and, when the host named the configuration keys of the
/// key and the address, the factories that follow them.
/// </summary>
/// <remarks>
/// <para>
/// Everything that sends the setup key asks this object for the factory at the time of the call: the chat clients, the
/// image generator, the speech synthesizer and the health check. When no configuration key is named
/// <see cref="Live"/> is <see langword="null"/> and <see cref="Initial"/> is the factory for the life of the host.
/// </para>
/// <para>
/// A factory owns its Google client and is <see cref="IDisposable"/>. The one the host started with is disposed with the
/// container. A factory that a changed key or address replaces is deliberately <strong>not</strong> disposed: a call that
/// began before the change still holds the client it was given, and a client disposed under a running call fails that
/// call. The collector releases it once the last such call has finished. A change is rare, so the cost is one client per
/// change.
/// </para>
/// </remarks>
internal sealed class GoogleSetup
{
    private GoogleSetup(GoogleChatClientFactory initial, LiveSetup<GoogleChatClientFactory>? live)
    {
        Initial = initial;
        Live = live;
    }

    /// <summary>Gets the factory the host started with.</summary>
    internal GoogleChatClientFactory Initial { get; }

    /// <summary>Gets the live setup, or <see langword="null"/> when the key and the address are fixed for the life of the host.</summary>
    internal LiveSetup<GoogleChatClientFactory>? Live { get; }

    /// <summary>Builds the setup of a set of options.</summary>
    /// <param name="options">The options the provider was set up with.</param>
    /// <param name="loggerFactory">The logger factory; the warning about an unusable value is written to it.</param>
    /// <param name="configuration">The configuration the names of the key and the address are read from.</param>
    /// <returns>The setup.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">No usable key is set up (<see cref="GoogleProviderOptions.ApiKey"/> is empty and the name gives none).</exception>
    internal static GoogleSetup Create(GoogleProviderOptions options, ILoggerFactory? loggerFactory, IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKeyConfigurationName) && string.IsNullOrWhiteSpace(options.EndpointConfigurationName))
        {
            return new GoogleSetup(new GoogleChatClientFactory(options, loggerFactory), live: null);
        }

        var source = new SetupCredentialSource(
            GoogleProviderNames.Google,
            GoogleChatClientFactory.ApiKeyOptionName,
            options.ApiKey,
            options.Endpoint,
            options.ApiKeyConfigurationName,
            options.EndpointConfigurationName,
            configuration,
            loggerFactory?.CreateLogger("Tracon.ModelProvider"));

        // The setup address is the operator's own and is never put behind the outbound address guard, so the factory is
        // built from options and not through the tenant path that attaches the guard to an address.
        GoogleChatClientFactory Build(ModelProviderCredential snapshot)
            => new(
                new GoogleProviderOptions
                {
                    ApiKey = string.Equals(snapshot.ApiKey, SetupCredentialSource.KeylessPlaceholder, StringComparison.Ordinal) ? null : snapshot.ApiKey,
                    Endpoint = Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
                    DefaultModel = options.DefaultModel,
                    ApiVersion = options.ApiVersion,
                    Timeout = options.Timeout,
                },
                loggerFactory);

        var first = source.Current();
        var initial = Build(first);

        return new GoogleSetup(initial, new LiveSetup<GoogleChatClientFactory>(source, first, initial, Build));
    }

    /// <summary>Returns the factory of the key and the address that are current now.</summary>
    /// <returns>The factory.</returns>
    internal GoogleChatClientFactory Current() => Live is null ? Initial : Live.Current();
}
