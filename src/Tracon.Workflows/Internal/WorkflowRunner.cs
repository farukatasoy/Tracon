using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Runs workflows and writes every execution to a <c>runs</c> row.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A workflow execution is a run.</strong> It opens its own <c>runs</c>
/// row (<see cref="RunKind.Workflow"/>), and every agent called inside it
/// attaches below that row through the <c>parent_run_id</c> mechanism from
/// mechanism. The waterfall view is therefore drawn correctly with no extra code.
/// </para>
/// <para>
/// <strong>The run scope is written again before every
/// <c>MoveNextAsync</c>.</strong> The scope lives in an <c>AsyncLocal</c>, and an
/// assignment inside an async iterator body does not cross the
/// <c>yield return</c> boundary: the <c>ExecutionContext</c> is restored when the
/// call returns to the driver.; it also holds for workflow
/// execution, because executors run exactly inside that pump. When the scope is
/// lost, nested agent calls are rejected with "run recording is closed" and
/// checkpoints are written with no tenant.
/// </para>
/// <para>
/// <strong>Execution needs a <c>TurnToken</c>.</strong> Measured:
/// when the token is not sent, the graph only <em>swallows</em> the incoming
/// messages, turns <c>Idle</c> after the first super-step, and no agent speaks.
/// This behaviour is not written in the MAF documentation.
/// </para>
/// </remarks>
internal sealed class WorkflowRunner : IWorkflowRunner, IDisposable
{
    private static readonly ActivitySource ActivitySource = new(TraconDiagnostics.ActivitySourceName);

    private readonly WorkflowCatalog _catalog;
    private readonly IRunStore _runStore;
    private readonly IWorkflowCheckpointStore _checkpointStore;
    private readonly ITenantContext _tenantContext;
    private readonly IOptions<TraconWorkflowOptions> _options;
    private readonly IOptions<TraconOptions> _traconOptions;
    private readonly ILogger<WorkflowRunner> _logger;
    private readonly TraconMetrics? _metrics;
    private readonly RunTraceCollector? _traceCollector;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _concurrency;
    private readonly IRunCancellationRegistry? _cancellationRegistry;
    private readonly QuotaEnforcer? _quotaEnforcer;
    private readonly IRunAttributionContext? _attributionContext;
    private readonly IReadOnlyList<IRunEventSink> _sinks;

    /// <summary>Creates a new runner.</summary>
    /// <param name="catalog">The workflow catalog.</param>
    /// <param name="runStore">The run record store.</param>
    /// <param name="checkpointStore">The checkpoint store.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="options">The workflow settings.</param>
    /// <param name="traconOptions">The general Tracon settings.</param>
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
    /// <param name="attributionContext">
    /// The attribution context. When <see langword="null"/>, the workflow's run row records
    /// no user and no labels.
    /// </param>
    /// <param name="sinks">
    /// The run event observers. Empty when none is registered — the identical
    /// hot path as before this extension point existed.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public WorkflowRunner(
        WorkflowCatalog catalog,
        IRunStore runStore,
        IWorkflowCheckpointStore checkpointStore,
        ITenantContext tenantContext,
        IOptions<TraconWorkflowOptions> options,
        IOptions<TraconOptions> traconOptions,
        ILogger<WorkflowRunner> logger,
        TraconMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        TimeProvider? timeProvider = null,
        IRunCancellationRegistry? cancellationRegistry = null,
        QuotaEnforcer? quotaEnforcer = null,
        IRunAttributionContext? attributionContext = null,
        IEnumerable<IRunEventSink>? sinks = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(traconOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _runStore = runStore;
        _checkpointStore = checkpointStore;
        _tenantContext = tenantContext;
        _options = options;
        _traconOptions = traconOptions;
        _logger = logger;
        _metrics = metrics;
        _traceCollector = traceCollector;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _concurrency = new SemaphoreSlim(Math.Max(options.Value.MaxConcurrentRuns, 1));
        _cancellationRegistry = cancellationRegistry;
        _quotaEnforcer = quotaEnforcer;
        _attributionContext = attributionContext;
        _sinks = sinks?.ToArray() ?? [];
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
    /// This MUST be an <c>async IAsyncEnumerable</c> iterator —
    /// <see cref="WorkflowSessionId.Require"/> throws
    /// <see cref="TraconException"/> for an invalid <c>sessionId</c>. When
    /// that call stays outside the iterator BODY (in a synchronous helper
    /// method), the exception NEVER passes through the <c>catch</c> block in the
    /// <c>await foreach</c> of <c>WorkflowEventStream</c> (the SSE writer in
    /// <c>Tracon.AspNetCore</c>) and falls straight into the global
    /// <c>ExceptionHandlerMiddleware</c> of ASP.NET — instead of an SSE
    /// <c>event: error</c> the client gets a bare <c>HTTP 500</c> that carries no
    /// diagnostic information.
    /// </remarks>
    public async IAsyncEnumerable<RunEvent> RunStreamingAsync(
        WorkflowRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var execution = new WorkflowExecution
        {
            WorkflowName = request.WorkflowName,
            RunId = request.RunId ?? TraconId.NewId(),
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
    /// <exception cref="TraconException">
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
            RunId = request.NewRunId ?? TraconId.NewId(),
            SessionId = record.SessionId!,
            Message = null,
            ResumeFrom = await RequireCheckpointAsync(request.RunId, request.CheckpointId, cancellationToken)
                .ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Turns a response request into an execution recipe that resumes from a checkpoint.
    /// </summary>
    /// <exception cref="TraconException">
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
            throw new TraconException(
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
            throw new TraconException(
                $"There is no pending request with id '{request.RequestId}' on run '{request.RunId}'. " +
                "Refresh the request list with GET /api/workflows/runs/{runId}/requests.");
        }

        return new WorkflowExecution
        {
            WorkflowName = record.WorkflowName!,
            RunId = request.NewRunId ?? TraconId.NewId(),
            SessionId = record.SessionId!,
            Message = null,
            ResumeFrom = await RequireCheckpointAsync(request.RunId, request.CheckpointId, cancellationToken)
                .ConfigureAwait(false),
            Answers = [new WorkflowAnswer(request.RequestId, request.Approved, request.Text, request.Json)],
        };
    }

    /// <summary>Finds the run and validates that it is a resumable workflow row.</summary>
    /// <exception cref="TraconException">
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
            throw new TraconException($"There is no run with id '{runId}'.");
        }

        if (record.Kind != RunKind.Workflow || record.WorkflowName is not { Length: > 0 })
        {
            throw new TraconException(
                $"Run '{runId}' is not a workflow run; it cannot be resumed.");
        }

        if (record.SessionId is not { Length: > 0 })
        {
            throw new TraconException(
                $"Run '{runId}' has no execution session; it cannot be resumed.");
        }

        return record;
    }

    /// <summary>Selects the checkpoint to resume from.</summary>
    /// <exception cref="TraconException">The run has no checkpoint at all.</exception>
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
            : throw new TraconException(
                $"Run '{runId}' has no checkpoint. " +
                "A run started while checkpoint writing was disabled cannot be resumed.");
    }

    /// <summary>
    /// Looks up a single checkpoint's metadata (state omitted) for the
    /// diagnostic message built when resuming from it fails. Used only on
    /// the failure path — a successful resume never pays for this lookup.
    /// </summary>
    private async ValueTask<WorkflowCheckpointRecord?> FindCheckpointMetadataAsync(
        string sessionId,
        string checkpointId,
        CancellationToken cancellationToken)
    {
        var checkpoints = await _checkpointStore
            .ListAsync(_tenantContext.TenantId, sessionId, cancellationToken)
            .ConfigureAwait(false);

        return checkpoints.FirstOrDefault(
            candidate => string.Equals(candidate.CheckpointId, checkpointId, StringComparison.Ordinal));
    }

    private async IAsyncEnumerable<RunEvent> ExecuteAsync(
        WorkflowExecution execution,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled)
        {
            throw new TraconException(
                "Workflow execution is disabled. Enable the 'Tracon:Workflows:Enabled' setting.");
        }

        var settings = _options.Value;

        // Timeout and cancellation merge into a single token: even when the client
        // drops the connection, the server-side execution must not run forever.
        using var timeout = new CancellationTokenSource(settings.RunTimeout, _timeProvider);
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
        TraconWorkflowOptions settings,
        CancellationTokenSource timeout,
        CancellationTokenSource linked)
    {
        var recording = _traconOptions.Value.RunRecording;
        var writer = new RunEventWriter(_runStore, recording, _logger, execution.RunId, _metrics, _sinks);

        // 🚨 The root span is started IN THE BODY OF THIS METHOD. Activity.Current
        // is an AsyncLocal, and an assignment made inside a helper method does not
        // flow back to the caller; if the span were started there, the spans of the
        // agents would be siblings of the root span instead of its children
        // (measured in phase 6).
        var activity = ActivitySource.StartActivity(TraconDiagnostics.RunActivityName, ActivityKind.Internal);

        var scope = new AgentRunScope
        {
            RunId = execution.RunId,
            RootRunId = execution.RunId,
            Depth = 0,
            AgentName = execution.WorkflowName,
            TenantId = _tenantContext.TenantId,
            SessionId = execution.SessionId,
            Budget = _traconOptions.Value.AgentGraph.CreateBudget(_timeProvider),
            Writer = writer,
        };

        if (activity is not null)
        {
            activity.SetTag(TraconDiagnostics.Tags.RunId, execution.RunId);
            activity.SetTag(TraconDiagnostics.Tags.AgentName, execution.WorkflowName);
            activity.SetTag(TraconDiagnostics.Tags.TenantId, scope.TenantId);
            activity.SetTag(TraconDiagnostics.Tags.SessionId, execution.SessionId);
            activity.SetTag(TraconDiagnostics.Tags.Streaming, true);
            _traceCollector?.BeginRun(activity.TraceId.ToString(), activity.SpanId.ToString());
        }

        TraconRunContext.SetCurrent(scope);

        var attribution = RunAttributionReader.Read(
            _attributionContext,
            (message, exception) => _logger.LogWarning(
                exception,
                "{Message} Workflow run {RunId} continues and is recorded with no user and no labels.",
                message,
                execution.RunId));

        await writer.StartAsync(
            new RunStartInfo
            {
                RunId = execution.RunId,
                AgentName = execution.WorkflowName,
                Kind = RunKind.Workflow,
                WorkflowName = execution.WorkflowName,
                StartedAt = _timeProvider.GetUtcNow(),
                TenantId = scope.TenantId,

                // A workflow is ONE run for quota purposes (K-394), so it is one
                // run for attribution too: the whole workflow belongs to whoever
                // started it. The agent steps underneath resolve the same
                // ambient attribution independently and record it on their own
                // child rows.
                //
                // 🚨 Read through RunAttributionReader, NOT straight off the
                // interface: a consumer implementation can throw, and an
                // over-long user id would fail the insert on SQL Server
                // (user_id is nvarchar(200)) and silently disable the writer for
                // the whole workflow. The agent path shares this reader.
                UserId = attribution.UserId,
                Labels = attribution.Labels,
                SessionId = execution.SessionId,
                IsStreaming = true,
            },

            // Workflow runs start with a structured input, not a single user
            // message; phase 45's eval case promotion covers only agent
            // runs (docs/arsiv/fazlar/45-URETIMDEN-EVAL-KUMESI.md).
            query: null,
            linked.Token).ConfigureAwait(false);

        yield return FirstEvent(execution);

        var startedAt = _timeProvider.GetTimestamp();
        var status = RunStatus.Completed;
        RunError? error = null;

        // Graph setup is tried separately: an error here (agent not found,
        // invalid definition) is one the user can fix, and it must not leave
        // the run open.
        Workflow? workflow = null;

        try
        {
            workflow = await _catalog.ResolveAsync(execution.WorkflowName, linked.Token).ConfigureAwait(false)
                ?? throw new TraconException(
                    $"There is no workflow named '{execution.WorkflowName}'.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            status = RunStatus.Failed;
            error = ToRunError(exception, execution.RunId);
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

            // 🚨 MAF ends the stream QUIETLY when the token is canceled: no
            // OperationCanceledException reaches the pump, MoveNextAsync simply
            // returns false. Measured on a real sequential graph (2026-08-18,
            // defect F-107): the caller cancels, the graph stops after the
            // running step, and without this line the run was recorded as
            // Completed with error: null — the caller was told the opposite of
            // what happened. A silent stop is a CANCELLATION, not a completion.
            //
            // Only a would-be Completed run is rewritten: a run that already
            // failed keeps its error, and one waiting for a human answer keeps
            // AwaitingInput, because that state is a legitimate pause rather
            // than an outcome.
            if (linked.IsCancellationRequested && status == RunStatus.Completed)
            {
                status = RunStatus.Canceled;
            }

            // 🚨 The deadline is the one stop nobody asked for, so it is the one
            // that has to name itself. A caller who cancels already knows why
            // and gets no reason; a run that ran past 'Tracon:Workflows:RunTimeout'
            // gets the setting's name, on every path that ends it. Only the
            // timeout source is consulted, never the linked one: the linked
            // source is also tripped by the caller and by
            // POST /api/runs/{id}/cancel, and blaming the deadline for those
            // would put a wrong reason on a correct stop.
            if (status == RunStatus.Canceled && error is null && timeout.IsCancellationRequested)
            {
                error = TimedOut();
            }
        }

        await CompleteAsync(execution, scope, writer, status, error, activity, startedAt).ConfigureAwait(false);

        // The client must also see the closing event: if the stream cuts off
        // before saying "done", the client cannot tell a dropped connection
        // from a finished job.
        yield return LastEvent(execution, writer, status, error);
    }

    /// <summary>
    /// Runs the graph and streams its events. Errors are returned inside a
    /// <see cref="PumpedEvent"/>, not thrown as exceptions.
    /// </summary>
    /// <remarks>
    /// In an <c>async iterator</c> body, <c>yield return</c> cannot appear in
    /// the same block as <c>try/catch</c>; the error is therefore carried as a
    /// value and the caller turns it into a status.
    /// </remarks>
    private async IAsyncEnumerable<PumpedEvent> PumpAsync(
        Workflow workflow,
        WorkflowExecution execution,
        TraconWorkflowOptions settings,
        AgentRunScope scope,
        RunEventWriter writer,
        TraconRunRecordingOptions recording,
        CancellationTokenSource linked)
    {
        // 🚨 The scope is written BEFORE execution starts; the assignment
        // inside the loop is not enough. Measured (phase 15):
        // InProcessExecution.RunStreamingAsync starts a background task that
        // runs the executors, and that task captures the ExecutionContext AT
        // THAT EXACT MOMENT. If the scope were only written before
        // MoveNextAsync, nested agent calls would be rejected with "run
        // recording is closed" and the workflow would run silently empty --
        // producing no agent row and no checkpoint at all.
        TraconRunContext.SetCurrent(scope);

        StreamingRun? run = null;
        PumpedEvent? startupFailure = null;

        try
        {
            run = await StartAsync(workflow, execution, linked.Token).ConfigureAwait(false);
        }
        // 🚨 `when (linked.IsCancellationRequested)`. An
        // OperationCanceledException raised while neither the caller nor the
        // workflow timeout cancelled anything is a provider failure - most
        // often HttpClient reporting its own deadline - and recording it as a
        // cancelled workflow hid a real outage behind a user action.
        // Phase 157.
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            startupFailure = PumpedEvent.FromCancellation();
        }
        catch (Exception exception)
        {
            startupFailure = PumpedEvent.FromFailure(ToRunError(exception, execution.RunId));
        }

        // 🚨 `yield return` cannot appear inside a `catch` block (CS1631).
        // The error is therefore captured as a value and streamed AFTER the
        // block ends.
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

            // Answers for pending requests are looked up by id; the same
            // request is never answered twice.
            var answers = execution.Answers.ToDictionary(
                static answer => answer.RequestId,
                StringComparer.Ordinal);

            var delivered = new HashSet<string>(StringComparer.Ordinal);

            // 🚨 The stream must be reopened AFTER the response is sent.
            // Measured (phase 16): SendResponseAsync queues a message, but the
            // WatchStreamAsync enumerator being consumed at that moment has
            // already decided to finish; only a new enumerator sees the
            // continuing super-steps.
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
                        // 🚨 MAF does NOT honor an external cancellation token in
                        // the middle of a running step. Measured (2026-08-18,
                        // defect F-107) on a real sequential graph: the caller
                        // cancels, MoveNextAsync still returns the remaining
                        // events, the stream ends normally and the run was
                        // recorded as Completed with error: null — the opposite
                        // of what the caller was told, at full cost.
                        //
                        // The boundary between two super-steps is the first
                        // place Tracon can see the request, so cancellation
                        // is enforced HERE, using MAF's own CancelRunAsync path
                        // rather than abandoning the enumerator: a half-run step
                        // must still close through the framework.
                        if (linked.IsCancellationRequested)
                        {
                            await CancelAsync(run).ConfigureAwait(false);

                            yield return PumpedEvent.FromCancellation();

                            yield break;
                        }

                        WorkflowEvent? workflowEvent = null;
                        PumpedEvent? stepFailure = null;

                        try
                        {
                            // 🚨 The scope is rewritten on EVERY step; the reason
                            // is in the class documentation. Executors run
                            // exactly inside this call, so writing the scope
                            // only outside the loop is not enough.
                            TraconRunContext.SetCurrent(scope);

                            if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                            {
                                workflowEvent = enumerator.Current;
                            }
                        }
                        // Same filter, same reason as StartAsync above.
                        catch (OperationCanceledException) when (linked.IsCancellationRequested)
                        {
                            stepFailure = PumpedEvent.FromCancellation();
                        }
                        catch (Exception exception)
                        {
                            stepFailure = PumpedEvent.FromFailure(ToRunError(exception, execution.RunId));
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
                                Type = nameof(TraconException),
                                Message = $"Workflow exceeded the {settings.MaxSuperSteps} super-step limit and was " +
                                          "stopped. The handoff or group chat loop may not be terminating; " +
                                          "lower the 'maxIterations' value or add a termination condition to " +
                                          "the agent instructions.",
                            });

                            yield break;
                        }

                        // The response is sent BEFORE the event is written to the
                        // stream: a pending-request event should describe only a
                        // request that is genuinely still pending.
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
                                "Unknown workflow event '{EventType}' was skipped in run {RunId}. " +
                                "Microsoft Agent Framework may have added a new event type.",
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

                        // 🚨 A graph-level error FAILS the run. Measured
                        // (phase 16): when it was only written to the event
                        // stream without changing the status, an executor had
                        // crashed, no output was ever produced, and the run
                        // was still recorded as "Completed" - a row that
                        // looked green in the list but had no result.
                        if (workflowEvent is WorkflowErrorEvent graphError)
                        {
                            yield return PumpedEvent.FromFailure(ToRunError(graphError, execution.RunId));
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

                // 🚨 An answered request does NOT prove the orchestration still
                // has work left. Measured (2026-08-14, defect F-106 / HATA-K-003,
                // K-401): a Magentic orchestration that hits its round limit
                // produces its own WorkflowOutputEvent and finishes; the pump
                // then reopened the stream, Microsoft Agent Framework rejected
                // the call with "the orchestration has already completed", and
                // a run that had genuinely produced its result was recorded as
                // RunFailed.
                //
                // The framework's OWN state answers the question, so no message
                // text is matched: matching the round-limit sentence would break
                // the moment MAF rewords it.
                var statusBeforeReopen = await run.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);

                if (statusBeforeReopen is not Microsoft.Agents.AI.Workflows.RunStatus.PendingRequests
                    and not Microsoft.Agents.AI.Workflows.RunStatus.Running)
                {
                    break;
                }
            }

            // When a request is still pending, execution is not half-done, it
            // is SUSPENDED: its state has been written to a checkpoint and it
            // resumes from exactly here once a human answer arrives. Counting
            // this as an error would wrongly mark the run as failed.
            var finalStatus = await run.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);

            if (finalStatus == Microsoft.Agents.AI.Workflows.RunStatus.PendingRequests)
            {
                yield return settings.EnableCheckpointing
                    ? PumpedEvent.FromAwaiting()
                    : PumpedEvent.FromFailure(new RunError
                    {
                        Type = nameof(TraconException),
                        Message = "Workflow is waiting for a human answer, but checkpoint writing is " +
                                  "disabled, so this wait cannot be resumed. " +
                                  "Enable the 'Tracon:Workflows:EnableCheckpointing' setting.",
                    });
            }
        }
    }

    /// <summary>
    /// Sends the answer we hold for a pending request, if any.
    /// </summary>
    /// <returns>
    /// Whether the answer was sent, and whether a conversion error occurred while sending it.
    /// </returns>
    /// <remarks>
    /// A conversion error (wrong type, missing field) <strong>ends the
    /// run</strong>. Swallowing it and going back to waiting would make the
    /// user resend the same answer over and over with no way to see why it
    /// does not progress.
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
        catch (TraconException exception)
        {
            return new ResponseDelivery(false, new RunError
            {
                Type = nameof(TraconException),
                Message = exception.Message,
            });
        }

        await run.SendResponseAsync(response).ConfigureAwait(false);

        return new ResponseDelivery(true, null);
    }

    /// <summary>Starts the graph and triggers the first turn.</summary>
    /// <remarks>
    /// If <c>TurnToken</c> is not sent, the graph only swallows the
    /// incoming message and no agent speaks. Measured.
    /// </remarks>
    private async ValueTask<StreamingRun> StartAsync(
        Workflow workflow,
        WorkflowExecution execution,
        CancellationToken cancellationToken)
    {
        var checkpointManager = _options.Value.EnableCheckpointing
            ? CheckpointManager.CreateJson(new TraconCheckpointStore(_checkpointStore, _tenantContext), null)
            : null;

        StreamingRun run;

        if (execution.ResumeFrom is { } checkpointId)
        {
            if (checkpointManager is null)
            {
                throw new TraconException(
                    "'Tracon:Workflows:EnableCheckpointing' must be enabled to resume from a checkpoint.");
            }

            // Fetched once and reused below: the proactive generation check
            // needs it before attempting resume, and the failure message
            // (if resume still fails for another reason) needs the same
            // record - fetching it twice would waste a query on the common,
            // successful path AND on the failure path.
            var recorded = await FindCheckpointMetadataAsync(execution.SessionId, checkpointId, cancellationToken)
                .ConfigureAwait(false);

            if (recorded?.StateSchemaVersion > TraconCheckpointStore.CurrentStateSchemaVersion)
            {
                throw new TraconException(
                    $"Workflow '{execution.WorkflowName}' cannot be resumed from checkpoint '{checkpointId}': " +
                    $"it was written with Tracon schema generation {recorded.StateSchemaVersion}; " +
                    $"this Tracon version can read up to generation {TraconCheckpointStore.CurrentStateSchemaVersion}. " +
                    "Update the Tracon packages.");
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
                // MAF's raw message ("not compatible with the workflow") does
                // not say what to do about it. The only real cause is that
                // executor ids have changed: either the workflow definition
                // was updated, or the application was restarted.
                throw new TraconException(
                    $"Workflow '{execution.WorkflowName}' cannot be resumed from this checkpoint: " +
                    "the graph's structure differs from when the checkpoint was written. " +
                    "If the workflow definition changed, start a new run. " +
                    "If the application restarted, old checkpoints cannot be used - " +
                    "executor ids are generated in process memory.",
                    exception);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                // Unlike the InvalidDataException case above (a known cause:
                // executor ids changed), this is the checkpoint STATE itself
                // failing to deserialize - the same class of failure
                // AgentSessionManager reports for sessions. The message
                // reports what was RECORDED, not a guess.
                var recordedMafVersion = recorded?.StateMafVersion ?? "unknown (written before version stamping existed)";

                throw new TraconException(
                    $"Workflow '{execution.WorkflowName}' cannot be resumed from checkpoint '{checkpointId}': " +
                    "its state could not be read. It was written with Microsoft Agent Framework " +
                    $"{recordedMafVersion}; this process is running {TraconCheckpointStore.CurrentMafVersion}. " +
                    "If the Microsoft Agent Framework version changed, start a new run instead of resuming.",
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

            // A SEPARATE overload is called when checkpointing is off: the
            // manager parameter is not nullable, and passing null would crash
            // at run time.
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

        // 🚨 MT-WF-062: an EXTRA TurnToken must NOT be sent on a /respond call
        // (Answers non-empty): the checkpoint already republishes the pending
        // request BY ITSELF (docs/arsiv/fazlar/16-WORKFLOWS-ARAYUZ.md #3) - the extra token
        // has a VISIBLE side effect whenever the graph's ENTRY node is an
        // AIAgentBinding (an agent-host SUBSCRIBED to the turn token): the
        // agent thinks it is "a new turn", reruns FROM SCRATCH, produces a
        // second (unanswered) WorkflowRequest, and the stream falls back into
        // AwaitingInput - previously MEASURED, a pattern that repeated 100% of
        // the time on EVERY respond call. On a graph made of plain executors
        // (not subscribed to TurnToken) this extra token had no visible
        // effect, which is why the existing coverage (ApprovalWorkflow,
        // WorkflowRunnerTests) never caught the defect at all.
        // 🚨 This is DELIBERATELY distinguished from a PLAIN /resume (Answers
        // EMPTY): the first attempt skipped the token on ALL resumes (even
        // when Answers was empty) and empirically left the
        // KontrolNoktasindanSurdurulur test hanging for up to 10 minutes (a
        // stream with no TurnToken on a checkpoint that has no pending request
        // at all, FULLY Idle, never ends naturally - it only stops at
        // RunTimeout). Resumes OTHER than ANSWERING a pending request
        // therefore KEEP the OLD behavior (always sending the token).
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
        // A workflow row has no model of its own (phase 20): only the agent
        // runs beneath it carry cost, which shows up in the tree total.
        await writer.CompleteAsync(
            status,
            usage: null,
            error,
            cost: null,
            modelId: null,
            modelProvider: null,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        await RecordQuotaAsync(execution, scope, CancellationToken.None).ConfigureAwait(false);

        // 🚨 The checkpoints of a run awaiting a human are NEVER deleted: the
        // response resumes from exactly those checkpoints. The cleanup
        // setting is only for runs that have genuinely finished.
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

        activity.SetTag(TraconDiagnostics.Tags.Status, status.ToString());

        if (error is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, error.Message);
        }

        // The span is stopped BEFORE it goes to the collector: stopping
        // triggers the ActivityStopped event, and the root span itself also
        // enters the buffer.
        activity.Stop();

        if (_traceCollector is not null)
        {
            await _traceCollector.CompleteRunAsync(
                activity.TraceId.ToString(),
                activity.SpanId.ToString(),
                execution.RunId,
                scope.TenantId ?? _tenantContext.TenantId,
                status,
                CancellationToken.None).ConfigureAwait(false);
        }

        activity.Dispose();
    }

    /// <summary>Writes the ENTIRE workflow (root level, a single "run") to the quota counters.</summary>
    /// <remarks>
    /// <para>
    /// workflow runs used to skip quota accounting
    /// ENTIRELY — only <c>RunRecordingAgent</c> (the agent run path) reached
    /// <c>QuotaEnforcer</c>; <see cref="IWorkflowRunner"/> never touched it.
    /// </para>
    /// <para>
    /// A workflow row has no <c>usage</c>/<c>cost</c> of its own (
    /// see the note on <see cref="CompleteAsync"/>); consumption is read from
    /// the total of the run tree that just completed
    /// (<see cref="RunRecord.TreeUsage"/>/<see cref="RunRecord.TreeCost"/>).
    /// The ENTIRE workflow counts as a SINGLE "run" — not per step (the same
    /// "root vs. step" design decision already explained on the agent side,
    /// identical in rationale to <see cref="RunRecordingAgent"/>'s
    /// <c>Depth == 0</c> rule).
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
                    ? treeCost.Total()
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
            // Cleanup is not functional; its failure does not affect the run.
            _logger.LogWarning(
                exception,
                "Could not delete checkpoints for run {RunId}.",
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
            // A run that cannot be canceled does not change the fact that the
            // limit was exceeded; the error is swallowed and the run is
            // closed anyway.
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

    /// <summary>The reason carried by a run that ran past its deadline.</summary>
    /// <remarks>
    /// Written once and read from the single place that closes a cancelled run.
    /// The status stays <see cref="RunStatus.Canceled"/> - a deadline stops a
    /// run, it does not fault one - so this text is the only thing that tells
    /// an operator a timeout apart from a cancellation somebody asked for.
    /// </remarks>
    private static RunError TimedOut()
        => new()
        {
            Type = nameof(TimeoutException),
            Message = "Workflow timed out and was stopped. " +
                      "Raise the 'Tracon:Workflows:RunTimeout' value, or " +
                      "shorten the graph.",
        };

    /// <summary>
    /// Converts an exception into a run error; strips reflection/handler-invocation
    /// wrappers.
    /// </summary>
    /// <remarks>
    /// MAF's internal execution pipeline (for example a Magentic turn-token /
    /// external-response handler) can throw an exception wrapped in a
    /// <see cref="TargetInvocationException"/> or a single-element
    /// <see cref="AggregateException"/>. The wrapped message carries only a
    /// meaningless text like "Error invoking handler for ..."; the real cause
    /// stays in <c>InnerException</c>, and if written unwrapped the operator
    /// never sees the actual fault at all.
    /// Only SINGLE-layer, single-inner-exception wrappers are stripped — a
    /// direct code error (for example a genuine `AggregateException` with
    /// multiple inner exceptions) is left as is.
    /// </remarks>
    private RunError ToRunError(Exception exception, Guid runId)
    {
        var unwrapped = exception switch
        {
            TargetInvocationException { InnerException: { } inner } => inner,
            AggregateException { InnerExceptions.Count: 1 } aggregate => aggregate.InnerExceptions[0],
            _ => exception,
        };

        var correlationId = SafeErrorText.NewCorrelationId();
        _logger.LogError(unwrapped, "Workflow run {RunId} failed. (ref: {CorrelationId})", runId, correlationId);

        return new RunError
        {
            Type = unwrapped.GetType().FullName ?? unwrapped.GetType().Name,
            Message = SafeErrorText.ForPersistence(unwrapped, correlationId),
        };
    }

    /// <summary>Converts a graph-level error into a run error.</summary>
    private RunError ToRunError(WorkflowErrorEvent failure, Guid runId)
        => failure.Exception is { } exception
            ? ToRunError(exception, runId)
            : new RunError
            {
                Type = nameof(WorkflowErrorEvent),
                Message = "Workflow execution stopped with an error.",
            };

    /// <summary>The recipe for a single run.</summary>
    private sealed record WorkflowExecution
    {
        public required string WorkflowName { get; init; }

        public required Guid RunId { get; init; }

        public required string SessionId { get; init; }

        public required string? Message { get; init; }

        /// <summary>The checkpoint to resume from. <see langword="null"/> for a new run.</summary>
        public required string? ResumeFrom { get; init; }

        /// <summary>
        /// The answers to give to pending requests. Only populated on the <c>/respond</c> path.
        /// </summary>
        public IReadOnlyList<WorkflowAnswer> Answers { get; init; } = [];
    }

    /// <summary>The result of one attempt to send a response.</summary>
    /// <param name="Delivered">Whether the response was sent to execution.</param>
    /// <param name="Failure">The error, if the response could not be converted.</param>
    private readonly record struct ResponseDelivery(bool Delivered, RunError? Failure);

    /// <summary>A single result coming out of the pump: an event, an error, a cancellation, or a wait.</summary>
    private readonly record struct PumpedEvent(RunEvent? Event, RunError? Failure, bool Canceled, bool Awaiting)
    {
        public static PumpedEvent FromEvent(RunEvent runEvent) => new(runEvent, null, false, false);

        public static PumpedEvent FromFailure(RunError error) => new(null, error, false, false);

        /// <summary>Execution is waiting for a human answer.</summary>
        public static PumpedEvent FromAwaiting() => new(null, null, false, true);

        /// <summary>Execution stopped because a token was cancelled.</summary>
        /// <remarks>
        /// The reason is deliberately not built here. A deadline can end a
        /// run through three separate doors - startup, a super-step boundary,
        /// a cancelled MoveNextAsync - and MAF can also end its stream quietly,
        /// through no door at all. While each door built its own reason, the
        /// quiet path had none: the runner produced a RunError naming the
        /// setting and then dropped it, and the operator saw a bare
        /// cancellation at full cost. The reason is attached once now, where
        /// the run is closed, so no path can be added that forgets it.
        /// </remarks>
        public static PumpedEvent FromCancellation() => new(null, null, true, false);
    }
}
