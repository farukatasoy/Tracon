using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Kayitli <see cref="IModelProvider"/> uygulamalarini ada gore tutan defter ve
/// model cagri boru hattini kuran tek nokta.
/// </summary>
/// <remarks>
/// <para>
/// Faz 1'de hicbir saglayici kayitli degildir; <c>AgentPrism.OpenAI</c> paketi
/// (Faz 3) <c>UseOpenAI()</c> ile ilk saglayiciyi ekler. Saglayici yokken
/// derleme yapilmaya calisilirsa anlasilir bir hata uretilir.
/// </para>
/// <para>
/// 🚨 <strong>Boru hattinin tamami burada kurulur</strong> (Faz 48'de tasindi).
/// Once dort saglayici paketi <c>UseFunctionInvocation()</c> +
/// <c>UseOpenTelemetry()</c> zincirini <em>kendi icinde</em> kuruyordu; defterin
/// sardigi her halka o zincirin DISINDA kaliyordu ve bu, tool cagri turlarini
/// goren bir halka yazmayi imkansiz kiliyordu. Artik <see cref="IModelProvider"/>
/// <strong>ham</strong> istemciyi doner ve boru hatti tek yerden kurulur; ucuncu
/// taraf bir saglayici da butun halkalari bedava devralir.
/// </para>
/// </remarks>
public sealed class ModelProviderRegistry : IModelProviderRegistry
{
    private readonly Dictionary<string, IModelProvider> _providers;
    private readonly ModelProviderCircuitBreaker? _circuitBreaker;
    private readonly IAttachmentStore? _attachmentStore;
    private readonly ITenantContext? _tenantContext;
    private readonly ContentGuardPipeline? _contentGuards;
    private readonly ILoggerFactory? _loggerFactory;

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
    /// <param name="contentGuards">
    /// Icerik denetimi boru hatti. <see langword="null"/> ise veya hicbir
    /// <see cref="IContentGuard"/> kayitli degilse denetim sarmalayicisi
    /// <strong>hic eklenmez</strong>.
    /// </param>
    /// <param name="loggerFactory">
    /// <c>UseFunctionInvocation()</c> ve <c>UseOpenTelemetry()</c> icin gunlukleyici
    /// fabrikasi. <see langword="null"/> ise MAF kendi varsayilanini kullanir.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">Ayni saglayici adi birden cok kez kaydedilmisse.</exception>
    public ModelProviderRegistry(
        IEnumerable<IModelProvider> providers,
        ModelProviderCircuitBreaker? circuitBreaker = null,
        IAttachmentStore? attachmentStore = null,
        ITenantContext? tenantContext = null,
        ContentGuardPipeline? contentGuards = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase);
        _circuitBreaker = circuitBreaker;
        _attachmentStore = attachmentStore;
        _tenantContext = tenantContext;
        _contentGuards = contentGuards;
        _loggerFactory = loggerFactory;

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

        // Saglayici HAM istemciyi doner; boru hattinin tamami burada kurulur.
        IChatClient chatClient = provider.CreateChatClient(binding);

        // 🚨 Icerik guard'i gercek istemcinin hemen ustunde, tool cagri dongusunun
        // ICINDE durur. Engellenen bir istek boylece aga hic cikmaz ve — bundan
        // daha onemlisi — dongunun her turu denetlenir: bir tool sonucu modele
        // ikinci cagride girer ve prompt injection'in en yaygin yolu odur.
        // Hicbir guard kayitli degilse bu satir hicbir sey yapmaz: sarmalayici
        // eklenmez ve model yolunda tek bir 'if' bile calismaz.
        if (_contentGuards is { HasGuards: true })
        {
            chatClient = new ContentGuardingChatClient(_contentGuards, binding.Model, chatClient);
        }

        // Tool cagri dongusu ve telemetri. Ikisi de Faz 48'e kadar saglayici
        // paketlerinin icindeydi; oradan tasindilar ki dongunun ICINE bir halka
        // konabilsin. Sira: dongu en distadir, telemetri onun icinde, boylece her
        // gercek model cagrisi kendi 'chat' span'ini alir.
        chatClient = chatClient
            .AsBuilder()
            .UseFunctionInvocation(_loggerFactory)
            .UseOpenTelemetry(_loggerFactory, AgentPrismDiagnostics.ActivitySourceName)
            .Build();

        // Ek cozme sarmalayicisi dongunun DISINDA durur: ekler yalnizca turun ilk
        // kullanici mesajinda bulunur ve her tool turunda yeniden cozmek deponun
        // ayni bayti tekrar tekrar okumasi olurdu.
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
