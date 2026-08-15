using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Runs workflows and writes every execution to a <c>runs</c> row.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A workflow execution is a run.</strong> It opens its own <c>runs</c>
/// row (<see cref="RunKind.Workflow"/>), and every agent called inside it
/// attaches below that row through the <c>parent_run_id</c> mechanism from
/// phase 12. The waterfall view is therefore drawn correctly with no extra code.
/// </para>
/// <para>
/// 🚨 <strong>The run scope is written again before every
/// <c>MoveNextAsync</c>.</strong> The scope lives in an <c>AsyncLocal</c>, and an
/// assignment inside an async iterator body does not cross the
/// <c>yield return</c> boundary: the <c>ExecutionContext</c> is restored when the
/// call returns to the driver. Measured in phase 12; it also holds for workflow
/// execution, because executors run exactly inside that pump. When the scope is
/// lost, nested agent calls are rejected with "run recording is closed" and
/// checkpoints are written with no tenant.
/// </para>
/// <para>
/// 🚨 <strong>Execution needs a <c>TurnToken</c>.</strong> Measured (phase 15):
/// when the token is not sent, the graph only <em>swallows</em> the incoming
/// messages, turns <c>Idle</c> after the first super-step, and no agent speaks.
/// This behaviour is not written in the MAF documentation.
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
    private readonly IRunCancellationRegistry? _cancellationRegistry;
    private readonly QuotaEnforcer? _quotaEnforcer;

    /// <summary>Creates a new runner.</summary>
    /// <param name="catalog">The workflow catalog.</param>
    /// <param name="runStore">The run record store.</param>
    /// <param name="checkpointStore">The checkpoint store.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="options">The workflow settings.</param>
    /// <param name="prismOptions">The general AgentPrism settings.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The metric instruments.</param>
    /// <param name="traceCollector">The span collector.</param>
    /// <param name="timeProvider">The time source.</param>
    /// <param name="cancellationRegistry">
    /// The cancellation registry. When <see langword="null"/>, a workflow run cannot be canceled from outside.
    /// </param>
    /// <param name="quotaEnforcer">
    /// The quota enforcer. When <see langword="null"/>, workflow consumption is not written to the quota
    /// counters (the SAME behaviour as the agent run path, see <c>RunRecordingAgent</c>).
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
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
        TimeProvider? timeProvider = null,
        IRunCancellationRegistry? cancellationRegistry = null,
        QuotaEnforcer? quotaEnforcer = null)
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
        _cancellationRegistry = cancellationRegistry;
        _quotaEnforcer = quotaEnforcer;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => _catalog.ListAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default)
        => _catalog.GetAsync(name, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<WorkflowGraph?> GetGraphAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var workflow = await _catalog.ResolveAsync(name, cancellationToken).ConfigureAwait(false);

        return workflow is null ? null : WorkflowGraphReader.Read(name, workflow);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireWorkflowRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // A run that was answered STAYS in AwaitingInput (status history is not
        // rewritten afterwards, K-014) but it no longer has a pending request;
        // the continuing work lives on a new run row. The card is shown only for
        // a run that really waits.
        if (record.Status != RunStatus.AwaitingInput)
        {
            return [];
        }

        var requests = new List<WorkflowPendingRequest>();

        await foreach (var runEvent in _runStore
                           .ReadEventsAsync(runId, 0, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (runEvent.Type != RunEventType.WorkflowRequest)
            {
                continue;
            }

            if (WorkflowRequestDescriptor.Parse(runEvent) is { } request)
            {
                requests.Add(request);
            }
        }

        return requests;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> RespondStreamingAsync(
        WorkflowRespondRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var execution = await PrepareResponseAsync(request, cancellationToken).ConfigureAwait(false);

        await foreach (var runEvent in ExecuteAsync(execution, cancellationToken).ConfigureAwait(false))
        {
            yield return runEvent;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// This MUST be an <c>async IAsyncEnumerable</c> iterator (K-403) —
    /// <see cref="WorkflowSessionId.Require"/> throws
    /// <see cref="AgentPrismException"/> for an invalid <c>sessionId</c>. When
    /// that call stays outside the iterator BODY (in a synchronous helper
    /// method), the exception NEVER passes through the <c>catch</c> block in the
    /// <c>await foreach</c> of <c>WorkflowEventStream</c> (the SSE writer in
    /// <c>AgentPrism.AspNetCore</c>) and falls straight into the global
    /// <c>ExceptionHandlerMiddleware</c> of ASP.NET — instead of an SSE
    /// <c>event: error</c> the client gets a bare <c>HTTP 500</c> that carries no
    /// diagnostic information (BUG-K-005). For the fourth repetition of the same
    /// class, in the async helper method of the calling layer, see the
    /// core-execution note under <c>docs/hafiza/</c>.
    /// </remarks>
    public async IAsyncEnumerable<RunEvent> RunStreamingAsync(
        WorkflowRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var execution = new WorkflowExecution
        {
            WorkflowName = request.WorkflowName,
            RunId = request.RunId ?? AgentPrismId.NewId(),
            SessionId = WorkflowSessionId.Require(request.SessionId),
            Message = request.Message,
            ResumeFrom = null,
        };

        await foreach (var runEvent in ExecuteAsync(execution, cancellationToken).ConfigureAwait(false))
        {
            yield return runEvent;
        }
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
    /// Turns a resume request into a runnable execution recipe.
    /// </summary>
    /// <exception cref="AgentPrismException">
    /// The run does not exist, is not a workflow run, belongs to another tenant,
    /// or the requested checkpoint cannot be found.
    /// </exception>
    private async ValueTask<WorkflowExecution> PrepareResumeAsync(
        WorkflowResumeRequest request,
        CancellationToken cancellationToken)
    {
        var record = await RequireWorkflowRunAsync(request.RunId, cancellationToken).ConfigureAwait(false);

        return new WorkflowExecution
        {
            WorkflowName = record.WorkflowName!,
            RunId = request.NewRunId ?? AgentPrismId.NewId(),
            SessionId = record.SessionId!,
            Message = null,
            ResumeFrom = await RequireCheckpointAsync(request.RunId, request.CheckpointId, cancellationToken)
                .ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Turns a response request into an execution recipe that resumes from a checkpoint.
    /// </summary>
    /// <exception cref="AgentPrismException">
    /// The run cannot be found, does not wait for human input, or the given
    /// request id does not exist on that run.
    /// </exception>
    private async ValueTask<WorkflowExecution> PrepareResponseAsync(
        WorkflowRespondRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId, nameof(request));

        var record = await RequireWorkflowRunAsync(request.RunId, cancellationToken).ConfigureAwait(false);

        if (record.Status != RunStatus.AwaitingInput)
        {
            throw new AgentPrismException(
                $"Run '{request.RunId}' is not awaiting human input " +
                $"(status: {record.Status}). Only a run in 'AwaitingInput' status " +
                "can be responded to.");
        }

        // The request id IS VALIDATED. A resume with an unknown id would tell the
        // user "answered" while the execution ended still waiting - a silence
        // that is hard to debug.
        var pending = await ListPendingRequestsAsync(request.RunId, cancellationToken).ConfigureAwait(false);

        if (!pending.Any(candidate => string.Equals(candidate.RequestId, request.RequestId, StringComparison.Ordinal)))
        {
            throw new AgentPrismException(
                $"There is no pending request with id '{request.RequestId}' on run '{request.RunId}'. " +
                "Refresh the request list with GET /api/workflows/runs/{runId}/requests.");
        }

        return new WorkflowExecution
        {
            WorkflowName = record.WorkflowName!,
            RunId = request.NewRunId ?? AgentPrismId.NewId(),
            SessionId = record.SessionId!,
            Message = null,
            ResumeFrom = await RequireCheckpointAsync(request.RunId, request.CheckpointId, cancellationToken)
                .ConfigureAwait(false),
            Answers = [new WorkflowAnswer(request.RequestId, request.Approved, request.Text, request.Json)],
        };
    }

    /// <summary>Finds the run and validates that it is a resumable workflow row.</summary>
    /// <exception cref="AgentPrismException">
    /// The run does not exist, belongs to another tenant, is not a workflow row,
    /// or carries no execution session.
    /// </exception>
    private async ValueTask<RunRecord> RequireWorkflowRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var record = await _runStore.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // When the tenant does not match, the answer is "not found". Not even the
        // knowledge that another tenant's run exists is leaked.
        if (record is null ||
            !string.Equals(record.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            throw new AgentPrismException($"There is no run with id '{runId}'.");
        }

        if (record.Kind != RunKind.Workflow || record.WorkflowName is not { Length: > 0 })
        {
            throw new AgentPrismException(
                $"Run '{runId}' is not a workflow run; it cannot be resumed.");
        }

        if (record.SessionId is not { Length: > 0 })
        {
            throw new AgentPrismException(
                $"Run '{runId}' has no execution session; it cannot be resumed.");
        }

        return record;
    }

    /// <summary>Selects the checkpoint to resume from.</summary>
    /// <exception cref="AgentPrismException">The run has no checkpoint at all.</exception>
    private async ValueTask<string> RequireCheckpointAsync(
        Guid runId,
        string? checkpointId,
        CancellationToken cancellationToken)
    {
        if (checkpointId is { Length: > 0 } explicitId)
        {
            return explicitId;
        }

        var checkpoints = await _checkpointStore
            .ListByRunAsync(_tenantContext.TenantId, runId, cancellationToken)
            .ConfigureAwait(false);

        return checkpoints.Count > 0
            ? checkpoints[^1].CheckpointId
            : throw new AgentPrismException(
                $"Run '{runId}' has no checkpoint. " +
                "A run started while checkpoint writing was disabled cannot be resumed.");
    }

    private async IAsyncEnumerable<RunEvent> ExecuteAsync(
        WorkflowExecution execution,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled)
        {
            throw new AgentPrismException(
                "Workflow execution is disabled. Enable the 'AgentPrism:Workflows:Enabled' setting.");
        }

        var settings = _options.Value;

        // Timeout and cancellation merge into a single token: even when the client
        // drops the connection, the server-side execution must not run forever.
        using var timeout = new CancellationTokenSource(settings.RunTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        // The workflow row is the root of its own tree (RunId == RootRunId): an
        // external cancellation (POST /api/runs/{id}/cancel) triggers this source,
        // and the agent runs below the same RootRunId are canceled too.
        using var cancellationRegistration = _cancellationRegistry?.Register(
            execution.RunId,
            execution.RunId,
            _tenantContext.TenantId,
            linked);

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

        // 🚨 The root span is started IN THE BODY OF THIS METHOD. Activity.Current
        // is an AsyncLocal, and an assignment made inside a helper method does not
        // flow back to the caller; if the span were started there, the spans of the
        // agents would be siblings of the root span instead of its children
        // (measured in phase 6).
        var activity = ActivitySource.StartActivity(AgentPrismDiagnostics.RunActivityName, ActivityKind.Internal);

        var scope = new AgentRunScope
        {
            RunId = execution.RunId,
            RootRunId = execution.RunId,
            Depth = 0,
            AgentName = execution.WorkflowName,
            TenantId = _tenantContext.TenantId,
            SessionId = execution.SessionId,
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

            // Workflow calistirmalari yapilandirilmis bir girdiyle baslar, tek bir
            // kullanici mesajiyla degil; Faz 45'in eval vaka terfisi yalniz agent
            // calistirmalarini kapsar (docs/45-URETIMDEN-EVAL-KUMESI.md).
            query: null,
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
                    $"There is no workflow named '{execution.WorkflowName}'.");
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

                if (produced.Awaiting)
                {
                    status = RunStatus.AwaitingInput;
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

            // Bekleyen isteklere verilecek yanitlar kimlige gore aranir; ayni
            // istek iki kez yanitlanmaz.
            var answers = execution.Answers.ToDictionary(
                static answer => answer.RequestId,
                StringComparer.Ordinal);

            var delivered = new HashSet<string>(StringComparer.Ordinal);

            // 🚨 Yanit gonderildikten SONRA akis yeniden acilmalidir. Olculdu
            // (Faz 16): SendResponseAsync bir mesaj kuyruklar ama o sirada
            // tuketilen WatchStreamAsync numaralandiricisi zaten bitmeye karar
            // vermistir; yalnizca yeni bir numaralandirici devam eden
            // super-step'leri gorur.
            while (true)
            {
                var responded = false;
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

                        // Yanit, olay akisa yazilmadan ONCE gonderilir: bekleyen
                        // istek olayi yalnizca gercekten bekleyen bir istegi
                        // anlatmalidir.
                        if (workflowEvent is RequestInfoEvent info)
                        {
                            var delivery = await TryRespondAsync(run, info.Request, answers, delivered)
                                .ConfigureAwait(false);

                            if (delivery.Failure is { } deliveryFailure)
                            {
                                yield return PumpedEvent.FromFailure(deliveryFailure);
                                yield break;
                            }

                            if (delivery.Delivered)
                            {
                                responded = true;

                                continue;
                            }
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

                        // 🚨 Graf hatasi calistirmayi BASARISIZ yapar. Olculdu
                        // (Faz 16): olay akisina yazilip durum degistirilmeyince
                        // bir executor patlamis, cikti hic uretilmemis ve
                        // calistirma yine de "Completed" kaydedilmisti - listede
                        // yesil gorunen ama hicbir sonucu olmayan bir satir.
                        if (workflowEvent is WorkflowErrorEvent graphError)
                        {
                            yield return PumpedEvent.FromFailure(ToRunError(graphError));
                        }
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }

                if (!responded)
                {
                    break;
                }
            }

            // Hâlâ bekleyen bir istek varsa yurutme yarim degil, ASKIDADIR:
            // durumu kontrol noktasina yazilmistir ve bir insan yaniti geldiginde
            // tam olarak buradan devam eder. Bunu hata saymak, calistirmayi
            // basarisiz gostermek olurdu.
            var finalStatus = await run.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);

            if (finalStatus == Microsoft.Agents.AI.Workflows.RunStatus.PendingRequests)
            {
                yield return settings.EnableCheckpointing
                    ? PumpedEvent.FromAwaiting()
                    : PumpedEvent.FromFailure(new RunError
                    {
                        Type = nameof(AgentPrismException),
                        Message = "Workflow bir insan yaniti bekliyor ancak kontrol noktasi yazimi kapali " +
                                  "oldugu icin bu bekleme sürdürulemez. " +
                                  "'AgentPrism:Workflows:EnableCheckpointing' ayarini acin.",
                    });
            }
        }
    }

    /// <summary>
    /// Bekleyen bir istege elimizde yanit varsa gonderir.
    /// </summary>
    /// <returns>
    /// Yanit gonderildi mi ve gonderilirken bir cevrim hatasi olustu mu.
    /// </returns>
    /// <remarks>
    /// Cevrim hatasi (yanlis tip, eksik alan) <strong>calistirmayi bitirir</strong>.
    /// Yutulup bekleme durumuna donulseydi kullanici ayni yaniti tekrar tekrar
    /// gonderir ve neden ilerlemedigini goremezdi.
    /// </remarks>
    private static async ValueTask<ResponseDelivery> TryRespondAsync(
        StreamingRun run,
        ExternalRequest request,
        Dictionary<string, WorkflowAnswer> answers,
        HashSet<string> delivered)
    {
        if (!answers.TryGetValue(request.RequestId, out var answer) || !delivered.Add(request.RequestId))
        {
            return default;
        }

        ExternalResponse response;

        try
        {
            response = WorkflowResponseFactory.Create(request, answer);
        }
        catch (AgentPrismException exception)
        {
            return new ResponseDelivery(false, new RunError
            {
                Type = nameof(AgentPrismException),
                Message = exception.Message,
            });
        }

        await run.SendResponseAsync(response).ConfigureAwait(false);

        return new ResponseDelivery(true, null);
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
                    "'AgentPrism:Workflows:EnableCheckpointing' must be enabled to resume from a checkpoint.");
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
                    $"Workflow '{execution.WorkflowName}' cannot be resumed from this checkpoint: " +
                    "the graph's structure differs from when the checkpoint was written. " +
                    "If the workflow definition changed, start a new run. " +
                    "If the application restarted, old checkpoints cannot be used - " +
                    "executor ids are generated in process memory.",
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

        // 🚨 MT-WF-062: bir /respond cagrisinda (Answers dolu) EKSTRA bir
        // TurnToken gonderilmemelidir: kontrol noktasi bekleyen istegi zaten
        // KENDISI yeniden yayinlar (docs/16-WORKFLOWS-ARAYUZ.md #3) - fazladan
        // token'in graf GIRIS dugumu bir AIAgentBinding (turn-token'a ABONE bir
        // agent-host) oldugunda GORUNUR bir yan etkisi vardir: agent "yeni bir
        // tur" sanip SIFIRDAN yeniden calisir, ikinci (karsiliksiz) bir
        // WorkflowRequest uretir ve akis yeniden AwaitingInput'a duser - onceden
        // OLCULDU, HER respond cagrisinda %100 tekrarlanan bir desendi. Duz
        // executor'lardan kurulu bir grafta (TurnToken'a abone degiller) bu
        // fazladan token'in gorunur etkisi yoktu, bu yuzden mevcut coverage
        // (ApprovalWorkflow, WorkflowRunnerTests) kusuru hic yakalamamisti.
        // 🚨 Bu, DUZ bir /resume'dan (Answers BOS) BILEREK ayirt edilir: ilk
        // denemede TUM surdurmelerde (Answers bos olsa bile) token atlanmisti
        // ve ampirik olarak KontrolNoktasindanSurdurulur testini 10 dakikaya
        // kadar asili birakti (bekleyen hicbir istegi olmayan, TAMAMEN Idle bir
        // kontrol noktasinda TurnToken'siz akis hicbir zaman dogal olarak
        // bitmiyor, yalniz RunTimeout'ta duruyor). Bir bekleyen istegi CEVAPLAMA
        // disindaki surdurmeler bu yuzden ESKI (her zaman token gonderen)
        // davranisi KORUR.
        if (execution.Answers.Count == 0)
        {
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);
        }

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
        // Bir workflow satirinin kendi modeli yoktur (Faz 20): maliyeti yalniz
        // altindaki agent calistirmalari tasir, agac toplaminda gorunur.
        await writer.CompleteAsync(status, usage: null, error, cost: null, CancellationToken.None).ConfigureAwait(false);

        await RecordQuotaAsync(execution, scope, CancellationToken.None).ConfigureAwait(false);

        // 🚨 Insan bekleyen bir calistirmanin kontrol noktalari ASLA silinmez:
        // yanit tam olarak onlardan devam eder. Temizlik ayari yalnizca gercekten
        // sonuclanmis calistirmalar icindir.
        if (!_options.Value.KeepCheckpointsAfterCompletion && status != RunStatus.AwaitingInput)
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

    /// <summary>Workflow'un TAMAMINI (kok seviyesi, tek bir "run") kota sayaclarina yazar.</summary>
    /// <remarks>
    /// <para>
    /// 🚨 HATA-S1-006: workflow calistirmalari daha once kota muhasebesini
    /// TAMAMEN atliyordu — <c>QuotaEnforcer</c>'a yalniz <c>RunRecordingAgent</c>
    /// (agent calistirma yolu) degiyordu, <see cref="IWorkflowRunner"/> hicbir
    /// zaman ona ugramiyordu.
    /// </para>
    /// <para>
    /// Bir workflow satirinin kendi <c>usage</c>/<c>cost</c>'u yoktur (Faz 20,
    /// <see cref="CompleteAsync"/>'in kendi notuna bkz.); tuketim, az once
    /// tamamlanan calistirma agacinin toplamindan (<see cref="RunRecord.TreeUsage"/>/
    /// <see cref="RunRecord.TreeCost"/>) okunur. Workflow'un TAMAMI TEK bir
    /// "run" sayilir — adim basina degil (agent tarafinda zaten aciklanan ayni
    /// "kok mu adim mi" tasarim karari, <see cref="RunRecordingAgent"/>'in
    /// <c>Depth == 0</c> kuraliyla birebir ayni gerekce).
    /// </para>
    /// </remarks>
    private async ValueTask RecordQuotaAsync(WorkflowExecution execution, AgentRunScope scope, CancellationToken cancellationToken)
    {
        if (_quotaEnforcer is null)
        {
            return;
        }

        var record = await _runStore.GetRunAsync(execution.RunId, cancellationToken).ConfigureAwait(false);

        await _quotaEnforcer.RecordAsync(
            new QuotaConsumption
            {
                TenantId = scope.TenantId ?? _tenantContext.TenantId,
                AgentName = execution.WorkflowName,
                Runs = 1,
                Tokens = record?.TreeUsage?.TotalTokens ?? 0,
                Cost = record?.TreeCost is { } treeCost
                    ? (treeCost.InputCost ?? 0m) + (treeCost.OutputCost ?? 0m)
                    : null,
                OccurredAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken).ConfigureAwait(false);
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
            Type = status switch
            {
                RunStatus.Completed => RunEventType.RunCompleted,
                RunStatus.AwaitingInput => RunEventType.RunAwaitingInput,
                _ => RunEventType.RunFailed,
            },
            Timestamp = _timeProvider.GetUtcNow(),
            Text = error?.Message,
        };

    /// <summary>
    /// Bir istisnayi calistirma hatasina cevirir; reflection/handler-cagrisi
    /// sarmalayicilarini (K-400) soyar.
    /// </summary>
    /// <remarks>
    /// MAF'in ic yurutme boru hatti (ornegin bir Magentic tur-token/dis-yanit
    /// isleyicisi) bir istisnayi <see cref="TargetInvocationException"/> veya
    /// tek elemanli bir <see cref="AggregateException"/> ile sarmalayarak
    /// firlatabilir. Sarmalanmamis mesaj yalniz "Error invoking handler for
    /// ..." gibi anlamsiz bir metin tasir; gercek neden <c>InnerException</c>'da
    /// kalir ve sarmalanmadan yazilirsa operator asil arizayi hic goremez
    /// (HATA-K-003, `MT-WF-071`/`073`). Yalniz TEK katmanli, tek-ic-istisnali
    /// sarmalayicilar soyulur — dogrudan bir kod hatasi (ornegin coklu ic
    /// istisnali gercek bir `AggregateException`) oldugu gibi birakilir.
    /// </remarks>
    private static RunError ToRunError(Exception exception)
    {
        var unwrapped = exception switch
        {
            TargetInvocationException { InnerException: { } inner } => inner,
            AggregateException { InnerExceptions.Count: 1 } aggregate => aggregate.InnerExceptions[0],
            _ => exception,
        };

        return new RunError
        {
            Type = unwrapped.GetType().FullName ?? unwrapped.GetType().Name,
            Message = unwrapped.Message,
        };
    }

    /// <summary>Graf duzeyinde bir hatayi calistirma hatasina cevirir.</summary>
    private static RunError ToRunError(WorkflowErrorEvent failure)
        => failure.Exception is { } exception
            ? ToRunError(exception)
            : new RunError
            {
                Type = nameof(WorkflowErrorEvent),
                Message = "Workflow yurutmesi bir hata ile durdu.",
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

        /// <summary>
        /// Bekleyen isteklere verilecek yanitlar. Yalnizca <c>/respond</c> yolunda dolu.
        /// </summary>
        public IReadOnlyList<WorkflowAnswer> Answers { get; init; } = [];
    }

    /// <summary>Bir yanit gonderme denemesinin sonucu.</summary>
    /// <param name="Delivered">Yanit yurutmeye gonderildi mi.</param>
    /// <param name="Failure">Yanit cevrilemediyse hata.</param>
    private readonly record struct ResponseDelivery(bool Delivered, RunError? Failure);

    /// <summary>Pompadan cikan tek bir sonuc: olay, hata, iptal veya bekleme.</summary>
    private readonly record struct PumpedEvent(RunEvent? Event, RunError? Failure, bool Canceled, bool Awaiting)
    {
        public static PumpedEvent FromEvent(RunEvent runEvent) => new(runEvent, null, false, false);

        public static PumpedEvent FromFailure(RunError error) => new(null, error, false, false);

        /// <summary>Yurutme bir insan yanitini bekliyor.</summary>
        public static PumpedEvent FromAwaiting() => new(null, null, false, true);

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
                    false,
                    false)
                : new PumpedEvent(null, null, true, false);
    }
}
