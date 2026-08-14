using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Workflow yurutmesini kaydeden uzantilar.</summary>
public static class AgentPrismWorkflowsBuilderExtensions
{
    /// <summary>
    /// Workflow yurutme motorunu kaydeder. Katalogdaki agent'lar hazir
    /// desenlerle birbirine baglanabilir ve her yurutme bir <c>runs</c> satiri
    /// uretir.
    /// </summary>
    /// <param name="builder">AgentPrism yapilandirma zinciri.</param>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// <strong>Guvenlik siniri.</strong> Arayuzden tanimlanan bir workflow yeni
    /// davranis uretmez: yalnizca katalogdaki agent'lari sirasi belli hazir
    /// desenlerle diziler. Serbest graf - ozel <c>Executor</c> tipleri, kosullu
    /// kenarlar, alt workflow'lar - yalnizca kodda,
    /// <see cref="AddWorkflow"/> ile tanimlanir. Tasarim kurali K2 boylece
    /// korunur.
    /// </para>
    /// <para>
    /// Tanim ve kontrol noktasi depolari <c>AddAgentPrism()</c> tarafindan zaten
    /// kaydedilmistir; bu cagri yalnizca <em>yurutmeyi</em> acar. Motor kayitli
    /// degilken HTTP katmani tanimlari listeleyip yonetebilir, yalnizca
    /// calistirma ucu <c>501</c> doner.
    /// </para>
    /// <para>
    /// <see cref="AgentPrismWorkflowOptions.SectionName"/> (<c>AgentPrism:Workflows</c>)
    /// <c>IConfiguration</c>'dan BAGLANIR (K-402) — diger tum <c>Use*()</c>
    /// uzantilariyla (<c>UseOpenAI</c>, <c>UsePostgreSql</c>, <c>UseSkillScripts</c>
    /// vb.) ayni sozlesme. <paramref name="configure"/> bu baglamadan SONRA
    /// calisir, boylece kod hala config'in uzerine yazabilir.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseOpenAI(apiKey)
    ///        .UsePostgreSql(connectionString)
    ///        .UseWorkflows()
    ///        .UseUI();
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseWorkflows(
        this IAgentPrismBuilder builder,
        Action<AgentPrismWorkflowOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services
            .AddOptions<AgentPrismWorkflowOptions>()
            .BindConfiguration(AgentPrismWorkflowOptions.SectionName);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Onbellek SINGLETON olmalidir: executor kimliklerinin kararliligi -
        // dolayisiyla kontrol noktalarindan sürdürme - buna baglidir.
        services.TryAddSingleton<WorkflowAgentCache>();
        services.TryAddSingleton<WorkflowDefinitionCompiler>();
        services.TryAddSingleton<WorkflowCatalog>();
        services.TryAddSingleton<IWorkflowRunner, WorkflowRunner>();

        return builder;
    }

    /// <summary>
    /// Kodda fabrika tabanli bir workflow tanimlar. Grafin nasil kuruldugu
    /// tamamen cagirana aittir.
    /// </summary>
    /// <param name="builder">AgentPrism yapilandirma zinciri.</param>
    /// <param name="name">Workflow adi.</param>
    /// <param name="factory">Grafi kuran fabrika.</param>
    /// <param name="description">Kisa aciklama.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> bos ise.</exception>
    /// <remarks>
    /// Kodda tanimli bir workflow, ayni ada sahip veritabani tanimin
    /// <strong>onune gecer</strong>. Ayni kural agent katalogunda da gecerlidir
    /// (K-019): veritabanina yazma yetkisi olan biri, kodda kayitli bir
    /// davranisi ele geciremez.
    /// </remarks>
    public static IAgentPrismBuilder AddWorkflow(
        this IAgentPrismBuilder builder,
        string name,
        Func<IServiceProvider, Workflow> factory,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddSingleton(new CodeWorkflowRegistration(name, description, factory));

        return builder;
    }
}
