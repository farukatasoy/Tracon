using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>Yerlesik model tabanli yargici acan zincir uzantilari — Faz 49.</summary>
/// <remarks>
/// Uzanti metodudur, <see cref="IAgentPrismBuilder"/>'in bir uyesi degildir:
/// arayuze uye eklemek yayindan sonra kiricidir, uzanti metodu eklemek degildir.
/// </remarks>
public static class AgentPrismOnlineEvaluationBuilderExtensions
{
    /// <summary>
    /// AgentPrism'in yerlesik model tabanli <see cref="IRunJudge"/> uygulamasini
    /// (<see cref="ModelRunJudge"/>) kaydeder.
    /// </summary>
    /// <param name="builder">Yapilandirma zinciri.</param>
    /// <param name="configure">Yargicin modeli, olcutleri ve talimati.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> veya <paramref name="configure"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// 🚨 Bu cagri TEK BASINA hicbir sey puanlamaz. Cevrimici degerlendirmenin
    /// asil kapisi <c>AgentPrism:OnlineEvaluation:Enabled</c> VE
    /// <c>AgentPrism:OnlineEvaluation:SampleRate</c>'dir (K1: ikisi de acik
    /// olmadikca hicbir calistirma orneklenmez). Bu uzanti yalnizca hangi
    /// modelin yargic olarak KULLANILACAGINI kaydeder.
    /// </para>
    /// <para>
    /// <see cref="ModelBinding"/> ic ice bir tip oldugu icin yapilandirma
    /// bolumunden degil, kod tarafinda kurulur — diger sagayici bindirmeleri
    /// (ornek: <c>AgentDefinition.Model</c>) ile ayni desen.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddModelRunJudge(options =>
    ///        {
    ///            options.Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" };
    ///            options.Criteria.Add("Yanit soruyu dogrudan cevapliyor mu?");
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder AddModelRunJudge(
        this IAgentPrismBuilder builder,
        Action<ModelRunJudgeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRunJudge, ModelRunJudge>());
        builder.Services.Configure(configure);

        return builder;
    }
}
