using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The chat client factory the Anthropic provider was set up with, and, when the host named the configuration keys of the
/// key and the address, the clients that follow them.
/// </summary>
/// <remarks>
/// Everything that sends the setup key asks this object for the factory: the chat clients and the health check. When no
/// configuration key is named <see cref="Live"/> is <see langword="null"/> and <see cref="Initial"/> is the factory for
/// the life of the host.
/// </remarks>
internal sealed class AnthropicSetup
{
    private AnthropicSetup(AnthropicChatClientFactory initial, LiveSetup<AnthropicChatClientFactory>? live)
    {
        Initial = initial;
        Live = live;
    }

    /// <summary>Gets the factory the host started with.</summary>
    internal AnthropicChatClientFactory Initial { get; }

    /// <summary>Gets the live setup, or <see langword="null"/> when the key and the address are fixed for the life of the host.</summary>
    internal LiveSetup<AnthropicChatClientFactory>? Live { get; }

    /// <summary>Builds the setup of the options.</summary>
    /// <param name="options">The options the host started with.</param>
    /// <param name="loggerFactory">The logger factory the clients and the warning use.</param>
    /// <param name="timeProvider">The clock a date in a <c>Retry-After</c> header is read against.</param>
    /// <param name="configuration">The configuration the names of the key and the address are read from.</param>
    /// <returns>The setup; one per host.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    internal static AnthropicSetup Create(
        AnthropicProviderOptions options,
        ILoggerFactory? loggerFactory,
        TimeProvider? timeProvider,
        IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKeyConfigurationName) && string.IsNullOrWhiteSpace(options.EndpointConfigurationName))
        {
            return new AnthropicSetup(new AnthropicChatClientFactory(options, loggerFactory, timeProvider), live: null);
        }

        var source = new SetupCredentialSource(
            AnthropicProviderNames.Anthropic,
            AnthropicChatClientFactory.ApiKeyOptionName,
            options.ApiKey,
            options.Endpoint,
            options.ApiKeyConfigurationName,
            options.EndpointConfigurationName,
            configuration,
            loggerFactory?.CreateLogger("Tracon.ModelProvider"));

        // The setup address is the operator's own and is never put behind the outbound address guard: this constructor
        // takes no guard.
        AnthropicChatClientFactory Build(ModelProviderCredential snapshot)
            => new(
                new AnthropicProviderOptions
                {
                    ApiKey = snapshot.ApiKey,
                    Endpoint = Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
                    DefaultModel = options.DefaultModel,
                    DefaultMaxOutputTokens = options.DefaultMaxOutputTokens,
                    MaxRetries = options.MaxRetries,
                    Timeout = options.Timeout,
                },
                loggerFactory,
                timeProvider);

        var first = source.Current();
        var initial = Build(first);

        return new AnthropicSetup(initial, new LiveSetup<AnthropicChatClientFactory>(source, first, initial, Build));
    }
}
