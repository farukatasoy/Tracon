using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentPrism;

/// <summary>AgentPrism'i standart .NET saglik denetim sistemine baglayan uzantilar (Faz 33, F-38).</summary>
public static class AgentPrismHealthCheckExtensions
{
    /// <summary>
    /// AgentPrism saglik denetimini kaydeder. Tuketici kendi <c>/health</c> yolunu kurar.
    /// </summary>
    /// <param name="builder">Saglik denetimi olusturucusu.</param>
    /// <param name="name">Denetimin adi. Varsayilan <c>agentprism</c>.</param>
    /// <param name="tags">Denetime eklenecek etiketler. Ornek: hazir olma/canlilik ayrimi.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> bos ise.</exception>
    /// <remarks>
    /// <para>
    /// AgentPrism <c>MapHealthChecks</c> cagirmaz — tuketicinin yol seciminin gaspi
    /// K1'i zorlar. Kurulum:
    /// </para>
    /// <example>
    /// <code>
    /// builder.Services.AddHealthChecks().AddAgentPrismHealthChecks();
    /// app.MapHealthChecks("/health");
    /// </code>
    /// </example>
    /// <para>
    /// Denetim <see cref="AgentPrismDiagnosticsCollector"/>'i okur; hicbir model
    /// cagrisi uretmez. Uc durum icin bkz. <see cref="AgentPrismHealthCheck"/>.
    /// </para>
    /// <para>
    /// Bu cagri yalniz bir servis kaydi ekler; <c>AddAgentPrism()</c>'in ondan once
    /// veya sonra cagrilmasi onemli degildir (DI kaydi sira bagimsizdir). Ancak
    /// <c>AddAgentPrism()</c> hic cagrilmamissa <c>/health</c> ilk yoklandiginda
    /// <see cref="AgentPrismDiagnosticsCollector"/> cozulemez ve DI acik bir hata verir.
    /// </para>
    /// </remarks>
    public static IHealthChecksBuilder AddAgentPrismHealthChecks(
        this IHealthChecksBuilder builder,
        string name = "agentprism",
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.AddCheck<AgentPrismHealthCheck>(name, tags: tags);
    }
}
