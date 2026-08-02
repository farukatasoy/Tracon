using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// Bir agent'in cagirabilecegi diger agent'lari katalogdan cozer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Katalog bilerek gec cozulur.</strong> Bagimlilik grafigi dairesel
/// olurdu: <c>IAgentCatalog</c> → <c>IAgentSource</c> → <c>AgentDefinitionCompiler</c>
/// → cagrilabilir agent'lar → <c>IAgentCatalog</c>. Kabin kurucu enjeksiyonu bu
/// zinciri kuramaz. Cozum, katalogu <em>kurulum aninda degil ilk kullanimda</em>
/// istemektir; o an her iki taraf da kurulmus olur.
/// </para>
/// <para>
/// Gec cozumun ikinci ve daha degerli faydasi: alt agent her cagrida yeniden
/// cozulur. Bir alt agent'in tanimi guncellendiginde cagiran agent'in derlenmis
/// kopyasi <em>bayatlamaz</em>; onbellek anahtarina alt agent surumunu tasimak
/// gerekmez.
/// </para>
/// </remarks>
public sealed class CallableAgentResolver
{
    private readonly IServiceProvider _services;
    private IAgentCatalog? _catalog;

    /// <summary>Yeni bir cozucu olusturur.</summary>
    /// <param name="services">Katalogun cozulecegi servis saglayici.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> <see langword="null"/> ise.</exception>
    public CallableAgentResolver(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
    }

    /// <summary>Adi verilen agent'i cozer.</summary>
    /// <param name="agentName">Agent adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Calistirma kaydi sarmalayicisiyla sarilmis agent; yoksa <see langword="null"/>.</returns>
    public ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
        => ResolveCatalog().ResolveAsync(agentName, cancellationToken);

    /// <summary>Katalogdaki agent ozetlerini listeler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Agent ozetleri.</returns>
    public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => ResolveCatalog().ListAsync(cancellationToken);

    /// <summary>
    /// Cagrilabilir agent adlarini, aciklamalariyla birlikte cozer.
    /// </summary>
    /// <param name="callableAgentNames">Cozulecek adlar.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ad ve aciklama ciftleri; katalogda olmayan adlar icin aciklama bostur.</returns>
    /// <remarks>
    /// Aciklama modele gonderilen agent listesinde yer alir. Aciklamasiz bir alt
    /// agent, modelin ne zaman cagirmasi gerektigini bilemedigi bir agent'tir.
    /// </remarks>
    public async ValueTask<IReadOnlyList<CallableAgentInfo>> DescribeAsync(
        IReadOnlyList<string> callableAgentNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callableAgentNames);

        if (callableAgentNames.Count == 0)
        {
            return [];
        }

        var descriptors = await ListAsync(cancellationToken).ConfigureAwait(false);
        var byName = new Dictionary<string, AgentDescriptor>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            byName[descriptor.Name] = descriptor;
        }

        var result = new List<CallableAgentInfo>(callableAgentNames.Count);

        foreach (var name in callableAgentNames)
        {
            byName.TryGetValue(name, out var descriptor);
            result.Add(new CallableAgentInfo(name, descriptor?.Description, descriptor?.Version ?? 0));
        }

        return result;
    }

    /// <summary>Katalogu ilk kullanimda cozer ve saklar.</summary>
    /// <remarks>
    /// Yarista iki cagrinin ayni katalogu iki kez cozmesi zararsizdir: katalog
    /// singleton'dir ve kap ayni ornegi dondurur. Kilit koymak, her cagriyi
    /// gereksiz yere senkronize ederdi.
    /// </remarks>
    private IAgentCatalog ResolveCatalog()
        => _catalog ??= _services.GetRequiredService<IAgentCatalog>();
}

/// <summary>Cagrilabilir bir alt agent'in modele bildirilecek ozeti.</summary>
/// <param name="Name">Agent adi.</param>
/// <param name="Description">Ne yaptigini anlatan aciklama.</param>
/// <param name="Version">
/// Alt agent tanimin surumu. Katalogda bulunamadiysa 0. Cagiran agent'in derlenmis
/// kopyasinin onbellek anahtarina girer: alt agent'in aciklamasi modele gonderilen
/// metne gomulur, dolayisiyla alt agent guncellendiginde cagiran yeniden derlenmelidir.
/// </param>
public readonly record struct CallableAgentInfo(string Name, string? Description, int Version);
