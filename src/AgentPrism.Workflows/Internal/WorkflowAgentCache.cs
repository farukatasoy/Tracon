using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Bir workflow'a baglanan agent sarmalayicilarini surec omru boyunca saklar.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Bu onbellek performans icin degil, DOGRULUK icindir.</strong>
/// Microsoft Agent Framework executor kimliklerini agent <em>ornegin</em>den
/// turetir: kimlik <c>{Name}_{AIAgent.Id}</c> bicimindedir ve <c>AIAgent.Id</c>
/// her ornek icin rastgele uretilir - sanal degildir, turetilmis bir sinif
/// degistiremez.
/// </para>
/// <para>
/// Sonuc: graf her calistirmada yeniden kuruldugunda agent'lar da yeniden
/// kurulsaydi executor kimlikleri her seferinde degisir ve bir kontrol
/// noktasindan sürdürme <c>InvalidDataException: The specified checkpoint is
/// not compatible with the workflow</c> ile basarisiz olurdu. Olculdu (Faz 15):
/// ayni agent ornekleriyle yeniden kurulan graf uyumlu, yeni orneklerle kurulan
/// graf uyumsuz cikti.
/// </para>
/// <para>
/// Sarmalayici (<see cref="ChildAgentInvoker"/>) gercek agent'i <em>her
/// cagrida</em> katalogdan cozer; bu yuzden onbellege alinmis bir sarmalayici
/// bayatlamaz. Onbellekte yalnizca ad, aciklama ve kimlik sabit kalir.
/// </para>
/// <para>
/// <strong>Faz 16'dan beri kimlik surec omrunu de asar.</strong> Her sarmalayici
/// <see cref="WorkflowAgentIdentity"/> ile <c>(workflow, agent)</c> ciftinden
/// turetilen kalici bir kimlik alir; boylece uygulama yeniden baslatildiginda
/// bile eski kontrol noktalari uyumlu kalir ve insan yaniti bekleyen bir
/// calistirma dagitimda kaybolmaz. Onbellek yine de tutulur: ayni ornegi
/// yeniden kullanmak hem ucuzdur hem de kimlik yazma yolunu tek bir noktada
/// toplar.
/// </para>
/// </remarks>
internal sealed class WorkflowAgentCache
{
    private readonly ConcurrentDictionary<AgentKey, ChildAgentInvoker> _agents = new();
    private readonly CallableAgentResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Yeni bir onbellek olusturur.</summary>
    /// <param name="resolver">Agent'lari katalogdan cozen cozucu.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="loggerFactory">Gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public WorkflowAgentCache(
        CallableAgentResolver resolver,
        ITenantContext tenantContext,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _resolver = resolver;
        _tenantContext = tenantContext;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Bir workflow'un kullanacagi agent sarmalayicisini dondurur; ilk cagrida
    /// olusturur ve saklar.
    /// </summary>
    /// <param name="workflowName">Sarmalayan workflow'un adi.</param>
    /// <param name="agentName">Baglanacak agent'in adi.</param>
    /// <param name="description">
    /// Katilimci aciklamasi. <strong>Ilk baglamada yakalanir</strong> ve surec
    /// boyunca sabit kalir; kimlik kararliligi bunu gerektirir. Aciklama
    /// yalnizca <c>GroupChat</c> ve <c>Magentic</c> desenlerinde modele
    /// gonderilen katilimci listesine girer, agent'in davranisini etkilemez.
    /// </param>
    /// <returns>Grafa konabilecek, kimligi kararli agent.</returns>
    public ChildAgentInvoker Get(string workflowName, string agentName, string? description)
        => _agents.GetOrAdd(
            new AgentKey(workflowName, agentName),
            static (key, state) => Create(key, state),
            (Resolver: _resolver, TenantContext: _tenantContext, LoggerFactory: _loggerFactory, Description: description));

    private static ChildAgentInvoker Create(
        AgentKey key,
        (CallableAgentResolver Resolver, ITenantContext TenantContext, ILoggerFactory LoggerFactory, string? Description) state)
    {
        var invoker = new ChildAgentInvoker(
            state.Resolver,
            state.TenantContext,
            state.LoggerFactory.CreateLogger<ChildAgentInvoker>(),
            key.WorkflowName,
            new CallableAgentInfo(key.AgentName, state.Description, Version: 0));

        // Kimlik ornegi grafa girmeden ONCE yazilir: MAF executor kimligini
        // baglama aninda okur ve sonradan degistirmek grafi ikiye bolerdi.
        WorkflowAgentIdentity.TryApply(
            invoker,
            key.WorkflowName,
            key.AgentName,
            state.LoggerFactory.CreateLogger(typeof(WorkflowAgentIdentity).FullName!));

        return invoker;
    }

    private readonly record struct AgentKey(string WorkflowName, string AgentName);
}
