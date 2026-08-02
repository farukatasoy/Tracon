using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>
/// Gomulu yonetim arayuzunu kaydeden uzantilar.
/// </summary>
public static class AgentPrismUiBuilderExtensions
{
    /// <summary>
    /// Gomulu yonetim arayuzunu kaydeder. <c>MapAgentPrism()</c> kaydi bulur ve
    /// arayuz rotalarini ayni onek altina baglar.
    /// </summary>
    /// <param name="builder">AgentPrism yapilandirma zinciri.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Ayri bir esleme cagrisi gerekmez. Onek tek yerde - <c>MapAgentPrism()</c>
    /// icinde - yazilir; iki yerde yazilan bir onek senkron kalmadiginda arayuz
    /// hicbir hata vermeden bos bir sayfa gosterirdi.
    /// </para>
    /// <para>
    /// Kayit <c>TryAdd</c> ile yapilir. Kendi <see cref="IAgentPrismUiProvider"/>
    /// uygulamasini daha once kaydeden bir tuketici kazanir (kural K4).
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseOpenAI(apiKey)
    ///        .UseUI();
    ///
    /// app.MapAgentPrism("/agentprism");
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseUI(this IAgentPrismBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<IAgentPrismUiProvider, EmbeddedUiProvider>();

        return builder;
    }
}
