using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Tek kiracili kurulumlar icin varsayilan kiraci baglami. Her zaman
/// <see cref="AgentPrismOptions.DefaultTenantId"/> degerini dondurur.
/// </summary>
/// <remarks>
/// Cok kiracili senaryolarda tuketici kendi <see cref="ITenantContext"/>
/// uygulamasini <c>AddAgentPrism()</c> cagrisindan <em>once</em> kaydeder;
/// AgentPrism <c>TryAdd</c> kullandigi icin tuketicinin kaydi kazanir.
/// </remarks>
public sealed class SingleTenantContext : ITenantContext
{
    private readonly IOptions<AgentPrismOptions> _options;

    /// <summary>Yeni bir tek kiraci baglami olusturur.</summary>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    public SingleTenantContext(IOptions<AgentPrismOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string TenantId => _options.Value.DefaultTenantId;
}
