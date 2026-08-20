using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

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
/// (<see cref="OpenAIChatClientFactory.CreateClient(OpenAIProviderOptions, EgressSocketGuard)"/> is unchanged).
/// </para>
/// </remarks>
internal sealed class OpenAINamedChatClientFactoryCache
{
    /// <summary>
    /// The fixed placeholder for compatible providers without a key. It is not a real
    /// secret; it is needed only because <c>OpenAIClient</c> rejects an empty credential.
    /// </summary>
    private const string PlaceholderApiKey = "no-key-required";

    private readonly IOptionsMonitor<OpenAIProviderOptions> _optionsMonitor;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ConcurrentDictionary<string, OpenAIChatClientFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    public OpenAINamedChatClientFactoryCache(
        IOptionsMonitor<OpenAIProviderOptions> optionsMonitor,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Returns the factory for the given name, building and caching it when absent.</summary>
    /// <param name="name">The name of the named options instance.</param>
    /// <returns>The cached factory.</returns>
    public OpenAIChatClientFactory Get(string name)
        => _factories.GetOrAdd(name, CreateFactory);

    private OpenAIChatClientFactory CreateFactory(string name)
    {
        var options = _optionsMonitor.Get(name);

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new OpenAIChatClientFactory(options, _loggerFactory);
        }

        var client = OpenAIChatClientFactory.CreateClient(new OpenAIProviderOptions
        {
            ApiKey = PlaceholderApiKey,
            Endpoint = options.Endpoint,
            Organization = options.Organization,
            Timeout = options.Timeout,
        });

        return OpenAIChatClientFactory.FromClient(client, options.DefaultModel, _loggerFactory);
    }
}
