using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Builds and caches <see cref="OpenAIChatClientFactory"/> instances by name for named
/// <see cref="OpenAIProviderOptions"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="OpenAIChatClientFactory"/> (and therefore one <c>OpenAIClient</c> and
/// one HTTP connection pool) is built <em>once</em> per name. Asking for the same name
/// more than once returns the same instance.
/// </para>
/// <para>
/// When <see cref="OpenAIProviderOptions.ApiKey"/> is empty (local servers) the
/// OpenAI client still expects a credential; it is built with a fixed placeholder. This
/// behaviour applies <strong>only</strong> through this cache — that is, only to the
/// providers registered with <c>UseOpenAICompatible()</c>. <c>UseOpenAI()</c> still does
/// not run without a key
/// (<see cref="OpenAIChatClientFactory.CreateClient(OpenAIProviderOptions, EgressSocketGuard, TimeProvider)"/> is unchanged).
/// </para>
/// </remarks>
internal sealed class OpenAINamedChatClientFactoryCache
{
    /// <summary>
    /// The fixed placeholder for compatible providers without a key. It is not a real
    /// secret; it is needed only because <c>OpenAIClient</c> rejects an empty credential.
    /// </summary>
    private const string PlaceholderApiKey = SetupCredentialSource.KeylessPlaceholder;

    private readonly IOptionsMonitor<OpenAIProviderOptions> _optionsMonitor;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly TimeProvider? _timeProvider;
    private readonly IConfiguration? _configuration;
    private readonly ConcurrentDictionary<string, Lazy<OpenAISetup>> _setups = new(StringComparer.OrdinalIgnoreCase);

    public OpenAINamedChatClientFactoryCache(
        IOptionsMonitor<OpenAIProviderOptions> optionsMonitor,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
        _configuration = configuration;
    }

    /// <summary>Returns the factory for the given name, building and caching it when absent.</summary>
    /// <param name="name">The name of the named options instance.</param>
    /// <returns>The factory the host started with.</returns>
    public OpenAIChatClientFactory Get(string name)
        => Setup(name).Initial;

    /// <summary>
    /// Returns the setup of the given options instance: the factory the host started with and, when the options name a
    /// configuration key for the key or the address, the live clients that follow them.
    /// </summary>
    /// <param name="name">The name of the options instance; <see cref="Options.DefaultName"/> for <c>UseOpenAI()</c>.</param>
    /// <returns>The same setup for the same name.</returns>
    public OpenAISetup Setup(string name)
        // The setup builds SDK clients and reads the configuration for the first time. Two callers that ask at once must
        // not build two of them, so the value is built once, by whoever reads it first.
        => _setups.GetOrAdd(name, static (key, self) => new Lazy<OpenAISetup>(() => self.CreateSetup(key)), this).Value;

    private OpenAISetup CreateSetup(string name)
    {
        var options = _optionsMonitor.Get(name);

        // The unnamed instance is the official service and needs a key; a named one is an OpenAI compatible server, which
        // may ask for none.
        var keyless = name.Length > 0;

        if (string.IsNullOrWhiteSpace(options.ApiKeyConfigurationName) && string.IsNullOrWhiteSpace(options.EndpointConfigurationName))
        {
            return new OpenAISetup(CreateFactory(options, options.ApiKey, options.Endpoint, keyless), live: null);
        }

        var providerName = name.Length == 0 ? OpenAIProviderNames.ChatCompletions : name;

        var source = new SetupCredentialSource(
            providerName,
            OpenAIChatClientFactory.ApiKeyOptionName,
            options.ApiKey,
            options.Endpoint,
            options.ApiKeyConfigurationName,
            options.EndpointConfigurationName,
            _configuration,
            _loggerFactory?.CreateLogger("Tracon.ModelProvider"),
            keyless);

        OpenAIChatClientFactory Build(ModelProviderCredential snapshot)
            => CreateFactory(
                options,
                keyless && string.Equals(snapshot.ApiKey, PlaceholderApiKey, StringComparison.Ordinal) ? null : snapshot.ApiKey,
                Uri.TryCreate(snapshot.Endpoint, UriKind.Absolute, out var endpoint) ? endpoint : null,
                keyless);

        var first = source.Current();
        var initial = Build(first);

        return new OpenAISetup(initial, new LiveSetup<OpenAIChatClientFactory>(source, first, initial, Build));
    }

    private OpenAIChatClientFactory CreateFactory(OpenAIProviderOptions options, string? apiKey, Uri? endpoint, bool keyless)
    {
        if (!string.IsNullOrWhiteSpace(apiKey) || !keyless)
        {
            // The setup address is the operator's own and is never put behind the outbound address guard.
            return new OpenAIChatClientFactory(
                new OpenAIProviderOptions
                {
                    ApiKey = apiKey,
                    Endpoint = endpoint,
                    DefaultModel = options.DefaultModel,
                    Organization = options.Organization,
                    Timeout = options.Timeout,
                    MaxRetries = options.MaxRetries,
                },
                _loggerFactory,
                _timeProvider);
        }

        var client = OpenAIChatClientFactory.CreateClient(new OpenAIProviderOptions
        {
            ApiKey = PlaceholderApiKey,
            Endpoint = endpoint,
            Organization = options.Organization,
            Timeout = options.Timeout,
            MaxRetries = options.MaxRetries,
        }, timeProvider: _timeProvider);

        return OpenAIChatClientFactory.FromClient(client, options.DefaultModel, _loggerFactory);
    }
}
