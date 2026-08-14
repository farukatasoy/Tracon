using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Sardigi agent'in her calistirmasini <see cref="IRunStore"/> icine olay olarak
/// yazar, metriklerini yayar ve span'lerini toplar.
/// </summary>
/// <remarks>
/// <para>
/// Bu bir Microsoft Agent Framework middleware'i <em>degil</em>, bir
/// <see cref="DelegatingAIAgent"/> sarmalayicisidir. Sebep: MAF middleware zinciri
/// agent'a ozgudur ve <c>HarnessAgent</c> kendi ic dekoratorlerini ekler.
/// Dis sarmalayici, harness dahil <strong>her</strong> agent tipinde ayni sekilde calisir.
/// </para>
/// <para>
/// Tool cagrilari, MAF'in urettigi <see cref="FunctionCallContent"/> ve
/// <see cref="FunctionResultContent"/> iceriklerinden okunur; ayri bir kanca gerekmez.
/// </para>
/// <para>
/// <strong>Kok span burada baslar.</strong> Sarmalayici en distaki dekoratordur
/// (<c>Order = 0</c>), bu yuzden acilan <c>agentprism.run</c> span'i ic
/// sarmalayicilarin ve model cagrilarinin span'lerini cocuk olarak toplar.
/// Calistirma kimligi ile trace kimligi ancak boyle birbirine baglanabilir.
/// </para>
/// </remarks>
public sealed class RunRecordingAgent : DelegatingAIAgent
{
    private static readonly ActivitySource ActivitySource = new(AgentPrismDiagnostics.ActivitySourceName);

    private readonly IRunStore _runStore;
    private readonly ITenantContext _tenantContext;
    private readonly AgentPrismRunRecordingOptions _options;
    private readonly ILogger<RunRecordingAgent> _logger;
    private readonly AgentPrismMetrics? _metrics;
    private readonly RunTraceCollector? _traceCollector;
    private readonly TimeProvider _timeProvider;
    private readonly string? _modelId;
    private readonly string? _modelProvider;
    private readonly AgentPrismAgentGraphOptions _graphOptions;
    private readonly int? _agentVersion;
    private readonly bool _includeAgentVersionTag;
    private readonly IRunPricingResolver? _pricingResolver;
    private readonly QuotaEnforcer? _quotaEnforcer;
    private readonly IWebhookPublisher? _webhookPublisher;
    private readonly IRunCancellationRegistry? _cancellationRegistry;
    private readonly IRunErrorClassifier? _errorClassifier;
    private readonly IRunInputStore? _runInputStore;
    private readonly RunSampler? _runSampler;
    private readonly ContentGuardPipeline? _contentGuardPipeline;

    /// <summary>Yeni bir kayit sarmalayicisi olusturur.</summary>
    /// <param name="innerAgent">Sarmalanan agent.</param>
    /// <param name="runStore">Olaylarin yazilacagi depo.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="options">Kayit ayrinti ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="metrics">Metrik aletleri. <see langword="null"/> ise metrik yayilmaz.</param>
    /// <param name="traceCollector">Span toplayici. <see langword="null"/> ise span yazilmaz.</param>
    /// <param name="modelId">Agent'in bagli oldugu model. Bilinmiyorsa <see langword="null"/>.</param>
    /// <param name="modelProvider">
    /// Agent'in bagli oldugu model saglayicisi. Yalniz maliyet cozumlemesinde
    /// kullanilir, kalicilastirilmaz (bkz. <c>docs/KARARLAR.md</c> K-154).
    /// </param>
    /// <param name="timeProvider">Zaman kaynagi. <see langword="null"/> ise sistem saati kullanilir.</param>
    /// <param name="graphOptions">
    /// Cagri agaci sinirlari. <see langword="null"/> ise varsayilanlar kullanilir.
    /// </param>
    /// <param name="agentVersion">
    /// Agent'in katalog ozetinden gelen guncel tanim surumu. Bilinmiyorsa (ornegin
    /// kod agent'i) <see langword="null"/>. Bir A/B deneyi tarafindan cozulen bir
    /// calistirmada <see cref="AgentPrismRunOptions.AgentVersion"/> bunun uzerine yazar.
    /// </param>
    /// <param name="includeAgentVersionTag">
    /// <see cref="AgentPrismDiagnostics.Tags.AgentVersion"/> etiketi span'e ve metriklere
    /// eklensin mi. Bkz. <see cref="AgentPrismObservabilityOptions.IncludeAgentVersionTag"/>.
    /// </param>
    /// <param name="pricingResolver">
    /// Maliyet cozumleyici. <see langword="null"/> ise hicbir maliyet hesaplanmaz.
    /// </param>
    /// <param name="quotaEnforcer">
    /// Kota muhasebecisi. <see langword="null"/> ise tuketim sayilmaz. Yalnizca
    /// <strong>kok</strong> calistirmalar sayilir; alt calistirmalar ayni
    /// istegin parcasidir ve iki kez sayilmamalidir.
    /// </param>
    /// <param name="webhookPublisher">
    /// Olay yayincisi. <see langword="null"/> ise <c>run.*</c> olaylari yayilmaz.
    /// </param>
    /// <param name="cancellationRegistry">
    /// Iptal defteri. <see langword="null"/> ise calistirma disaridan
    /// (<c>POST /api/runs/{id}/cancel</c>) iptal edilemez.
    /// </param>
    /// <param name="errorClassifier">
    /// Hata siniflandirici. <see langword="null"/> ise hata sinifi ve kumeleme
    /// parmak izi hesaplanmaz (<see cref="RunError.Class"/>/<see cref="RunError.Fingerprint"/>
    /// bos kalir).
    /// </param>
    /// <param name="runInputStore">
    /// Girdi deposu (Faz 47). <see langword="null"/> ise veya
    /// <see cref="AgentPrismRunRecordingOptions.RecordRunInput"/> kapaliysa girdi
    /// yazilmaz ve calistirma yeniden oynatilamaz.
    /// </param>
    /// <param name="runSampler">
    /// Cevrimici degerlendirme orneklemeleyicisi (Faz 49). <see langword="null"/>
    /// ise hicbir calistirma orneklenmez.
    /// </param>
    /// <param name="contentGuardPipeline">
    /// Icerik denetimi boru hatti (Faz 48). <see langword="null"/> veya
    /// <see cref="ContentGuardPipeline.HasGuards"/> <see langword="false"/> ise
    /// girdi ham kaydedilir. Verilmisse <c>RunStarted</c> olayina ve
    /// <see cref="IRunInputStore"/>'a yazilan girdi, <see cref="ContentGuardingChatClient"/>'in
    /// modele gonderdigi ile AYNI denetimden gecer — ikisi ayrilirsa maskelenen/engellenen
    /// icerik kalici depoda ham kalir (HATA-S3-006).
    /// </param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public RunRecordingAgent(
        AIAgent innerAgent,
        IRunStore runStore,
        ITenantContext tenantContext,
        AgentPrismRunRecordingOptions options,
        ILogger<RunRecordingAgent> logger,
        AgentPrismMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        string? modelId = null,
        string? modelProvider = null,
        TimeProvider? timeProvider = null,
        AgentPrismAgentGraphOptions? graphOptions = null,
        int? agentVersion = null,
        bool includeAgentVersionTag = true,
        IRunPricingResolver? pricingResolver = null,
        QuotaEnforcer? quotaEnforcer = null,
        IWebhookPublisher? webhookPublisher = null,
        IRunCancellationRegistry? cancellationRegistry = null,
        IRunErrorClassifier? errorClassifier = null,
        IRunInputStore? runInputStore = null,
        RunSampler? runSampler = null,
        ContentGuardPipeline? contentGuardPipeline = null)
        : base(innerAgent)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _runStore = runStore;
        _tenantContext = tenantContext;
        _options = options;
        _logger = logger;
        _metrics = metrics;
        _traceCollector = traceCollector;
        _modelId = modelId;
        _modelProvider = modelProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _graphOptions = graphOptions ?? new AgentPrismAgentGraphOptions();
        _agentVersion = agentVersion;
        _includeAgentVersionTag = includeAgentVersionTag;
        _pricingResolver = pricingResolver;
        _quotaEnforcer = quotaEnforcer;
        _webhookPublisher = webhookPublisher;
        _cancellationRegistry = cancellationRegistry;
        _errorClassifier = errorClassifier;
        _runInputStore = runInputStore;
        _runSampler = runSampler;
        _contentGuardPipeline = contentGuardPipeline;
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        }

        // 🚨 Kok span BU METODUN GOVDESINDE baslatilmalidir. Activity.Current bir
        // AsyncLocal'dir; bir async yardimci metodun icinde yapilan atama cagirana
        // GERI AKMAZ. Olculdu: span yardimci metotta acildiginda ic span'ler
        // (invoke_agent, chat) kok span'in cocugu degil, kardesi oluyordu.
        var start = PrepareRun(session, options, isStreaming: false);

        // Ayni AsyncLocal kurali calistirma kapsami icin de gecerlidir: skill
        // script calistiricisi ve alt agent sarmalayicisi kapsami buradan okur.
        AgentPrismRunContext.SetCurrent(start.Scope);

        // Disaridan gelen bir iptal istegi (POST /api/runs/{id}/cancel) bu
        // kaynagi tetikler. Cagiranin kendi belirteci (ornegin istemcinin HTTP
        // baglantisi) ayri kalir: ikisinden biri dusse de calistirma iptal
        // olur, ama defter yalniz KENDI kaynagini tasir.
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var cancellationRegistration = _cancellationRegistry?.Register(
            start.Scope.RunId,
            start.Scope.RootRunId,
            start.Scope.TenantId,
            cancellationSource);

        var scope = CreateScope(start);

        try
        {
            await WriteRunStartAsync(start, messages, cancellationToken).ConfigureAwait(false);

            var response = await base.RunCoreAsync(messages, session, options, cancellationSource.Token).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                await WriteContentsAsync(scope, message.Contents, cancellationToken).ConfigureAwait(false);
            }

            await scope.Writer.AppendAsync(
                new RunEventDraft(RunEventType.MessageCompleted) { Text = response.Text },
                cancellationToken).ConfigureAwait(false);

            var usage = ToRunUsage(response.Usage);

            // Alt calistirma onay isteyerek bittiyse kayit basarili gorunmemelidir:
            // model bir sonucu degil, cevaplanamayacak bir soruyu geri dondu.
            if (start.Scope.Depth > 0 && ChildRunApproval.Describe(response.Messages) is { } pending)
            {
                await CompleteAsync(scope, RunStatus.Failed, usage, ApprovalError(pending), cancellationToken)
                    .ConfigureAwait(false);

                return response;
            }

            // Kuyruktan kosan bir kok calistirma (Faz 55) onay isteyerek bittiyse
            // Completed yerine AwaitingApproval ile kapanir: canli bir istemci
            // yoktur, karar POST /api/approvals/{id}/decide ile YENI bir
            // calistirmada gelir (K-014, RunStatus.AwaitingInput ile ayni ilke).
            // Senkron/MCP/A2A yolu SuspendOnApproval'i HIC ayarlamaz; davranisi
            // degismez.
            if (start.Scope.Depth == 0 &&
                options is AgentPrismRunOptions { SuspendOnApproval: true } &&
                ChildRunApproval.Describe(response.Messages) is not null)
            {
                await CompleteAsync(scope, RunStatus.AwaitingApproval, usage, null, cancellationToken)
                    .ConfigureAwait(false);

                return response;
            }

            await CompleteAsync(scope, RunStatus.Completed, usage, null, cancellationToken)
                .ConfigureAwait(false);

            return response;
        }
        catch (OperationCanceledException)
        {
            await CompleteAsync(scope, RunStatus.Canceled, null, null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await CompleteAsync(scope, RunStatus.Failed, null, ToRunError(ex), CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            await foreach (var passthrough in base.RunCoreStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
            {
                yield return passthrough;
            }

            yield break;
        }

        // Kok span burada baslar; gerekcesi RunCoreAsync icindeki nota bakiniz.
        var start = PrepareRun(session, options, isStreaming: true);

        AgentPrismRunContext.SetCurrent(start.Scope);

        // Gerekce RunCoreAsync icindeki nota bakiniz: bu kaynak defterin
        // TryCancel'inin tetikledigi kaynaktir, cagiranin kendi belirtecinden ayridir.
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var cancellationRegistration = _cancellationRegistry?.Register(
            start.Scope.RunId,
            start.Scope.RootRunId,
            start.Scope.TenantId,
            cancellationSource);

        var scope = CreateScope(start);
        UsageDetails? usage = null;
        string? pendingApproval = null;

        // Faz 55: kuyruktan kosan bir kok calistirmada onay isteyen bir tool
        // cagrisi Completed yerine AwaitingApproval'a esler. Gerekce RunCoreAsync
        // icindeki AYNI notta.
        var suspendOnApproval = start.Scope.Depth == 0 && options is AgentPrismRunOptions { SuspendOnApproval: true };
        var topLevelPendingApproval = false;

        var enumerator = base.RunCoreStreamingAsync(messages, session, options, cancellationSource.Token)
            .GetAsyncEnumerator(cancellationSource.Token);

        // 🚨 HATA-S1-015: bu iki bayrak, DisposeAsync()'in tuketici tarafindan
        // ERKEN cagrildigi (dongu ne dogal bitmis ne de bir istisnayla cikmis)
        // durumu ayirt eder. C#'in async-iterator kurali geregi, tuketici bir
        // `yield return`'den SONRA (bir sonraki MoveNextAsync'ten ONCE)
        // DisposeAsync() cagirirsa, yalniz asagidaki `finally` blogu calisir —
        // onun ALTINDAKI kod (dogal bitisin CompleteAsync cagrisi) HICBIR ZAMAN
        // calismaz; disposal metodun geri kalanini normal akisla SURDURMEZ,
        // yalniz askidaki `finally` bloklarini calistirir. Gercek zamanli ses
        // turunda kullanici `cancel` gonderdiginde tam bu ariza olusuyordu: TTS
        // ag cagrisi surerken (`yield return`'den sonra, tuketici -
        // VoiceConversationDriver.RespondAsync - kontrolu devralmisken) gelen
        // iptal, tuketicinin `await foreach`'ini erken DisposeAsync()'e
        // zorluyor, ne `Completed` ne `Canceled` hic yazilmiyordu — run kalici
        // olarak Running'de asili kaliyordu (gercek maliyet sessizce kaybolur).
        var naturalEnd = false;
        var completedByCatch = false;

        try
        {
            // 🚨 HATA-S4-012: bu adim BILEREK try/finally'nin ICINDEDIR — gerekce
            // WriteRunStartAsync'in kendi belgesinde ve CreateScope'un notunda.
            await WriteRunStartAsync(start, messages, cancellationToken).ConfigureAwait(false);

            while (true)
            {
                AgentResponseUpdate update;

                try
                {
                    // 🚨 Kapsam HER adimda yeniden yazilir. Bir async iterator
                    // govdesinde yapilan AsyncLocal atamasi `yield return`
                    // sinirini asmaz: cagri driver'a dondugunde ExecutionContext
                    // geri alinir ve sonraki MoveNextAsync temiz bir baglamla
                    // baslar. Olculdu (Faz 12): akisli calistirmada alt agent
                    // cagrisi "calistirma kaydi kapali" diyerek reddediliyordu.
                    // Atama MoveNextAsync'ten HEMEN once yapilmalidir; yalnizca
                    // dongunun disinda yapmak yetmez.
                    AgentPrismRunContext.SetCurrent(start.Scope);

                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        naturalEnd = true;
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    completedByCatch = true;
                    await CompleteAsync(scope, RunStatus.Canceled, null, null, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    completedByCatch = true;
                    await CompleteAsync(scope, RunStatus.Failed, null, ToRunError(ex), CancellationToken.None)
                        .ConfigureAwait(false);
                    throw;
                }

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        usage = usageContent.Details;
                    }
                }

                pendingApproval ??= start.Scope.Depth > 0
                    ? ChildRunApproval.Describe(update.Contents)
                    : null;

                topLevelPendingApproval = topLevelPendingApproval ||
                    (suspendOnApproval && ChildRunApproval.Describe(update.Contents) is not null);

                await WriteContentsAsync(scope, update.Contents, cancellationToken).ConfigureAwait(false);

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);

            // Ne dogal bitis (asagidaki kod tamamlar) ne bir istisna (yukaridaki
            // catch zaten tamamladi) — tuketicinin erken DisposeAsync()'i. Bu,
            // terminal durumu yazacak SON sans: asagidaki kod bu noktadan sonra
            // ASLA calismayacak.
            if (!naturalEnd && !completedByCatch)
            {
                await CompleteAsync(scope, RunStatus.Canceled, ToRunUsage(usage), null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        var streamingStatus = pendingApproval is not null
            ? RunStatus.Failed
            : topLevelPendingApproval
                ? RunStatus.AwaitingApproval
                : RunStatus.Completed;

        await CompleteAsync(
            scope,
            streamingStatus,
            ToRunUsage(usage),
            pendingApproval is null ? null : ApprovalError(pendingApproval),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calistirmanin kimligini cozer ve kok span'i acar.
    /// </summary>
    /// <remarks>
    /// <strong>Bu metot es zamanlidir ve oyle kalmalidir.</strong>
    /// <see cref="Activity.Current"/> bir <c>AsyncLocal</c>'dir: bir async metodun
    /// icinde yapilan atama, o metot dondugunde cagirana geri akmaz. Span burada
    /// acilip cagiran metodun kendi govdesinde tutulmazsa, ic sarmalayicilarin
    /// ve model cagrilarinin span'leri kok span'in <em>cocugu</em> degil
    /// <em>kardesi</em> olur ve waterfall gorunumu yanlis bir hiyerarsi cizer.
    /// </remarks>
    private RunStart PrepareRun(AgentSession? session, AgentRunOptions? options, bool isStreaming)
    {
        // Cagiran kimligi verdiyse o kullanilir. Akisli bir uc, ilk cerceveyi
        // yazmadan once kimligi bilmek zorundadir; kendi urettigi kimligi buraya
        // gecerek istemciye dogru kimligi bildirebilir.
        var prismOptions = options as AgentPrismRunOptions;
        var runId = prismOptions?.RunId ?? AgentPrismId.NewId();
        var agentName = Name ?? InnerAgent.Id;
        var tenantId = _tenantContext.TenantId;
        var sessionId = session is null ? null : GetSessionId(session);
        var depth = Math.Max(prismOptions?.Depth ?? 0, 0);
        var agentVersion = prismOptions?.AgentVersion ?? _agentVersion;

        var activity = ActivitySource.StartActivity(AgentPrismDiagnostics.RunActivityName, ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(AgentPrismDiagnostics.Tags.RunId, runId);
            activity.SetTag(AgentPrismDiagnostics.Tags.AgentName, agentName);
            activity.SetTag(AgentPrismDiagnostics.Tags.TenantId, tenantId);
            activity.SetTag(AgentPrismDiagnostics.Tags.Streaming, isStreaming);

            if (sessionId is not null)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.SessionId, sessionId);
            }

            if (_modelId is not null)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.ModelId, _modelId);
            }

            if (agentVersion is { } version && _includeAgentVersionTag)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.AgentVersion, version);
            }

            if (prismOptions?.ParentRunId is { } parent)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.ParentRunId, parent);
                activity.SetTag(AgentPrismDiagnostics.Tags.Depth, depth);
            }

            // Kok calistirmada yeni bir trace baslar. Alt calistirma ayni trace'i
            // surdurur: Activity.Current cagri zinciriyle asagi aktigi icin span
            // dogal olarak kok span'in altina yerlesir.
            if (depth == 0)
            {
                _traceCollector?.BeginRun(activity.TraceId.ToString());
            }
        }

        // 🚨 Yazici BU METOTTA kurulur, BeginRunAsync icinde degil. Kapsam bir
        // AsyncLocal'e yazilir ve async bir metot icinden yapilan atama cagirana
        // geri akmaz; yazici orada kurulsaydi alt cagri kapsamda yazici goremez
        // ve ozet olaylari kok akisa yazamazdi.
        var writer = new RunEventWriter(_runStore, _options, _logger, runId);

        var scope = new AgentRunScope
        {
            RunId = runId,
            RootRunId = prismOptions?.RootRunId ?? runId,
            Depth = depth,
            AgentName = agentName,
            TenantId = tenantId,

            // Kapsamdaki oturum kimligi calistirma kaydindakinden GENIS tanimlidir:
            // alt calistirmaya MAF bir oturum gecirmez, ama orada uretilen icerik
            // yine kok oturuma aittir. `RunStartInfo.SessionId` (yani runs.session_id)
            // bu geri dusustu KULLANMAZ ve anlamini korur.
            SessionId = sessionId ?? prismOptions?.SessionId,
            Budget = prismOptions?.Budget ?? (depth == 0 ? _graphOptions.CreateBudget() : null),
            Writer = writer,
            ExtraUsage = new CompactionUsageAccumulator(),
            ToolUsage = new ToolUsageAccumulator(),
            AgentVersion = agentVersion,
            ExperimentId = prismOptions?.ExperimentId,
            Variant = prismOptions?.Variant,
        };

        return new RunStart(
            scope,
            writer,
            sessionId,
            isStreaming,
            activity,
            prismOptions?.ParentRunId,
            prismOptions?.Kind ?? RunKind.Agent,
            agentVersion,
            prismOptions?.ExperimentId,
            prismOptions?.Variant,

            // Soy bagi yalniz KOK calistirmada anlamlidir: bir yeniden oynatmanin
            // alt cagrilari kaynak agacin alt cagrilarina karsilik gelmez.
            depth == 0 ? prismOptions?.ReplayOfRunId : null);
    }

    /// <summary>
    /// Calistirma kapsamini kurar. Yalniz bellek icinde nesne olusturur, hicbir
    /// G/C yapmaz — bu yuzden ne istisna atar ne iptal edilir.
    /// </summary>
    /// <remarks>
    /// 🚨 HATA-S4-012: kapsam kurma <see cref="WriteRunStartAsync"/>'ten
    /// (G/C yapan, iptal edilebilen adim) BILEREK ayrildi. Eski tek-parca
    /// <c>BeginRunAsync</c>'te kapsam ancak <see cref="SaveInputAsync"/>
    /// BASARIYLA donunce kuruluyordu; <c>SaveInputAsync</c> ise
    /// <see cref="OperationCanceledException"/>'i BILEREK yutmaz (K-034 —
    /// gercek bir iptali sessizce bogmamak icin). Sonuc: RunStarted olayi
    /// depoya ZATEN yazilmisken (ayri, ONCEKI bir yazma) istemci baglantiyi
    /// bu dar pencerede keserse istisna cagiranin try/finally guvenlik agina
    /// hic GIRMEDEN metodun disina firliyordu — calistirma sonsuza dek
    /// Running'de asili kaliyordu (bkz. RunReconciliationOptions varsayilani
    /// da kapali, kendiliginden iyilesme yok). Kapsam artik G/C'den ONCE,
    /// cagiranin kendi try/finally'sinin ICINDE kurulur; boylece
    /// <see cref="WriteRunStartAsync"/> iptal edilse bile guvenlik agi tam
    /// bir <see cref="RunScope"/> ile <see cref="CompleteAsync"/> cagirabilir.
    /// </remarks>
    private RunScope CreateScope(RunStart start)
        => new(
            start.Writer,
            start.Activity,
            new ToolInvocationTracker(
                start.Scope.RunId,
                start.IsStreaming,
                _timeProvider,
                start.Scope.ToolUsage,
                start.Scope.TenantId),
            start.Scope.AgentName!,
            start.Scope.TenantId!,
            _timeProvider.GetTimestamp(),
            start.Scope.Budget,
            start.Scope.ExtraUsage,
            OwnsTrace: start.Scope.Depth == 0,
            AgentVersion: start.AgentVersion,
            RunId: start.Scope.RunId,
            RootRunId: start.Scope.RootRunId,
            Depth: start.Scope.Depth,
            SessionId: start.SessionId,
            Kind: start.Kind);

    /// <summary>
    /// Calistirma kaydini acar: <c>runs</c> satirini ve ilk <c>RunStarted</c>
    /// olayini yazar, girdiyi <see cref="IRunInputStore"/>'a kaydeder.
    /// </summary>
    /// <remarks>
    /// Cagiran bu metodu KENDI try/finally guvenlik aginin ICINDE cagirmalidir
    /// (bkz. <see cref="CreateScope"/>'un notu) — aksi halde bu adimda olusan
    /// bir <see cref="OperationCanceledException"/> calistirmayi terminal bir
    /// duruma hic tasimadan kaybolur.
    /// </remarks>
    private async ValueTask WriteRunStartAsync(
        RunStart start,
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        // Girdi listesi BIR kez maddelestirilir: hem sorgu metni hem girdi kaydi
        // ayni koleksiyonu okur.
        var input = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        // 🚨 Kayda giden girdi, guard boru hattindan ayrica gecirilir: aksi halde
        // maskelenen/engellenen icerik RunStarted olayinda ve IRunInputStore'da
        // HAM kalir — modele giden ContentGuardingChatClient icinde zaten
        // maskelenir, ama bu kayit HIC o istemciye ugramaz (HATA-S3-006).
        // ContentGuardPipeline.PreviewAsync kullanilir: InspectAsync degil, cunku
        // bu noktada calistirma satiri (runs) henuz yok — bkz. o metodun belgesi.
        // Cagirana geciren `messages` degiskeni BILEREK degistirilmez, modele
        // giden metin bu maskelemeden etkilenmemelidir.
        if (_contentGuardPipeline is { HasGuards: true } guardPipeline && guardPipeline.Options.InspectInput)
        {
            input = await ContentGuardMessageMasker
                .PreviewAsync(guardPipeline, ContentGuardDirection.Input, input, _modelId, cancellationToken)
                .ConfigureAwait(false);
        }

        await start.Writer.StartAsync(
            new RunStartInfo
            {
                RunId = start.Scope.RunId,
                AgentName = start.Scope.AgentName!,
                Kind = start.Kind,
                StartedAt = _timeProvider.GetUtcNow(),
                TenantId = start.Scope.TenantId,
                SessionId = start.SessionId,
                ModelId = _modelId,
                IsStreaming = start.IsStreaming,
                ParentRunId = start.ParentRunId,

                // Kok calistirmada alan bos kalir: kokun kendisine isaret eden bir
                // deger yazmak, "kok mu, alt mi" sorusunu sorguda ikinci bir kosula
                // dondururdu.
                RootRunId = start.Scope.Depth == 0 ? null : start.Scope.RootRunId,
                Depth = start.Scope.Depth,
                AgentVersion = start.AgentVersion,
                ExperimentId = start.ExperimentId,
                Variant = start.Variant,
                ReplayOfRunId = start.ReplayOfRunId,
            },
            ExtractQuery(input),
            cancellationToken).ConfigureAwait(false);

        await SaveInputAsync(start, input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calistirmanin girdi mesajlarini <see cref="IRunInputStore"/> icine yazar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kayit <strong>her</strong> calistirma icin yapilir — kok, alt calistirma,
    /// eval ve workflow dahil. Bir alt agent cagrisi da tek basina yeniden
    /// oynatilabilir olmalidir; kok ile sinirlamak agac derinligi kadar satiri
    /// kaybettirirdi.
    /// </para>
    /// <para>
    /// 🚨 Hata <strong>yutulur</strong>: gozlemlenebilirlik islevselligi bozmaz
    /// (<see cref="IRunStore"/> ile ayni sozlesme). Girdi yazilamamis bir
    /// calistirma calisir, yalnizca yeniden oynatilamaz.
    /// </para>
    /// </remarks>
    private async ValueTask SaveInputAsync(
        RunStart start,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (_runInputStore is null || !_options.RecordRunInput || messages.Count == 0)
        {
            return;
        }

        try
        {
            await _runInputStore.SaveAsync(
                new RunInputRecord
                {
                    RunId = start.Scope.RunId,
                    TenantId = start.Scope.TenantId!,
                    Messages = messages,
                    CreatedAt = _timeProvider.GetUtcNow(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "AgentPrism calistirma girdisi kaydedilemedi. Calistirma {RunId} normal sekilde devam ediyor " +
                "ancak yeniden oynatilamayacak.",
                start.Scope.RunId);
        }
    }

    /// <summary>Tamamlanmis bir kok calistirmanin tuketimini kota sayaclarina yazar.</summary>
    /// <remarks>
    /// Fiyat tanimsizsa <see cref="QuotaConsumption.Cost"/> <see langword="null"/>
    /// kalir — sifir <strong>degil</strong> (Faz 20 kurali). Boyle bir
    /// calistirma para cinsi kotaya katilmaz, ama token kotasina katilir: kota
    /// para cinsinden uygulanamadiginda token'a duser.
    /// </remarks>
    private async ValueTask RecordQuotaAsync(
        RunScope scope,
        RunUsage? usage,
        RunCost? cost,
        CancellationToken cancellationToken)
    {
        if (_quotaEnforcer is null)
        {
            return;
        }

        await _quotaEnforcer.RecordAsync(
            new QuotaConsumption
            {
                TenantId = scope.TenantId,
                AgentName = scope.AgentName,
                Runs = 1,
                Tokens = usage?.TotalTokens ?? 0,
                Cost = cost is { Source: not PricingSource.Unknown }
                    ? (cost.InputCost ?? 0m) + (cost.OutputCost ?? 0m)
                    : null,
                OccurredAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Kok calistirma bittiginde cevrimici degerlendirme icin ornekleme kararini
    /// verir (Faz 49).
    /// </summary>
    /// <remarks>
    /// 🚨 <see cref="RunSampler.SampleAsync"/> zaten kendi hatasini yutar
    /// (bkz. sinif belgesi); buradaki <c>try/catch</c> ikinci bir savunma
    /// katmanidir — ornekleme hicbir sekilde calistirmayi ETKILEMEMELIDIR
    /// (gozlemlenebilirlik islevselligi bozmaz kurali).
    /// </remarks>
    private async ValueTask SampleForOnlineEvalAsync(RunScope scope, RunStatus status, CancellationToken cancellationToken)
    {
        if (_runSampler is null)
        {
            return;
        }

        try
        {
            await _runSampler.SampleAsync(
                new RunSampleRequest
                {
                    RunId = scope.RunId,
                    TenantId = scope.TenantId,
                    AgentName = scope.AgentName,
                    Kind = scope.Kind,
                    Status = status,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "Cevrimici degerlendirme ornekleme karari basarisiz oldu: calistirma={RunId}.", scope.RunId);
            }
        }
    }

    /// <summary>Kok calistirma bittiginde <c>run.completed</c>/<c>run.failed</c> yayar.</summary>
    /// <remarks>
    /// Yuk yalnizca <strong>ozet</strong> tasir (K-161): mesaj icerigi ve model
    /// yaniti buraya girmez. Icerik isteyen alici <c>/api/runs/{id}</c> cagirir.
    /// </remarks>
    private async ValueTask PublishRunEventAsync(
        RunScope scope,
        RunStatus status,
        RunUsage? usage,
        RunCost? cost,
        RunError? error,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        if (_webhookPublisher is null)
        {
            return;
        }

        // Iptal edilen calistirma ne basari ne hatadir; abone icin gurultudur.
        var eventType = status switch
        {
            RunStatus.Completed => WebhookEvents.RunCompleted,
            RunStatus.Failed => WebhookEvents.RunFailed,
            _ => null,
        };

        if (eventType is null)
        {
            return;
        }

        await _webhookPublisher.PublishAsync(
            scope.TenantId,
            eventType,
            new WebhookEventPayload
            {
                OccurredAt = _timeProvider.GetUtcNow(),
                Run = new WebhookRunSummary
                {
                    RunId = scope.RunId.ToString(),
                    RootRunId = scope.RootRunId.ToString(),
                    SessionId = scope.SessionId,
                    AgentName = scope.AgentName,
                    ModelId = _modelId,
                    Status = status.ToString(),
                    DurationMs = (long)elapsed.TotalMilliseconds,
                    InputTokens = usage?.InputTokens,
                    OutputTokens = usage?.OutputTokens,
                    Cost = cost is { Source: not PricingSource.Unknown }
                        ? (cost.InputCost ?? 0m) + (cost.OutputCost ?? 0m)
                        : null,
                    Currency = cost?.Currency,
                    Error = error?.Message,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static RunError ApprovalError(string toolNames)
        => new()
        {
            Type = nameof(AgentPrismException),
            Message = $"Alt calistirma '{toolNames}' tool'u icin kullanici onayi istedi. " +
                      "Alt agent onay isteyemez: onay bir sonraki turun girdisidir ve cagri " +
                      "agacinin ortasinda beklenemez. Bu tool icin otomatik onay kurali " +
                      "tanimlayin veya alt agent'i onay gerektirmeyen tool'larla sinirlayin.",
        };

    private async ValueTask CompleteAsync(
        RunScope scope,
        RunStatus status,
        RunUsage? usage,
        RunError? error,
        CancellationToken cancellationToken)
    {
        // 🚨 Siniflandirici YALNIZ hata varken cagrilir: basarili bir
        // calistirmada (error is null) sicak yolda hic tetiklenmez.
        if (error is not null && _errorClassifier is not null)
        {
            var classification = _errorClassifier.Classify(error);
            error = error with { Class = classification.Class, Fingerprint = classification.Fingerprint };
        }

        // Sonuc gelmeden biten cagrilar acikca kapatilir; aksi halde arayuzde
        // "basladi ama bitmedi" gorunen bir tool karti kalirdi.
        foreach (var unfinished in scope.Tools.DrainUnfinished("Calistirma tool sonucu gelmeden sonlandi."))
        {
            await scope.Writer.RecordToolInvocationAsync(unfinished, cancellationToken).ConfigureAwait(false);
            _metrics?.RecordToolInvocation(unfinished.ToolName, succeeded: false, unfinished.Duration);
        }

        // Baglam sikistirmasinin (ozetleme) urettigi token'lar agent'in kendi
        // AgentResponse'undan tamamen ayri bir yan-kanal cagrisidir; burada
        // nihai kullanima katilmazsa maliyet raporu ve agac butcesi eksik kalir.
        usage = MergeUsage(usage, scope.ExtraUsage?.ToRunUsage());

        // Maliyet BURADA, nihai (birlestirilmis) kullanimdan hesaplanir — fiyat
        // anlik goruntusudur (bkz. docs/20-MALIYET-VE-GOSTERGE-PANELI.md bolum 20.2):
        // fiyat listesi sonradan degisirse bu calistirmanin maliyeti degismez.
        var cost = _pricingResolver?.Resolve(_modelProvider, _modelId, usage);

        await scope.Writer.CompleteAsync(status, usage, error, cost, cancellationToken).ConfigureAwait(false);

        // Butce agac boyunca paylasilan tek nesnedir; kok de alt calistirmalar da
        // ayni sayaci besler. Aksi halde "agac ne harcadi" sorusunun cevabi yalnizca
        // alt cagrilari kapsardi.
        scope.Budget?.RecordUsage(usage?.TotalTokens ?? 0);

        var elapsed = _timeProvider.GetElapsedTime(scope.StartedAt);

        // 🚨 Kota ve olay yayini YALNIZCA kok calistirmada isler. Alt calistirma
        // ayni kullanici isteginin parcasidir; ayrica sayilsaydi bir agent agaci
        // kotayi derinligi kadar hizli tuketirdi ve her dugum icin ayri bir
        // run.completed olayi yayilirdi.
        if (scope.Depth == 0)
        {
            await RecordQuotaAsync(scope, usage, cost, cancellationToken).ConfigureAwait(false);
            await PublishRunEventAsync(scope, status, usage, cost, error, elapsed, cancellationToken).ConfigureAwait(false);
            await SampleForOnlineEvalAsync(scope, status, cancellationToken).ConfigureAwait(false);
        }

        _metrics?.RecordRun(
            scope.AgentName,
            status,
            scope.TenantId,
            _modelId,
            elapsed,
            usage,
            agentVersion: _includeAgentVersionTag ? scope.AgentVersion : null);

        // Fiyat tanimsizsa (Source == Unknown) hicbir sey yayilmaz: bilinmeyen
        // maliyeti sifir olarak yaymak gercek harcamayi kucuk gosterirdi.
        // Iptal/hata durumunda da yayilir (Acik Soru 4): harcanan token icin
        // para zaten harcanmistir.
        if (cost is { Source: not PricingSource.Unknown } knownCost)
        {
            _metrics?.RecordCost(
                scope.AgentName,
                _modelId,
                scope.TenantId,
                (knownCost.InputCost ?? 0m) + (knownCost.OutputCost ?? 0m),
                knownCost.Currency ?? "unknown");
        }

        if (scope.Activity is null)
        {
            return;
        }

        scope.Activity.SetTag(AgentPrismDiagnostics.Tags.Status, status.ToString());

        if (error is not null)
        {
            scope.Activity.SetStatus(ActivityStatusCode.Error, error.Message);
        }

        // Span toplayiciya gecmeden ONCE durdurulur: durdurma ActivityStopped
        // olayini tetikler ve kok span'in kendisi de tampona girer.
        scope.Activity.Stop();

        // 🚨 Trace tamponunun sahibi YALNIZ kok calistirmadir. Agactaki her
        // calistirma ayni W3C trace kimligini paylasir (alt span'ler kok span'in
        // altina yerlesir) ve tampon o kimlikle anahtarlanir. Alt calistirma da
        // tamponu kapatsaydi -- ki once O biter -- tum agacin span'leri alt
        // calistirmaya baglanir, kok calistirma bos kalirdi. Olculdu (Faz 12):
        // gercek bir cagrida kokun /trace ucu 404, alt calistirmanınki dolu geldi.
        if (_traceCollector is not null && scope.OwnsTrace)
        {
            await _traceCollector.CompleteRunAsync(
                scope.Activity.TraceId.ToString(),
                scope.Writer.RunId,
                scope.TenantId,
                status,
                cancellationToken).ConfigureAwait(false);
        }

        scope.Activity.Dispose();
    }

    private async ValueTask WriteContentsAsync(
        RunScope scope,
        IEnumerable<AIContent> contents,
        CancellationToken cancellationToken)
    {
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when _options.RecordMessageDeltas && !string.IsNullOrEmpty(text.Text):
                    await scope.Writer.AppendAsync(
                        new RunEventDraft(RunEventType.MessageDelta) { Text = text.Text },
                        cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionCallContent call:
                    await WriteToolCallAsync(scope, call, cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionResultContent result:
                    await WriteToolResultAsync(scope, result, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    break;
            }
        }
    }

    private static async ValueTask WriteToolCallAsync(
        RunScope scope,
        FunctionCallContent call,
        CancellationToken cancellationToken)
    {
        var arguments = FormatArguments(call);

        scope.Tools.OnCall(call, source: null, arguments);

        await scope.Writer.AppendAsync(
            new RunEventDraft(RunEventType.ToolInvoking)
            {
                ToolName = call.Name,
                ToolCallId = call.CallId,
                Payload = arguments,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteToolResultAsync(
        RunScope scope,
        FunctionResultContent result,
        CancellationToken cancellationToken)
    {
        var record = scope.Tools.OnResult(result);

        await scope.Writer.AppendAsync(
            new RunEventDraft(result.Exception is null ? RunEventType.ToolInvoked : RunEventType.ToolFailed)
            {
                ToolName = record.ToolName,
                ToolCallId = result.CallId,
                Text = result.Exception?.Message,
                Payload = result.Result?.ToString(),
            },
            cancellationToken).ConfigureAwait(false);

        await scope.Writer.RecordToolInvocationAsync(record, cancellationToken).ConfigureAwait(false);

        _metrics?.RecordToolInvocation(record.ToolName, record.Succeeded, record.Duration);
    }

    private static string? FormatArguments(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
        {
            return null;
        }

        // Argumanlari AOT uyumlu kalmak icin elle bicimlendiriyoruz;
        // yansimaya dayanan JSON serilestirme kullanilmiyor.
        return string.Join(", ", call.Arguments.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    // Oturum kimligi AgentSessionManager tarafindan oturuma damgalanir. Damga yoksa
    // oturum AgentPrism disinda acilmis demektir; kayda yer tutucu bir deger yazmak
    // yerine bos birakilir.
    private static string? GetSessionId(AgentSession session) => AgentSessionIdentity.GetId(session);

    // Faz 45 (F-53): bu calistirmayi tetikleyen ilk kullanici mesaji
    // RunEventType.RunStarted olayina yazilir. run_events, girdi metninin
    // KALICILASTIGI TEK yerdir — oturum yalniz calistirma BASARIYLA
    // tamamlandiginda kaydedilir (AgentEndpoints.AgentRunStream), bu yuzden
    // basarisiz bir calistirmanin sorgusu baska hicbir yoldan okunamaz.
    private static string? ExtractQuery(IReadOnlyList<ChatMessage> messages)
        => messages.FirstOrDefault(static message => message.Role == ChatRole.User)?.Text;

    private static RunUsage? MergeUsage(RunUsage? primary, RunUsage? extra)
    {
        if (extra is null)
        {
            return primary;
        }

        if (primary is null)
        {
            return extra;
        }

        return new RunUsage
        {
            InputTokens = (primary.InputTokens ?? 0) + (extra.InputTokens ?? 0),
            OutputTokens = (primary.OutputTokens ?? 0) + (extra.OutputTokens ?? 0),
            TotalTokens = (primary.TotalTokens ?? 0) + (extra.TotalTokens ?? 0),
        };
    }

    private static RunUsage? ToRunUsage(UsageDetails? usage)
        => usage is null
            ? null
            : new RunUsage
            {
                InputTokens = usage.InputTokenCount,
                OutputTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
            };

    // AgentPrism istisnalari kendi kararli hata tipi adini tasiyabilir (ornegin
    // content_filtered). Varsayilan deger yine tipin tam adidir, bu yuzden mevcut
    // kayitlarin bicimi degismez.
    private static RunError ToRunError(Exception exception)
        => new()
        {
            Type = exception is AgentPrismException prismException
                ? prismException.ErrorType
                : exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
        };

    /// <summary>Kok span acildiktan sonra, depo yazimindan once bilinenler.</summary>
    private sealed record RunStart(
        AgentRunScope Scope,
        RunEventWriter Writer,
        string? SessionId,
        bool IsStreaming,
        Activity? Activity,
        Guid? ParentRunId,
        RunKind Kind,
        int? AgentVersion,
        Guid? ExperimentId,
        string? Variant,
        Guid? ReplayOfRunId);

    /// <summary>Tek bir calistirmanin kayit durumu.</summary>
    private sealed record RunScope(
        RunEventWriter Writer,
        Activity? Activity,
        ToolInvocationTracker Tools,
        string AgentName,
        string TenantId,
        long StartedAt,
        AgentRunBudget? Budget,
        CompactionUsageAccumulator? ExtraUsage,
        bool OwnsTrace,
        int? AgentVersion,
        Guid RunId,
        Guid RootRunId,
        int Depth,
        string? SessionId,
        RunKind Kind);
}
