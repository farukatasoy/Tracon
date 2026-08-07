using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>Icerik denetimini acan zincir uzantilari — Faz 48.</summary>
/// <remarks>
/// Uzanti metodudur, <see cref="IAgentPrismBuilder"/>'in bir uyesi degildir:
/// arayuze uye eklemek yayindan sonra kiricidir, uzanti metodu eklemek degildir.
/// </remarks>
public static class AgentPrismContentGuardBuilderExtensions
{
    /// <summary>
    /// AgentPrism'in yerlesik desen tabanli icerik guard'ini kaydeder.
    /// </summary>
    /// <param name="builder">Yapilandirma zinciri.</param>
    /// <param name="configure">Desen ve yasak sozcuk ayarlari.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// 🚨 <strong>Bu cagri K1'in kapisidir.</strong> <c>AddAgentPrism()</c> tek
    /// basina hicbir guard kaydetmez ve denetim sarmalayicisi model boru hattina
    /// <em>hic eklenmez</em>. Cagri yapilmadan hicbir istem suzulmez, hicbir yanit
    /// denetlenmez ve hicbir maliyet odenmez.
    /// </para>
    /// <para>
    /// Yerlesik guard varsayilan olarak <strong>hicbir kural tasimaz</strong>:
    /// <see cref="PatternContentGuardOptions.MaskedPii"/>
    /// <see cref="PiiPatterns.None"/>'dir ve
    /// <see cref="PatternContentGuardOptions.DeniedTerms"/> bostur. Hangi desen
    /// ailesinin acilacagi acik bir tercihtir; hepsini birlikte acmak yanlis
    /// pozitif riskini toplar.
    /// </para>
    /// <para>
    /// Ayni ayarlar <c>AgentPrism:ContentGuard:Pattern</c> yapilandirma
    /// bolumunden de okunur; bolum varsa guard <c>AddAgentPrism()</c> tarafindan
    /// zaten kaydedilir ve bu cagri gereksizdir (kayit <c>TryAddEnumerable</c>
    /// oldugu icin iki kez eklenmez).
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddPatternContentGuard(options =>
    ///        {
    ///            options.MaskedPii = PiiPatterns.CreditCard | PiiPatterns.Email;
    ///            options.DeniedTerms.Add("gizli-proje");
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder AddPatternContentGuard(
        this IAgentPrismBuilder builder,
        Action<PatternContentGuardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentGuard, PatternContentGuard>());

        if (configure is not null)
        {
            // Yapilandirma baglamasindan SONRA calisir: kodda yazilan deger
            // AgentPrism:ContentGuard:Pattern bolumunu gecersiz kilar. Diger
            // ayarlarla ayni sira (K4 — cagiranin kaydi kazanir).
            builder.Services.Configure(configure);
        }

        return builder;
    }

    /// <summary>
    /// Kendi <see cref="IContentGuard"/> uygulamanizi kaydeder.
    /// </summary>
    /// <typeparam name="TGuard">Guard tipi.</typeparam>
    /// <param name="builder">Yapilandirma zinciri.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Birden cok guard kaydedilebilir; hepsi sirayla calisir ve
    /// <strong>en sert karar kazanir</strong>.
    /// </remarks>
    public static IAgentPrismBuilder AddContentGuard<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TGuard>(
        this IAgentPrismBuilder builder)
        where TGuard : class, IContentGuard
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentGuard, TGuard>());

        return builder;
    }
}
