using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The chat client factory the Azure OpenAI provider was set up with, and, when the host named the configuration keys of
/// the key and the address, the clients that follow them.
/// </summary>
/// <remarks>
/// Everything that sends the setup key asks this object for the factory when the call starts: the chat client, the image
/// generator, the health check. When no configuration key is named <see cref="Live"/> is <see langword="null"/> and
/// <see cref="Initial"/> is the factory for the life of the host.
/// </remarks>
internal sealed class AzureOpenAISetup(AzureOpenAIChatClientFactory initial, LiveSetup<AzureOpenAIChatClientFactory>? live)
{
    /// <summary>Gets the factory the host started with.</summary>
    internal AzureOpenAIChatClientFactory Initial { get; } = initial;

    /// <summary>Gets the live setup, or <see langword="null"/> when the key and the address are fixed for the life of the host.</summary>
    internal LiveSetup<AzureOpenAIChatClientFactory>? Live { get; } = live;

    /// <summary>Returns the factory of the key and the address that are current now.</summary>
    /// <returns>The factory.</returns>
    internal AzureOpenAIChatClientFactory Current() => Live is null ? Initial : Live.Current();

    /// <summary>Creates the setup of the options.</summary>
    /// <param name="options">The provider options.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="timeProvider">The clock the retry wait runs on.</param>
    /// <param name="configuration">The configuration the names are read from.</param>
    /// <returns>The setup; <see cref="Live"/> is set when the options name a configuration key.</returns>
    internal static AzureOpenAISetup Create(
        AzureOpenAIProviderOptions options,
        ILoggerFactory? loggerFactory,
        TimeProvider? timeProvider,
        IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKeyConfigurationName) && string.IsNullOrWhiteSpace(options.EndpointConfigurationName))
        {
            return new AzureOpenAISetup(new AzureOpenAIChatClientFactory(options, loggerFactory, timeProvider), live: null);
        }

        // A credential factory (Entra) wins over a key: the managed credential is not a key and has no configuration key.
        var managed = options.CredentialFactory is not null;

        var source = new SetupCredentialSource(
            AzureOpenAIProviderNames.AzureOpenAI,
            AzureOpenAIChatClientFactory.ApiKeyOptionName,
            options.ApiKey,
            options.Endpoint,
            managed ? null : options.ApiKeyConfigurationName,
            options.EndpointConfigurationName,
            configuration,
            loggerFactory?.CreateLogger("Tracon.ModelProvider"),
            keyless: managed);

        AzureOpenAIChatClientFactory Build(ModelProviderCredential snapshot)
            => new(
                new AzureOpenAIProviderOptions
                {
                    Endpoint = Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
                    ApiKey = managed ? null : snapshot.ApiKey,
                    CredentialFactory = options.CredentialFactory,
                    DefaultDeployment = options.DefaultDeployment,
                    Audience = options.Audience,
                    Timeout = options.Timeout,
                },
                loggerFactory,
                timeProvider);

        var initial = Build(source.Current());

        return new AzureOpenAISetup(initial, new LiveSetup<AzureOpenAIChatClientFactory>(source, initial, Build));
    }

    /// <summary>Creates the image generator of this setup.</summary>
    /// <param name="deploymentName">The Azure image deployment name.</param>
    /// <returns>A generator that shares the setup's client; live setups ask for the current client every time it generates.</returns>
#pragma warning disable MEAI001
    internal IImageGenerator CreateImageGenerator(string deploymentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName);

        return Live is null
            ? Initial.CreateImageGenerator(deploymentName)
            : new OpenAIPipelineImageGenerator(() => Current().ImageTarget(), deploymentName, AzureOpenAIProviderNames.AzureOpenAI);
    }
#pragma warning restore MEAI001
}
