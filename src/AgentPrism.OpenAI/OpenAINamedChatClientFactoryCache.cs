using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Adlandirilmis <see cref="OpenAIProviderOptions"/> ornekleri icin
/// <see cref="OpenAIChatClientFactory"/> orneklerini ada gore kurar ve onbellekler.
/// </summary>
/// <remarks>
/// <para>
/// Her ad icin <em>bir kez</em> bir <see cref="OpenAIChatClientFactory"/> (dolayisiyla
/// bir <c>OpenAIClient</c>, bir HTTP baglanti havuzu) kurulur. Ayni adin birden cok
/// kez istenmesi ayni ornegi dondurur.
/// </para>
/// <para>
/// <see cref="OpenAIProviderOptions.ApiKey"/> bos ise (yerel sunucular, F-05) OpenAI
/// istemcisi yine de bir kimlik bekler; sabit bir yer tutucu ile kurulur. Bu davranis
/// <strong>yalnizca</strong> bu onbellek uzerinden — yani <c>UseOpenAICompatible()</c>
/// ile kaydedilen saglayicilar icin — gecerlidir. <c>UseOpenAI()</c> hala anahtarsiz
/// calismaz (<see cref="OpenAIChatClientFactory.CreateClient(OpenAIProviderOptions)"/>
/// degismedi).
/// </para>
/// </remarks>
internal sealed class OpenAINamedChatClientFactoryCache
{
    /// <summary>
    /// Anahtarsiz uyumlu saglayicilar icin sabit yer tutucu. Gercek bir sir degildir;
    /// yalnizca <c>OpenAIClient</c>'in bos kimlik kabul etmemesi yuzunden gereklidir.
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

    /// <summary>Verilen ad icin bir fabrika dondurur; yoksa kurar ve onbellekler.</summary>
    /// <param name="name">Adlandirilmis ayar orneginin adi.</param>
    /// <returns>Onbelleklenmis fabrika.</returns>
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

        return new OpenAIChatClientFactory(client, options.DefaultModel, _loggerFactory);
    }
}
