using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Kayitli <see cref="IModelProvider"/> uygulamalarini ada gore tutan defter.
/// </summary>
/// <remarks>
/// Faz 1'de hicbir saglayici kayitli degildir; <c>AgentPrism.OpenAI</c> paketi
/// (Faz 3) <c>UseOpenAI()</c> ile ilk saglayiciyi ekler. Saglayici yokken
/// derleme yapilmaya calisilirsa anlasilir bir hata uretilir.
/// </remarks>
public sealed class ModelProviderRegistry : IModelProviderRegistry
{
    private readonly Dictionary<string, IModelProvider> _providers;
    private readonly ModelProviderCircuitBreaker? _circuitBreaker;
    private readonly IAttachmentStore? _attachmentStore;
    private readonly ITenantContext? _tenantContext;

    /// <summary>Kayitli saglayicilardan yeni bir defter olusturur.</summary>
    /// <param name="providers">Model saglayicilari.</param>
    /// <param name="circuitBreaker">
    /// Uretilen istemcileri saracak devre kesici. <see langword="null"/> ise hicbir
    /// sarmalama yapilmaz (ornegin dogrudan kurulan testlerde).
    /// </param>
    /// <param name="attachmentStore">
    /// Ek referanslarini cozmek icin kullanilacak depo. <paramref name="tenantContext"/>
    /// ile birlikte verilmezse hicbir ek cozme sarmalamasi eklenmez.
    /// </param>
    /// <param name="tenantContext">Ek cozmede kullanilacak kiraci baglami.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">Ayni saglayici adi birden cok kez kaydedilmisse.</exception>
    public ModelProviderRegistry(
        IEnumerable<IModelProvider> providers,
        ModelProviderCircuitBreaker? circuitBreaker = null,
        IAttachmentStore? attachmentStore = null,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase);
        _circuitBreaker = circuitBreaker;
        _attachmentStore = attachmentStore;
        _tenantContext = tenantContext;

        foreach (var provider in providers)
        {
            if (!_providers.TryAdd(provider.Name, provider))
            {
                throw new AgentPrismException(
                    $"'{provider.Name}' adinda birden cok model saglayicisi kaydedilmis.");
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelProviderDescriptor> List()
    {
        var descriptors = new List<ModelProviderDescriptor>(_providers.Count);

        foreach (var provider in _providers.Values)
        {
            descriptors.Add(new ModelProviderDescriptor
            {
                Name = provider.Name,
                Models = provider.Models,
            });
        }

        descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return descriptors;
    }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!_providers.TryGetValue(binding.Provider, out var provider))
        {
            var known = _providers.Count == 0
                ? "hic saglayici kayitli degil"
                : string.Join(", ", _providers.Keys);

            throw new AgentPrismException(
                $"'{binding.Provider}' adinda bir model saglayicisi kayitli degil. Kayitli saglayicilar: {known}. " +
                "OpenAI icin `builder.AddAgentPrism().UseOpenAI(apiKey)` cagirin.");
        }

        IChatClient chatClient = provider.CreateChatClient(binding);

        // Ek cozme sarmalayicisi devre kesicinin ICINE, dogrudan gercek istemcinin
        // yanina konur: her gercek ag cagrisindan hemen once taze cozulmelidir,
        // devre kesicinin erken donen kisa devresinden etkilenmemelidir.
        if (_attachmentStore is not null && _tenantContext is not null)
        {
            chatClient = new AttachmentResolvingChatClient(chatClient, _attachmentStore, _tenantContext);
        }

        if (_circuitBreaker is not null)
        {
            chatClient = _circuitBreaker.Wrap(binding.Provider, chatClient);
        }

        // Icerik filtresi tespiti EN DISTA durur — devre kesicinin disinda. Bir
        // guvenlik filtresi saglayicinin saglikli oldugunu gosterir; icerde olsaydi
        // attigi istisna ardisik hata sayacini artirir ve arka arkaya filtrelenen
        // birkac istek saglayiciyi kapatirdi.
        return new ContentFilterDetectingChatClient(binding.Provider, chatClient);
    }
}
