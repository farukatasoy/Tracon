using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Kayitli bir calistirmayi ayni girdiyle, degistirilmis kosullarla yeniden
/// calistirilabilir hale getirir.
/// </summary>
/// <remarks>
/// <para>
/// Servis calistirmayi <strong>baslatmaz</strong>; yalnizca hazirlar. Boylece
/// cagiran taraf ayni plani hem akisli hem akissiz hem de kuyruga alinmis
/// (Faz 46) bir yolda kullanabilir ve butun hata durumlari yanit basmadan
/// once bilinir.
/// </para>
/// <para>
/// 🚨 <strong>Yeniden oynatma oturumsuzdur.</strong> Kaynak calistirma bir
/// oturuma bagliysa girdisi yalnizca <em>o turun</em> mesajlaridir; gecmis
/// <c>ChatHistoryProvider</c> tarafindan enjekte edilir ve kayitli girdinin
/// parcasi degildir. Oynatmayi ayni oturumda calistirmak kaynagin konusmasina
/// yazardi (append-only, K-014); bu yuzden oynatma her zaman yeni ve oturumsuz
/// bir calistirmadir. Cok turlu bir konusmayi bastan almak icin dallandirma
/// (<see cref="IConversationBranchStore"/>) kullanilir.
/// </para>
/// </remarks>
public sealed class RunReplayService
{
    private readonly IRunStore _runs;
    private readonly IRunInputStore _inputs;
    private readonly IAgentDefinitionStore _definitions;
    private readonly IAgentCatalog _catalog;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly IToolRegistry _tools;
    private readonly IAgentDecorator[] _decorators;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir yeniden oynatma servisi olusturur.</summary>
    /// <param name="runs">Calistirma deposu.</param>
    /// <param name="inputs">Girdi deposu.</param>
    /// <param name="definitions">Agent tanim deposu.</param>
    /// <param name="catalog">Agent katalogu.</param>
    /// <param name="compiler">Tanim derleyicisi.</param>
    /// <param name="tools">Tool defteri. Onay gerektiren tool denetimi buradan okunur.</param>
    /// <param name="decorators">Cozulen agent'a uygulanacak sarmalayicilar.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public RunReplayService(
        IRunStore runs,
        IRunInputStore inputs,
        IAgentDefinitionStore definitions,
        IAgentCatalog catalog,
        AgentDefinitionCompiler compiler,
        IToolRegistry tools,
        IEnumerable<IAgentDecorator> decorators,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(decorators);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _runs = runs;
        _inputs = inputs;
        _definitions = definitions;
        _catalog = catalog;
        _compiler = compiler;
        _tools = tools;

        // Sira CompositeAgentCatalog ile AYNIDIR: Order'i buyuk olan once
        // uygulanir, boylece kayit sarmalayicisi (Order = 0) en distaki olur.
        _decorators = [.. decorators.OrderByDescending(static decorator => decorator.Order)];
        _tenantContext = tenantContext;
    }

    /// <summary>Bir yeniden oynatmayi hazirlar.</summary>
    /// <param name="runId">Kaynak calistirmanin kimligi.</param>
    /// <param name="request">Degistirilecek kosullar.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Hazirlik sonucu.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> <see langword="null"/> ise.</exception>
    public async ValueTask<RunReplayPreparation> PrepareAsync(
        Guid runId,
        RunReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = await _runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Yok" ile "baska kiraciya ait" AYNI sonucu verir; ayri bir cevap
        // varligi sizdirirdi.
        if (source is null || !string.Equals(source.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.RunNotFound,
                $"There is no run with id '{runId}'.");
        }

        var input = await _inputs
            .GetAsync(_tenantContext.TenantId, runId, cancellationToken)
            .ConfigureAwait(false);

        if (input is null || input.Messages.Count == 0)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.InputNotFound,
                $"Run '{runId}' has no recorded input. It may have started while input recording " +
                "was disabled (AgentPrism:RunRecording:RecordRunInput = false), or been deleted by " +
                "a retention policy; this run cannot be replayed.");
        }

        var definition = await ResolveDefinitionAsync(source.AgentName, request.AgentVersion, cancellationToken)
            .ConfigureAwait(false);

        if (definition is null)
        {
            return await PrepareFromCatalogAsync(source, request, input, cancellationToken).ConfigureAwait(false);
        }

        if (request.ToolMode == ReplayToolMode.LiveTools && FindApprovalTool(definition.ToolNames) is { } approvalTool)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.ApprovalRequired,
                $"Agent '{definition.Name}' carries the tool '{approvalTool}', which requires approval, " +
                "and cannot run in 'LiveTools' mode. A replay may start in the background; the approval " +
                "request would find no client to answer it at that moment (the same limit applies to " +
                "sub-agents). Use 'ReplayTools' or 'NoTools'.");
        }

        var effective = ApplyOverrides(definition, request);
        var playback = request.ToolMode == ReplayToolMode.ReplayTools
            ? new RecordedToolPlayback(
                await _runs.ListToolInvocationsAsync(runId, cancellationToken).ConfigureAwait(false))
            : null;

        var callable = await _compiler
            .ResolveCallableAgentsAsync(effective, cancellationToken)
            .ConfigureAwait(false);

        // 🚨 Onbellek (CompiledAgentCache) BILEREK atlanir: bindirilmis model ve
        // oynatilan tool'lar cagriya ozgudur; onbellege girmeleri sonraki normal
        // calistirmalari da bozardi.
        var agent = _compiler.Compile(effective, callable, playback is null ? null : playback.Wrap);

        // 🚨 Kalkan dekoratorlerin ICINDE durur; gerekce ReplayMismatchGuard'in
        // notundadir (istisna kayit sarmalayicisinin catch blokina dusmelidir).
        if (playback is not null)
        {
            agent = new ReplayMismatchGuard(agent, playback);
        }

        return RunReplayPreparation.Ready(
            Decorate(agent, ToDescriptor(effective)),
            input.Messages,
            source,
            effective.Version,
            effective.Model.Model);
    }

    /// <summary>
    /// Tanim deposunda karsiligi olmayan (kod kaynakli) bir agent icin plan kurar.
    /// </summary>
    /// <remarks>
    /// 🚨 Kod agent'inin <see cref="AgentDefinition"/> karsiligi yoktur; model
    /// bindirmesi ve tool degistirme icin yeniden derlenecek bir tanim da yoktur.
    /// Sessizce <see cref="ReplayToolMode.LiveTools"/>'a dusmek K1'e aykiridir —
    /// kullanici yan etki uretmedigini sanirdi. Bu yuzden istek acikca reddedilir.
    /// </remarks>
    private async ValueTask<RunReplayPreparation> PrepareFromCatalogAsync(
        RunRecord source,
        RunReplayRequest request,
        RunInputRecord input,
        CancellationToken cancellationToken)
    {
        if (request.AgentVersion is not null)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.NotSupported,
                $"Agent '{source.AgentName}' has no version {request.AgentVersion}. " +
                "Code-defined agents have no version history.");
        }

        if (request.ModelId is not null || request.ToolMode != ReplayToolMode.LiveTools)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.NotSupported,
                $"Agent '{source.AgentName}' has no persistent definition (code-defined or deleted). " +
                "Model overrides and 'NoTools'/'ReplayTools' modes require recompiling the definition. " +
                "This agent can only be replayed with 'toolMode: LiveTools' and no override — tools " +
                "ACTUALLY run and produce side effects.");
        }

        var agent = await _catalog.ResolveAsync(source.AgentName, cancellationToken).ConfigureAwait(false);

        if (agent is null)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.NotSupported,
                $"There is no longer an agent named '{source.AgentName}'; the run cannot be replayed.");
        }

        // Code-defined agent kendi AgentDefinition'ini tasimaz (yukaridaki kontrol
        // zaten LiveTools disindaki her modu reddetmisti), ama ayni onay-tool
        // korumasi burada da gerekli: aksi halde onay gerektiren bir tool'u
        // tasiyan kod kaynakli bir agent, DB kaynaklinin aksine, hicbir 409
        // almadan sessizce "hicbir sey olmadi" ile biterdi (HATA-S4-014).
        // Tool adlari AgentDefinition'dan degil katalog aciklayicisindan (ayni
        // veri, arayuzun agent listesini de besler) okunur.
        var descriptors = await _catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var descriptor = descriptors.FirstOrDefault(
            candidate => string.Equals(candidate.Name, source.AgentName, StringComparison.Ordinal));

        if (descriptor is not null && FindApprovalTool(descriptor.ToolNames) is { } approvalTool)
        {
            return RunReplayPreparation.Failed(
                RunReplayOutcome.ApprovalRequired,
                $"Agent '{source.AgentName}' carries the tool '{approvalTool}', which requires approval, " +
                "and cannot run in 'LiveTools' mode. A replay may start in the background; the approval " +
                "request would find no client to answer it at that moment (the same limit applies to " +
                "sub-agents). This agent has no persistent definition (code-defined or deleted), so " +
                "'ReplayTools'/'NoTools' are not available either — this run cannot be replayed.");
        }

        // Katalog sarmalayicilari kendisi uygular; ikinci kez sarmalamak
        // calistirmayi iki kez kaydederdi.
        return RunReplayPreparation.Ready(agent, input.Messages, source, agentVersion: null, modelId: source.ModelId);
    }

    private async ValueTask<AgentDefinition?> ResolveDefinitionAsync(
        string agentName,
        int? version,
        CancellationToken cancellationToken)
        => version is { } requested
            ? await _definitions.GetVersionAsync(agentName, requested, cancellationToken).ConfigureAwait(false)
            : await _definitions.GetAsync(agentName, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Istegin bindirmelerini tanima uygular.
    /// </summary>
    /// <remarks>
    /// 🚨 <see cref="ReplayToolMode.NoTools"/> ve <see cref="ReplayToolMode.ReplayTools"/>
    /// modlarinda skill ve cagrilabilir alt agent yuzeyleri de <strong>kapatilir</strong>.
    /// Ikisi de tool'larini bir <c>AIContextProvider</c> uzerinden acar ve tool
    /// donusumunden gecmez; acik birakilsalardi skill script'i calisir ve alt
    /// agent gercek bir model cagrisi (ve gercek para) harcardi — "hicbir tool
    /// gercekten kosmaz" sozu bozulurdu.
    /// </remarks>
    private static AgentDefinition ApplyOverrides(AgentDefinition definition, RunReplayRequest request)
    {
        var effective = definition;

        if (request.ModelId is { Length: > 0 } modelId)
        {
            effective = effective with { Model = effective.Model with { Model = modelId } };
        }

        if (request.ToolMode != ReplayToolMode.LiveTools)
        {
            effective = effective with { SkillNames = [], CallableAgentNames = [] };
        }

        if (request.ToolMode == ReplayToolMode.NoTools)
        {
            effective = effective with { ToolNames = [] };
        }

        return effective;
    }

    private string? FindApprovalTool(IReadOnlyList<string> toolNames)
    {
        if (toolNames.Count == 0)
        {
            return null;
        }

        foreach (var descriptor in _tools.List())
        {
            if (descriptor.RequiresApproval &&
                toolNames.Contains(descriptor.Name, StringComparer.Ordinal))
            {
                return descriptor.Name;
            }
        }

        return null;
    }

    private AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
    {
        foreach (var decorator in _decorators)
        {
            agent = decorator.Decorate(agent, descriptor);
        }

        return agent;
    }

    private static AgentDescriptor ToDescriptor(AgentDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = AgentDefinitionOrigin.Database,
            SourceName = "replay",
            Version = definition.Version,
            Model = definition.Model,
            ToolNames = definition.ToolNames,
            SkillNames = definition.SkillNames,
            CallableAgentNames = definition.CallableAgentNames,
            UsesHarness = definition.Harness is not null,
            UpdatedAt = definition.UpdatedAt,
        };
}

/// <summary>Bir yeniden oynatma hazirliginin sonucu.</summary>
public enum RunReplayOutcome
{
    /// <summary>Plan hazir.</summary>
    Ready = 0,

    /// <summary>Kaynak calistirma yok veya baska bir kiraciya ait.</summary>
    RunNotFound = 1,

    /// <summary>Kaynak calistirmanin kayitli girdisi yok.</summary>
    InputNotFound = 2,

    /// <summary>Istenen kosullar bu agent icin uygulanamaz.</summary>
    NotSupported = 3,

    /// <summary>Onay gerektiren bir tool <see cref="ReplayToolMode.LiveTools"/> ile calistirilamaz.</summary>
    ApprovalRequired = 4,
}

/// <summary>Hazirlanmis bir yeniden oynatma plani.</summary>
public sealed record RunReplayPreparation
{
    private RunReplayPreparation()
    {
    }

    /// <summary>Hazirligin sonucu.</summary>
    public required RunReplayOutcome Outcome { get; init; }

    /// <summary>Basarisizligin insan okunur gerekcesi. <see cref="RunReplayOutcome.Ready"/> iken bos.</summary>
    public string? Detail { get; init; }

    /// <summary>Calistirilacak agent.</summary>
    public AIAgent? Agent { get; init; }

    /// <summary>Kaynak calistirmanin kayitli girdisi.</summary>
    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];

    /// <summary>Kaynak calistirma.</summary>
    public RunRecord? SourceRun { get; init; }

    /// <summary>Oynatmada kullanilacak tanim surumu. Kod agent'inda <see langword="null"/>.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Oynatmada kullanilacak model. Bilinmiyorsa <see langword="null"/>.</summary>
    public string? ModelId { get; init; }

    /// <summary>Basarisiz bir hazirlik uretir.</summary>
    /// <param name="outcome">Sonuc.</param>
    /// <param name="detail">Gerekce.</param>
    /// <returns>Hazirlik.</returns>
    public static RunReplayPreparation Failed(RunReplayOutcome outcome, string detail)
        => new() { Outcome = outcome, Detail = detail };

    /// <summary>Hazir bir plan uretir.</summary>
    /// <param name="agent">Calistirilacak agent.</param>
    /// <param name="messages">Kayitli girdi.</param>
    /// <param name="sourceRun">Kaynak calistirma.</param>
    /// <param name="agentVersion">Kullanilacak tanim surumu.</param>
    /// <param name="modelId">Kullanilacak model.</param>
    /// <returns>Hazirlik.</returns>
    public static RunReplayPreparation Ready(
        AIAgent agent,
        IReadOnlyList<ChatMessage> messages,
        RunRecord sourceRun,
        int? agentVersion,
        string? modelId)
        => new()
        {
            Outcome = RunReplayOutcome.Ready,
            Agent = agent,
            Messages = messages,
            SourceRun = sourceRun,
            AgentVersion = agentVersion,
            ModelId = modelId,
        };
}
