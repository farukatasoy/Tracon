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
        IWebhookPublisher? webhookPublisher = null)
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

        var scope = await BeginRunAsync(start, cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

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

        var scope = await BeginRunAsync(start, cancellationToken).ConfigureAwait(false);
        UsageDetails? usage = null;
        string? pendingApproval = null;
        var enumerator = base.RunCoreStreamingAsync(messages, session, options, cancellationToken).GetAsyncEnumerator(cancellationToken);

        try
        {
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
                        break;
                    }

                    update = enumerator.Current;
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

                await WriteContentsAsync(scope, update.Contents, cancellationToken).ConfigureAwait(false);

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        await CompleteAsync(
            scope,
            pendingApproval is null ? RunStatus.Completed : RunStatus.Failed,
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
            Budget = prismOptions?.Budget ?? (depth == 0 ? _graphOptions.CreateBudget() : null),
            Writer = writer,
            ExtraUsage = new CompactionUsageAccumulator(),
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
            prismOptions?.Variant);
    }

    private async ValueTask<RunScope> BeginRunAsync(RunStart start, CancellationToken cancellationToken)
    {
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
            },
            cancellationToken).ConfigureAwait(false);

        return new RunScope(
            start.Writer,
            start.Activity,
            new ToolInvocationTracker(start.Scope.RunId, start.IsStreaming, _timeProvider),
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
            SessionId: start.SessionId);
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
        }

        _metrics?.RecordRun(
            scope.AgentName,
            status,
            scope.TenantId,
            _modelId,
            elapsed,
            usage,
            agentVersion: _includeAgentVersionTag ? scope.AgentVersion : null);

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
        string? Variant);

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
        string? SessionId);
}
