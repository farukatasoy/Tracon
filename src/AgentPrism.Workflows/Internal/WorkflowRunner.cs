using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Workflow'lari calistirir ve her yurutmeyi bir <c>runs</c> satirina yazar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Workflow calistirmasi bir calistirmadir.</strong> Kendi
/// <c>runs</c> satirini acar (<see cref="RunKind.Workflow"/>) ve icinde cagrilan
/// her agent, Faz 12'nin <c>parent_run_id</c> mekanizmasiyla o satirin altina
/// baglanir. Waterfall gorunumu bu sayede ek bir kod olmadan dogru cizilir.
/// </para>
/// <para>
/// 🚨 <strong>Calistirma kapsami her <c>MoveNextAsync</c> oncesinde yeniden
/// yazilir.</strong> Kapsam bir <c>AsyncLocal</c>'de yasar ve bir async iterator
/// govdesindeki atama <c>yield return</c> sinirini asmaz: cagri driver'a
/// dondugunde <c>ExecutionContext</c> geri alinir. Faz 12'de olculdu; workflow
/// yurutmesinde de gecerlidir cunku executor'lar tam olarak o pompanin icinde
/// calisir. Kapsam kaybolursa alt agent cagrilari "calistirma kaydi kapali"
/// diyerek reddedilir ve kontrol noktalari kiracisiz yazilir.
/// </para>
/// <para>
/// 🚨 <strong>Yurutme bir <c>TurnToken</c> ister.</strong> Olculdu (Faz 15):
/// token gonderilmezse graf gelen mesajlari yalnizca <em>yutar</em>, ilk
/// super-step'ten sonra <c>Idle</c> olur ve hicbir agent konusmaz. Bu davranis
/// MAF dokumaninda yazili degildir.
/// </para>
/// </remarks>
internal sealed class WorkflowRunner : IWorkflowRunner, IDisposable
{
    private static readonly ActivitySource ActivitySource = new(AgentPrismDiagnostics.ActivitySourceName);

    private readonly WorkflowCatalog _catalog;
    private readonly IRunStore _runStore;
    private readonly IWorkflowCheckpointStore _checkpointStore;
    private readonly ITenantContext _tenantContext;
    private readonly IOptions<AgentPrismWorkflowOptions> _options;
    private readonly IOptions<AgentPrismOptions> _prismOptions;
    private readonly ILogger<WorkflowRunner> _logger;
    private readonly AgentPrismMetrics? _metrics;
    private readonly RunTraceCollector? _traceCollector;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _concurrency;

    /// <summary>Yeni bir kosucu olusturur.</summary>
    /// <param name="catalog">Workflow katalogu.</param>
    /// <param name="runStore">Calistirma kaydi deposu.</param>
    /// <param name="checkpointStore">Kontrol noktasi deposu.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="options">Workflow ayarlari.</param>
    /// <param name="prismOptions">Genel AgentPrism ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="metrics">Metrik aletleri.</param>
    /// <param name="traceCollector">Span toplayici.</param>
    /// <param name="timeProvider">Zaman kaynagi.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public WorkflowRunner(
        WorkflowCatalog catalog,
        IRunStore runStore,
        IWorkflowCheckpointStore checkpointStore,
        ITenantContext tenantContext,
        IOptions<AgentPrismWorkflowOptions> options,
        IOptions<AgentPrismOptions> prismOptions,
        ILogger<WorkflowRunner> logger,
        AgentPrismMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(prismOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _runStore = runStore;
        _checkpointStore = checkpointStore;
        _tenantContext = tenantContext;
        _options = options;
        _prismOptions = prismOptions;
        _logger = logger;
        _metrics = metrics;
        _traceCollector = traceCollector;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _concurrency = new SemaphoreSlim(Math.Max(options.Value.MaxConcurrentRuns, 1));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => _catalog.ListAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default)
        => _catalog.GetAsync(name, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<RunEvent> RunStreamingAsync(
        WorkflowRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteAsync(
            new WorkflowExecution
            {
                WorkflowName = request.WorkflowName,
                RunId = request.RunId ?? AgentPrismId.NewId(),
                SessionId = WorkflowSessionId.Require(request.SessionId),
                Message = request.Message,
                ResumeFrom = null,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> ResumeStreamingAsync(
        WorkflowResumeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var execution = await PrepareResumeAsync(request, cancellationToken).ConfigureAwait(false);

        await foreach (var runEvent in ExecuteAsync(execution, cancellationToken).ConfigureAwait(false))
        {
            yield return runEvent;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _concurrency.Dispose();

    /// <summary>
    /// Sürdürme istegini, calistirilabilir bir yurutme tarifine cevirir.
    /// </summary>
    /// <exception cref="AgentPrismException">
    /// Calistirma yoksa, workflow calistirmasi degilse, baska bir kiraciya aitse
    /// veya istenen kontrol noktasi bulunamiyorsa.
    /// </exception>
    private async ValueTask<WorkflowExecution> PrepareResumeAsync(
        WorkflowResumeRequest request,
        CancellationToken cancellationToken)
    {
        var record = await _runStore.GetRunAsync(request.RunId, cancellationToken).ConfigureAwait(false);

        // Kiraci eslesmezse "bulunamadi" denir. Baska bir kiracinin
        // calistirmasinin var oldugu bilgisi bile sizdirilmaz.
        if (record is null ||
            !string.Equals(record.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            throw new AgentPrismException($"'{request.RunId}' kimlikli calistirma bulunamadi.");
        }

        if (record.Kind != RunKind.Workflow || record.WorkflowName is not { Length: > 0 } workflowName)
        {
            throw new AgentPrismException(
                $"'{request.RunId}' kimlikli calistirma bir workflow calistirmasi degil; sürdürulemez.");
        }

        if (record.SessionId is not { Length: > 0 } sessionId)
        {
            throw new AgentPrismException(
                $"'{request.RunId}' kimlikli calistirmanin yurutme oturumu yok; sürdürulemez.");
        }

        var checkpointId = request.CheckpointId;

        if (checkpointId is null)
        {
            var checkpoints = await _checkpointStore
                .ListByRunAsync(_tenantContext.TenantId, request.RunId, cancellationToken)
                .ConfigureAwait(false);

            checkpointId = checkpoints.Count > 0 ? checkpoints[^1].CheckpointId : null;
        }

        if (checkpointId is null)
        {
            throw new AgentPrismException(
                $"'{request.RunId}' kimlikli calistirmanin kontrol noktasi yok. " +
                "Kontrol noktasi yazimi kapaliyken baslatilan bir calistirma sürdürulemez.");
        }

        return new WorkflowExecution
        {
            WorkflowName = workflowName,
            RunId = request.NewRunId ?? AgentPrismId.NewId(),
            SessionId = sessionId,
            Message = null,
            ResumeFrom = checkpointId,
        };
    }

    private async IAsyncEnumerable<RunEvent> ExecuteAsync(
        WorkflowExecution execution,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled)
        {
            throw new AgentPrismException(
                "Workflow calistirma kapali. 'AgentPrism:Workflows:Enabled' ayarini acin.");
        }

        var settings = _options.Value;

        // Zaman asimi ve iptal tek bir belirtecte birlesir: istemci baglantiyi
        // kesse de sunucu tarafindaki yurutme sonsuza kadar surmemelidir.
        using var timeout = new CancellationTokenSource(settings.RunTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        await _concurrency.WaitAsync(linked.Token).ConfigureAwait(false);

        try
        {
            await foreach (var runEvent in RunGuardedAsync(execution, settings, timeout, linked).ConfigureAwait(false))
            {
                yield return runEvent;
            }
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async IAsyncEnumerable<RunEvent> RunGuardedAsync(
        WorkflowExecution execution,
        AgentPrismWorkflowOptions settings,
        CancellationTokenSource timeout,
        CancellationTokenSource linked)
    {
        var recording = _prismOptions.Value.RunRecording;
        var writer = new RunEventWriter(_runStore, recording, _logger, execution.RunId);

        // 🚨 Kok span BU METODUN GOVDESINDE acilir. Activity.Current bir
        // AsyncLocal'dir ve bir yardimci metodun icinde yapilan atama cagirana
        // geri akmaz; span orada acilsaydi agent'larin span'leri kok span'in
        // cocugu degil kardesi olurdu (Faz 6'da olculdu).
        var activity = ActivitySource.StartActivity(AgentPrismDiagnostics.RunActivityName, ActivityKind.Internal);

        var scope = new AgentRunScope
        {
            RunId = execution.RunId,
            RootRunId = execution.RunId,
            Depth = 0,
            AgentName = execution.WorkflowName,
            TenantId = _tenantContext.TenantId,
            Budget = _prismOptions.Value.AgentGraph.CreateBudget(),
            Writer = writer,
        };

        if (activity is not null)
        {
            activity.SetTag(AgentPrismDiagnostics.Tags.RunId, execution.RunId);
            activity.SetTag(AgentPrismDiagnostics.Tags.AgentName, execution.WorkflowName);
            activity.SetTag(AgentPrismDiagnostics.Tags.TenantId, scope.TenantId);
            activity.SetTag(AgentPrismDiagnostics.Tags.SessionId, execution.SessionId);
            activity.SetTag(AgentPrismDiagnostics.Tags.Streaming, true);
            _traceCollector?.BeginRun(activity.TraceId.ToString());
        }

        AgentPrismRunContext.SetCurrent(scope);

        await writer.StartAsync(
            new RunStartInfo
            {
                RunId = execution.RunId,
                AgentName = execution.WorkflowName,
                Kind = RunKind.Workflow,
                WorkflowName = execution.WorkflowName,
                StartedAt = _timeProvider.GetUtcNow(),
                TenantId = scope.TenantId,
                SessionId = execution.SessionId,
                IsStreaming = true,
            },
            linked.Token).ConfigureAwait(false);

        yield return FirstEvent(execution);

        var startedAt = _timeProvider.GetTimestamp();
        var status = RunStatus.Completed;
        RunError? error = null;

        // Graf kurulumu ayri denenir: burada olusan bir hata (agent bulunamadi,
        // tanim gecersiz) kullanicinin duzeltebilecegi bir hatadir ve
        // calistirmayi acik birakmamalidir.
        Workflow? workflow = null;

        try
        {
            workflow = await _catalog.ResolveAsync(execution.WorkflowName, linked.Token).ConfigureAwait(false);

            if (workflow is null)
            {
                throw new AgentPrismException(
                    $"'{execution.WorkflowName}' adinda bir workflow yok.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            status = RunStatus.Failed;
            error = ToRunError(exception);
        }

        if (workflow is not null)
        {
            await foreach (var produced in PumpAsync(
                                   workflow,
                                   execution,
                                   settings,
                                   scope,
                                   writer,
                                   recording,
                                   timeout,
                                   linked)
                               .ConfigureAwait(false))
            {
                if (produced.Failure is { } failure)
                {
                    status = RunStatus.Failed;
                    error = failure;
                    continue;
                }

                if (produced.Canceled)
                {
                    status = RunStatus.Canceled;
                    continue;
                }

                yield return produced.Event!;
            }
        }

        await CompleteAsync(execution, scope, writer, status, error, activity, startedAt).ConfigureAwait(false);

        // Kapanis olayini da istemci gormelidir: akis "bitti" demeden kesilirse
        // istemci baglantinin koptugunu mu yoksa isin bittigini mi anlayamaz.
        yield return LastEvent(execution, writer, status, error);
    }

    /// <summary>
    /// Grafi calistirir ve olaylarini akitir. Hatalar istisna olarak degil
    /// <see cref="PumpedEvent"/> icinde dondurulur.
    /// </summary>
    /// <remarks>
    /// Bir <c>async iterator</c> govdesinde <c>yield return</c> ile
    /// <c>try/catch</c> ayni blokta bulunamaz; bu yuzden hata bir deger olarak
    /// tasinir ve cagiran onu duruma cevirir.
    /// </remarks>
    private async IAsyncEnumerable<PumpedEvent> PumpAsync(
        Workflow workflow,
        WorkflowExecution execution,
        AgentPrismWorkflowOptions settings,
        AgentRunScope scope,
        RunEventWriter writer,
        AgentPrismRunRecordingOptions recording,
        CancellationTokenSource timeout,
        CancellationTokenSource linked)
    {
        // 🚨 Kapsam yurutme BASLAMADAN once yazilir, dongudeki atama yetmez.
        // Olculdu (Faz 15): InProcessExecution.RunStreamingAsync executor'lari
        // suren bir arka plan gorevi baslatir ve o gorev ExecutionContext'i
        // TAM O ANDA yakalar. Kapsam yalnizca MoveNextAsync oncesinde
        // yazilsaydi alt agent cagrilari "calistirma kaydi kapali" diyerek
        // reddedilir, workflow sessizce bos calisirdi -- hicbir agent satiri
        // ve hicbir kontrol noktasi olusmazdi.
        AgentPrismRunContext.SetCurrent(scope);

        StreamingRun? run = null;
        PumpedEvent? startupFailure = null;

        try
        {
            run = await StartAsync(workflow, execution, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            startupFailure = PumpedEvent.FromCancellation(timeout.IsCancellationRequested);
        }
        catch (Exception exception)
        {
            startupFailure = PumpedEvent.FromFailure(ToRunError(exception));
        }

        // 🚨 `yield return` bir `catch` blogunun icinde yazilamaz (CS1631).
        // Hata bu yuzden bir degere alinir ve blok bittikten SONRA akitilir.
        if (startupFailure is { } failure)
        {
            yield return failure;
            yield break;
        }

        if (run is null)
        {
            yield break;
        }

        await using (run.ConfigureAwait(false))
        {
            var superSteps = 0;
            var enumerator = run
                .WatchStreamAsync(blockOnPendingRequest: false, linked.Token)
                .GetAsyncEnumerator(linked.Token);

            try
            {
                while (true)
                {
                    WorkflowEvent? workflowEvent = null;
                    PumpedEvent? stepFailure = null;

                    try
                    {
                        // 🚨 Kapsam HER adimda yeniden yazilir; gerekcesi sinif
                        // aciklamasindadir. Executor'lar tam olarak bu cagrinin
                        // icinde calisir, dolayisiyla kapsami yalnizca dongunun
                        // disinda yazmak yetmez.
                        AgentPrismRunContext.SetCurrent(scope);

                        if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                        {
                            workflowEvent = enumerator.Current;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        stepFailure = PumpedEvent.FromCancellation(timeout.IsCancellationRequested);
                    }
                    catch (Exception exception)
                    {
                        stepFailure = PumpedEvent.FromFailure(ToRunError(exception));
                    }

                    if (stepFailure is { } failed)
                    {
                        yield return failed;
                        yield break;
                    }

                    if (workflowEvent is null)
                    {
                        break;
                    }

                    if (workflowEvent is SuperStepStartedEvent && ++superSteps > settings.MaxSuperSteps)
                    {
                        await CancelAsync(run).ConfigureAwait(false);

                        yield return PumpedEvent.FromFailure(new RunError
                        {
                            Type = nameof(AgentPrismException),
                            Message = $"Workflow {settings.MaxSuperSteps} super-step sinirini asti ve durduruldu. " +
                                      "Devretme veya grup sohbeti dongusu sonlanmiyor olabilir; " +
                                      "'maxIterations' degerini dusurun veya agent talimatlarina bir " +
                                      "bitirme kosulu ekleyin.",
                        });

                        yield break;
                    }

                    var mapping = WorkflowEventMapper.Map(workflowEvent);

                    if (!mapping.IsKnown)
                    {
                        _logger.LogWarning(
                            "Bilinmeyen workflow olayi '{EventType}' calistirma {RunId} icinde atlandi. " +
                            "Microsoft Agent Framework yeni bir olay tipi eklemis olabilir.",
                            workflowEvent.GetType().Name,
                            execution.RunId);

                        continue;
                    }

                    if (mapping.Draft is not { } draft)
                    {
                        continue;
                    }

                    if (draft.Type == RunEventType.MessageDelta && !recording.RecordMessageDeltas)
                    {
                        continue;
                    }

                    yield return PumpedEvent.FromEvent(
                        await writer.AppendAsync(draft, linked.Token).ConfigureAwait(false));
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            // Bekleyen bir dis istek varsa yurutme yarim kalmistir. Faz 15
            // human-in-the-loop yanitini TASIMAZ; bunu sessizce "tamamlandi"
            // saymak, kullaniciya bitmemis bir isi bitmis gostermek olurdu.
            var finalStatus = await run.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);

            if (finalStatus == Microsoft.Agents.AI.Workflows.RunStatus.PendingRequests)
            {
                yield return PumpedEvent.FromFailure(new RunError
                {
                    Type = nameof(AgentPrismException),
                    Message = "Workflow disaridan bir yanit bekliyor ve bu surumde yanit verilemiyor. " +
                              "Insan onayi gerektiren desenler (plan onayi, dis istek portlari) " +
                              "sonraki fazda desteklenecektir.",
                });
            }
        }
    }

    /// <summary>Grafi baslatir ve ilk turu tetikler.</summary>
    /// <remarks>
    /// 🚨 <c>TurnToken</c> gonderilmezse graf gelen mesaji yalnizca yutar ve
    /// hicbir agent konusmaz. Olculdu (Faz 15).
    /// </remarks>
    private async ValueTask<StreamingRun> StartAsync(
        Workflow workflow,
        WorkflowExecution execution,
        CancellationToken cancellationToken)
    {
        var checkpointManager = _options.Value.EnableCheckpointing
            ? CheckpointManager.CreateJson(new AgentPrismCheckpointStore(_checkpointStore, _tenantContext), null)
            : null;

        StreamingRun run;

        if (execution.ResumeFrom is { } checkpointId)
        {
            if (checkpointManager is null)
            {
                throw new AgentPrismException(
                    "Kontrol noktasindan sürdürme icin 'AgentPrism:Workflows:EnableCheckpointing' acik olmalidir.");
            }

            try
            {
                run = await InProcessExecution.ResumeStreamingAsync(
                    workflow,
                    new CheckpointInfo(execution.SessionId, checkpointId),
                    checkpointManager,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                // MAF'in ham mesaji ("not compatible with the workflow") ne
                // yapilmasi gerektigini soylemez. Tek gercek sebebi executor
                // kimliklerinin degismis olmasidir: ya workflow tanimi
                // guncellenmistir ya da uygulama yeniden baslatilmistir.
                throw new AgentPrismException(
                    $"'{execution.WorkflowName}' workflow'u bu kontrol noktasindan sürdürulemiyor: " +
                    "grafin yapisi kontrol noktasi yazildigi andakinden farkli. " +
                    "Workflow tanimi degistirildiyse yeni bir calistirma baslatin. " +
                    "Uygulama yeniden baslatildiysa eski kontrol noktalari kullanilamaz - " +
                    "executor kimlikleri surec belleğinde uretilir.",
                    exception);
            }
        }
        else
        {
            var input = new List<ChatMessage>();

            if (execution.Message is { Length: > 0 } message)
            {
                input.Add(new ChatMessage(ChatRole.User, message));
            }

            // Kontrol noktasi kapaliyken AYRI bir asiri yukleme cagrilir:
            // yonetici parametresi nullable degildir ve null gecmek calisma
            // aninda patlardi.
            run = checkpointManager is null
                ? await InProcessExecution.RunStreamingAsync(
                    workflow,
                    input,
                    execution.SessionId,
                    cancellationToken).ConfigureAwait(false)
                : await InProcessExecution.RunStreamingAsync(
                    workflow,
                    input,
                    checkpointManager,
                    execution.SessionId,
                    cancellationToken).ConfigureAwait(false);
        }

        await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);

        return run;
    }

    private async ValueTask CompleteAsync(
        WorkflowExecution execution,
        AgentRunScope scope,
        RunEventWriter writer,
        RunStatus status,
        RunError? error,
        Activity? activity,
        long startedAt)
    {
        await writer.CompleteAsync(status, usage: null, error, CancellationToken.None).ConfigureAwait(false);

        if (!_options.Value.KeepCheckpointsAfterCompletion)
        {
            await DiscardCheckpointsAsync(execution).ConfigureAwait(false);
        }

        var elapsed = _timeProvider.GetElapsedTime(startedAt);

        _metrics?.RecordRun(
            execution.WorkflowName,
            status,
            scope.TenantId ?? _tenantContext.TenantId,
            modelId: null,
            elapsed,
            usage: null);

        if (activity is null)
        {
            return;
        }

        activity.SetTag(AgentPrismDiagnostics.Tags.Status, status.ToString());

        if (error is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, error.Message);
        }

        // Span toplayiciya gecmeden ONCE durdurulur: durdurma ActivityStopped
        // olayini tetikler ve kok span'in kendisi de tampona girer.
        activity.Stop();

        if (_traceCollector is not null)
        {
            await _traceCollector.CompleteRunAsync(
                activity.TraceId.ToString(),
                execution.RunId,
                scope.TenantId ?? _tenantContext.TenantId,
                status,
                CancellationToken.None).ConfigureAwait(false);
        }

        activity.Dispose();
    }

    private async ValueTask DiscardCheckpointsAsync(WorkflowExecution execution)
    {
        try
        {
            await _checkpointStore
                .DeleteAsync(_tenantContext.TenantId, execution.SessionId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Temizlik islevsel degildir; basarisizligi calistirmayi etkilemez.
            _logger.LogWarning(
                exception,
                "Calistirma {RunId} icin kontrol noktalari silinemedi.",
                execution.RunId);
        }
    }

    private static async ValueTask CancelAsync(StreamingRun run)
    {
        try
        {
            await run.CancelRunAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Iptal edilemeyen bir yurutme, sinirin asildigi gercegini
            // degistirmez; hata yutulur ve calistirma yine de kapatilir.
        }
    }

    private RunEvent FirstEvent(WorkflowExecution execution)
        => new()
        {
            RunId = execution.RunId,
            Sequence = 0,
            Type = RunEventType.RunStarted,
            Timestamp = _timeProvider.GetUtcNow(),
            Text = execution.WorkflowName,
            Payload = execution.SessionId,
        };

    private RunEvent LastEvent(
        WorkflowExecution execution,
        RunEventWriter writer,
        RunStatus status,
        RunError? error)
        => new()
        {
            RunId = execution.RunId,
            Sequence = writer.EventCount,
            Type = status == RunStatus.Completed ? RunEventType.RunCompleted : RunEventType.RunFailed,
            Timestamp = _timeProvider.GetUtcNow(),
            Text = error?.Message,
        };

    private static RunError ToRunError(Exception exception)
        => new()
        {
            Type = exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
        };

    /// <summary>Tek bir yurutmenin tarifi.</summary>
    private sealed record WorkflowExecution
    {
        public required string WorkflowName { get; init; }

        public required Guid RunId { get; init; }

        public required string SessionId { get; init; }

        public required string? Message { get; init; }

        /// <summary>Sürdürulecek kontrol noktasi. Yeni calistirmada <see langword="null"/>.</summary>
        public required string? ResumeFrom { get; init; }
    }

    /// <summary>Pompadan cikan tek bir sonuc: olay, hata veya iptal.</summary>
    private readonly record struct PumpedEvent(RunEvent? Event, RunError? Failure, bool Canceled)
    {
        public static PumpedEvent FromEvent(RunEvent runEvent) => new(runEvent, null, false);

        public static PumpedEvent FromFailure(RunError error) => new(null, error, false);

        public static PumpedEvent FromCancellation(bool timedOut)
            => timedOut
                ? new PumpedEvent(
                    null,
                    new RunError
                    {
                        Type = nameof(TimeoutException),
                        Message = "Workflow zaman asimina ugradi ve durduruldu. " +
                                  "'AgentPrism:Workflows:RunTimeout' degerini yukseltin veya " +
                                  "grafi kisaltin.",
                    },
                    false)
                : new PumpedEvent(null, null, true);
    }
}
