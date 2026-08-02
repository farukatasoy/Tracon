using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>
/// Cok kiracililigi HTTP istegine baglayan uzantilar.
/// </summary>
public static class AgentPrismTenancyBuilderExtensions
{
    /// <summary>
    /// Kiraciyi gecerli HTTP isteginden cozer. Cagrilmazsa AgentPrism tek
    /// kiracili calisir ve hicbir ek yapilandirma gerekmez.
    /// </summary>
    /// <param name="builder">AgentPrism yapilandirma zinciri.</param>
    /// <param name="configure">Cozumleme ayarlari.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Kiraci kaynagi olarak <strong>claim tercih edilir</strong>; baslik yolu
    /// acikca acilmalidir ve sahtelenebilir oldugu icin yalnizca guvenilen bir
    /// ag icinde kullanilmalidir. Ayrinti:
    /// <see cref="AgentPrismTenancyOptions"/>.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseOpenAI(apiKey)
    ///        .UsePostgreSql(connectionString)
    ///        .UseTenancy(options =>
    ///        {
    ///            options.Enabled = true;
    ///            options.ClaimType = "tenant_id";
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseTenancy(
        this IAgentPrismBuilder builder,
        Action<AgentPrismTenancyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<AgentPrismTenancyOptions>();
        services.Configure(configure);
        services.AddHttpContextAccessor();

        // TryAdd DEGIL Replace: AddAgentPrism() zincirde once calisir ve
        // SingleTenantContext'i zaten kaydetmis olur.
        services.Replace(ServiceDescriptor.Singleton<ITenantContext, HttpTenantContext>());

        return builder;
    }
}
