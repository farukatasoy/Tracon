using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
    private readonly RunRecordingServices _services;
    private readonly WorkflowRunErrors _errors;
    private readonly WorkflowRequestReader _reader;
    private readonly WorkflowResumePlanner _planner;
    private readonly WorkflowParameterBinder _binder;
    private readonly IWorkflowCheckpointStore _checkpointStore;
    private readonly IOptions<TraconWorkflowOptions> _options;
    private readonly IOptions<TraconOptions> _traconOptions;
    private readonly ILogger<WorkflowRunner> _logger;
    private readonly SlotPool _concurrency;
    private readonly TenantSlotLimiter? _tenantSlots;
    private readonly IWorkflowResponseStore? _responses;
    private readonly IWorkflowRequestStore? _requests;
    private readonly WorkflowAgentCache? _agentCache;

    /// <summary>Creates a new runner.</summary>
    /// <param name="catalog">The workflow catalog.</param>
    /// <param name="checkpointStore">The checkpoint store.</param>
    /// <param name="options">The workflow settings.</param>
    /// <param name="traconOptions">The general Tracon settings.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="services">
    /// The services and settings every recorder of a run shares: the run store, the tenant context, the time
    /// source, the metrics and the span collector, the quota counter, the cancellation registry, the attribution
    /// context, the event observers, the error classifier, the webhook publisher, the input store, the tool
    /// approval presenter and the reconciliation settings. A member that is <see langword="null"/> switches its
    /// feature off, the same rule the agent run path follows (<see cref="RunRecordingServices"/>). A runner built
    /// with no reconciliation settings writes the first heartbeat of a run that waits for a slot at once.
    /// </param>
    /// <param name="parameterFactory">
    /// Looks up the participants' definitions for the start-of-run coverage check
    /// and applies the configured value length limit. When <see langword="null"/>
    /// the coverage check is skipped.
    /// </param>
    /// <param name="responseStore">
    /// Where a run that answers a request records that it handed the answer over. When
    /// <see langword="null"/> nothing is recorded, and an answer whose run fails after the hand-over
    /// can be given again.
    /// </param>
    /// <param name="requestStore">
    /// The inbox of the requests a run waits on. When <see langword="null"/> a run that waits writes no
    /// row: the tenant-wide list stays empty, no request expires, and a request carries no expiry.
    /// </param>
    /// <param name="agentCache">
    /// The agent wrappers of the workflows, which name the agent behind an executor id. When <see langword="null"/> a
    /// tool approval request does not name the agent that asked, and an approval of it cannot be remembered.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public WorkflowRunner(
        WorkflowCatalog catalog,
        IWorkflowCheckpointStore checkpointStore,
        IOptions<TraconWorkflowOptions> options,
        IOptions<TraconOptions> traconOptions,
        ILogger<WorkflowRunner> logger,
        RunRecordingServices services,
        ParameterizedAgentFactory? parameterFactory = null,
        IWorkflowResponseStore? responseStore = null,
        IWorkflowRequestStore? requestStore = null,
        WorkflowAgentCache? agentCache = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(traconOptions);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(services);

        _catalog = catalog;
        _services = services;
        _checkpointStore = checkpointStore;
        _options = options;
        _traconOptions = traconOptions;
        _logger = logger;
        _errors = new WorkflowRunErrors(logger, services.ErrorClassifier);
        _reader = new WorkflowRequestReader(services.RunStore, services.TenantContext, requestStore, logger);
        _binder = new WorkflowParameterBinder(parameterFactory, services, traconOptions, logger);
        _planner = new WorkflowResumePlanner(_reader, catalog, checkpointStore, services.RunInputStore, services.TenantContext);
        _concurrency = new SlotPool(Math.Max(options.Value.MaxConcurrentRuns, 1));
        _tenantSlots = options.Value.MaxConcurrentRunsPerTenant is { } perTenant ? new TenantSlotLimiter(perTenant) : null;
        _responses = responseStore;
        _requests = requestStore;
        _agentCache = agentCache;
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
    public ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
        => _reader.ListPendingRequestsAsync(runId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListRequestsAsync(
        WorkflowRequestQuery query,
        CancellationToken cancellationToken = default)
        => _reader.ListRequestsAsync(query, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> RespondStreamingAsync(
        WorkflowRespondRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var execution = await _planner.PrepareResponseAsync(request, cancellationToken).ConfigureAwait(false);

        await foreach (var runEvent in ExecuteAsync(execution, cancellationToken).ConfigureAwait(false))
        {
            yield return runEvent;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The request is read as the waiting run recorded it, in its
    /// <see cref="RunEventType.WorkflowRequest"/> event: the checkpoint is not opened and
    /// no graph is built. <see cref="WorkflowResponseFactory.Check"/> applies the rule
    /// <see cref="WorkflowResponseFactory.Create"/> applies to the republished request.
    /// </remarks>
    public async ValueTask<string?> ValidateResponseAsync(
        WorkflowRespondRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return null;
        }

        var record = await _services.RunStore.GetRunAsync(request.RunId, cancellationToken).ConfigureAwait(false);

        // Missing, another tenant's, not a workflow, not waiting: RespondStreamingAsync
        // answers each with the message it always gave. There is no request to judge.
        if (record is null ||
            !string.Equals(record.TenantId, _services.TenantContext.TenantId, StringComparison.Ordinal) ||
            record.Kind != RunKind.Workflow ||
            record.Status != RunStatus.AwaitingInput)
        {
            return null;
        }

        await foreach (var runEvent in _services.RunStore
                           .ReadEventsAsync(request.RunId, 0, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (runEvent.Type == RunEventType.WorkflowRequest &&
                WorkflowRequestDescriptor.ReadRecorded(runEvent) is { } recorded &&
                string.Equals(recorded.RequestId, request.RequestId, StringComparison.Ordinal))
            {
                return WorkflowResponseFactory.Check(
                    recorded,
                    new WorkflowAnswer(request.RequestId, request.Approved, request.Text, request.Json, request.Remember));
            }
        }

        return null;
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
            Parameters = request.Parameters,
            DefinitionVersion = request.DefinitionVersion,

            // A queued run, or a recovery's continuation that starts the work again, arrives under an id whose row
            // is already open and may already name the run it continues.
            RowMayBeOpen = request.RunId is not null,
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

        var execution = await _planner.PrepareResumeAsync(request, cancellationToken).ConfigureAwait(false);

        await foreach (var runEvent in ExecuteAsync(execution, cancellationToken).ConfigureAwait(false))
        {
            yield return runEvent;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A run still in flight keeps its slot and gives it back when it ends: the pool disposes its semaphore with the
    /// last slot. A semaphore disposed here made that give-back throw from the <c>finally</c> that also closes the
    /// run, and the run was left at <c>Running</c>.
    /// </remarks>
    public void Dispose() => _concurrency.Dispose();

    /// <summary>
    /// Reads what the runs this execution continues already spent, and the run its row must name as the one it continues.
    /// </summary>
    /// <param name="execution">The execution.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The carry-over for the budget (empty for a new run), and the run to record as the one this run
    /// continues (<see langword="null"/> when the row names it already, or when this run continues none).
    /// </returns>
    /// <remarks>
    /// <para>
    /// A row opened before the run starts names what it continues, and that name is the more exact one: a
    /// recovery opens its continuation with a link to the interrupted run, while the checkpoint it resumes from
    /// may belong to an earlier run of the chain. The chain is read from the row's link first, and a link
    /// the row has is never overwritten. A resume whose row names nothing records its source.
    /// </para>
    /// <para>
    /// A new run costs nothing here: it has no source and no row.
    /// </para>
    /// </remarks>
    private async ValueTask<(BudgetCarryOver CarryOver, Guid? Continues)> ReadContinuationAsync(
        WorkflowExecution execution,
        CancellationToken cancellationToken)
    {
        if (execution.SourceRunId is null && !execution.RowMayBeOpen)
        {
            return (BudgetCarryOver.None, null);
        }

        var opened = execution.RowMayBeOpen
            ? await _services.RunStore.GetRunAsync(execution.RunId, cancellationToken).ConfigureAwait(false)
            : null;

        var continuesFrom = opened?.ContinuedFromRunId ?? execution.SourceRunId;
        var records = execution.RecordsContinuation && opened?.ContinuedFromRunId is null ? execution.SourceRunId : null;

        if (continuesFrom is not { } source)
        {
            return (BudgetCarryOver.None, records);
        }

        var carryOver = await BudgetLineage
            .ReadAsync(_services.RunStore, source, _services.TenantContext.TenantId, _traconOptions.Value.Pricing.Currency, _services.TimeProvider, _logger, cancellationToken)
            .ConfigureAwait(false);

        return (carryOver, records);
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
        // Armed with the installation's value now, when the run starts; a definition
        // that sets its own re-arms the same source from this moment once it
        // is read, so the deadline still counts from the start of the run.
        using var timeout = new CancellationTokenSource(settings.RunTimeout, _services.TimeProvider);
        var deadline = new RunDeadline(timeout, _services.TimeProvider.GetTimestamp(), settings.RunTimeout, cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        // The workflow row is the root of its own tree (RunId == RootRunId): an
        // external cancellation (POST /api/runs/{id}/cancel) triggers this source,
        // and the agent runs below the same RootRunId are canceled too.
        using var cancellationRegistration = _services.CancellationRegistry?.Register(
            execution.RunId,
            execution.RunId,
            _services.TenantContext.TenantId,
            linked);

        // 🚨 The concurrency slot is taken INSIDE RunGuardedAsync, after the run
        // row is open, not here. Waiting here made the slot wait the one stretch
        // of a run with no row: a run whose deadline passed while it queued
        // ended with an OperationCanceledException before its first event, the
        // stream closed without a closing frame, and the run id the endpoint had
        // already announced pointed at nothing.
        await foreach (var runEvent in RunGuardedAsync(execution, settings, deadline, linked).ConfigureAwait(false))
        {
            yield return runEvent;
        }
    }

    private async IAsyncEnumerable<RunEvent> RunGuardedAsync(
        WorkflowExecution execution,
        TraconWorkflowOptions settings,
        RunDeadline deadline,
        CancellationTokenSource linked)
    {
        var writer = _services.CreateWriter(execution.RunId, _logger);

        // 🚨 The root span is started IN THE BODY OF THIS METHOD. Activity.Current
        // is an AsyncLocal, and an assignment made inside a helper method does not
        // flow back to the caller; if the span were started there, the spans of the
        // agents would be siblings of the root span instead of its children
        var activity = ActivitySource.StartActivity(TraconDiagnostics.RunActivityName, ActivityKind.Internal);

        // Read BEFORE the scope is built. The scope promises the same
        // user and labels the run record carries (AgentRunScope.UserId/Labels),
        // and every node and tool of the root reads them from it; built first,
        // it carried neither. The read writes no AsyncLocal.
        var attribution = RunAttributionReader.Read(
            _services.AttributionContext,
            (message, exception) => _logger.LogWarning(
                exception,
                "{Message} Workflow run {RunId} continues and is recorded with no user and no labels.",
                message,
                execution.RunId));

        // The definition is read ONCE, here, BEFORE the row opens. The row stamps the
        // version it was compiled from (runs.agent_version), and the graph, the parameter schema
        // and the run timeout below all come from this same read - a stamp and a graph taken from
        // two reads could name two versions of a workflow being edited. A failure of the read is
        // held, not thrown: the row must open first so the run id the endpoint announced points at
        // a run, and the failure is raised inside the setup block below, which closes it.
        WorkflowCatalog.FoundWorkflow? found = null;
        ExceptionDispatchInfo? lookupFailure = null;

        try
        {
            found = await _catalog.FindAsync(execution.WorkflowName, execution.DefinitionVersion, linked.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lookupFailure = ExceptionDispatchInfo.Capture(exception);
        }

        // How long a request this run publishes may wait. Taken from the SAME read as the
        // graph, so a run compiled from a version carries that version's limit; the row it writes
        // freezes the time, and an edit made while the run waits does not move it.
        var requestTimeout = found?.Code?.RequestTimeout ?? found?.Definition?.RequestTimeout;

        // The ceiling is the logical run's. A run that continues an earlier one (a person's answer, an
        // operator's resume, a recovery) starts from what the chain already spent. Read before the budget is
        // built and held like the definition's read: the row must open first, and a chain that cannot be read
        // fails the run in the setup block below, not with a budget of zero.
        var carryOver = BudgetCarryOver.None;
        Guid? continues = null;
        ExceptionDispatchInfo? lineageFailure = null;

        try
        {
            (carryOver, continues) = await ReadContinuationAsync(execution, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, linked.Token))
        {
            lineageFailure = ExceptionDispatchInfo.Capture(exception);
        }
        catch (OperationCanceledException)
        {
            // Stopped before the row opened: the run opens, then closes Canceled in the setup block below.
        }

        var budget = _traconOptions.Value.AgentGraph.CreateBudget(_services.TimeProvider, carryOver);

        // The workflow names the counter its steps add to. An agent step is a child
        // run of this root and writes its own tokens and cost when it ends, even one that a
        // step left running in the background past the workflow's last event.
        budget.Account = new TreeAccount(_services.TenantContext.TenantId, execution.WorkflowName, attribution.UserId, _services.QuotaEnforcer);

        var scope = new AgentRunScope
        {
            RunId = execution.RunId,
            RootRunId = execution.RunId,
            Depth = 0,
            AgentName = execution.WorkflowName,
            TenantId = _services.TenantContext.TenantId,
            SessionId = execution.SessionId,

            // A workflow's session id never gets a sessions record; everything below inherits that.
            SessionHasNoRecord = true,
            UserId = attribution.UserId,
            Labels = attribution.Labels,
            Budget = budget,
            Writer = writer,

            // The handoff count of this run, from the SAME read of the definition the graph is
            // compiled from. The scope is the run's own object, so every step adds to one counter; a resume
            // or an answer is a new run and builds its own.
            Handoffs = found?.Definition is { Kind: WorkflowKind.Handoff } handoffDefinition
                ? HandoffLimit.For(handoffDefinition)
                : null,

            // What the function nodes of this workflow report beyond tokens adds up
            // here; the agent steps below keep their own (a tool's measurement) and
            // reach the same budget.
            NonTokenCost = new NonTokenCostAccumulator(_traconOptions.Value.Pricing.Currency, budget),
        };

        if (activity is not null)
        {
            activity.SetTag(TraconDiagnostics.Tags.RunId, execution.RunId);
            activity.SetTag(TraconDiagnostics.Tags.AgentName, execution.WorkflowName);
            activity.SetTag(TraconDiagnostics.Tags.TenantId, scope.TenantId);
            activity.SetTag(TraconDiagnostics.Tags.SessionId, execution.SessionId);
            activity.SetTag(TraconDiagnostics.Tags.Streaming, true);
            _services.TraceCollector?.BeginRun(activity.TraceId.ToString(), activity.SpanId.ToString());
        }

        TraconRunContext.SetCurrent(scope);

        // A read, not a reservation. The slot is taken after setup; if one is free now the row opens
        // Running as it always did, and a race with another run only shows a run as Queued (or Running) for
        // a moment longer than it was.
        //
        // The tenant is read ONCE, here: the ambient tenant is an AsyncLocal, and this iterator resumes on
        // whatever flow its consumer moves it with, so a later read could name another tenant's slot.
        var tenantId = _services.TenantContext.TenantId;
        var opensQueued = _concurrency.IsFull || _tenantSlots?.IsFull(tenantId) == true;

        var startInfo = new RunStartInfo
        {
            RunId = execution.RunId,
            AgentName = execution.WorkflowName,
            Kind = RunKind.Workflow,
            WorkflowName = execution.WorkflowName,
            StartedAt = _services.TimeProvider.GetUtcNow(),
            TenantId = scope.TenantId,

            // A workflow is ONE run for quota purposes, so it is one
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

            // The version the run is compiled from. A workflow defined in code has no
            // versions, and a run whose definition could not be read stamps none.
            AgentVersion = found?.Definition?.Version,

            // A resume points back at the run it resumes, so the chain a later continuation reads
            // its budget back along is whole. Null when the row was opened with a link already.
            ContinuedFromRunId = continues,

            // A run that would wait for a slot is Queued while it waits and Running once it has
            // one, so what the list shows is what the run is doing.
            Status = opensQueued ? RunStatus.Queued : RunStatus.Running,
        };

        var startedAt = _services.TimeProvider.GetTimestamp();
        var status = RunStatus.Completed;
        RunError? error = null;

        // 🚨 The run row is OPEN from writer.StartAsync below until
        // CompleteAsync, and this method is an async iterator: every
        // line between those two only runs WHILE SOMEBODY IS STILL
        // ENUMERATING. A consumer that breaks out of `await foreach`, or an
        // exception unwinding through a `yield return`, disposes the
        // enumerator and resumes the body only to run its `finally` blocks.
        // Without this one the run was simply left at Running: measured
        // 2026-09-20 on windows-latest, where the sibling race
        // `Cancellation_requested_inside_a_function_node_still_records_Canceled`
        // went red on two tag runs while passing 40/40 locally.
        //
        // RunReconciliationService would eventually claim the orphan, but it
        // closes orphans as Failed - a run the caller walked away from would
        // be reported, minutes later, as a failure that never happened.
        var closed = false;
        var holdsSlot = false;
        IDisposable? tenantSlot = null;

        try
        {
            // The row opens INSIDE the try. A stop that arrives while the row is being opened (the caller leaves,
            // an operator cancels, the deadline passes) ends the write with a cancellation after the store took
            // the row and before the first event; outside the try that left a row at Running and a span open
            // with nothing to close them, and the closing write below only ran for a run that got further.
            await writer.StartAsync(
                startInfo,

                // Workflow runs start with a structured input, not a single user
                // message; the eval case promotion covers only agent
                // runs.
                query: null,
                linked.Token).ConfigureAwait(false);

            // 🚨 A Queued row is closed by reconciliation only when it has a heartbeat (IRunStore
            // .ClaimOrphanedRunsAsync): the job queue owns every other Queued row. This one is owned by this
            // process, so it says so now and does not wait for the first heartbeat cycle; a process that stopped
            // in between would leave the row Queued for good.
            //
            // Only while run reconciliation is on: with it off nothing writes a heartbeat and nothing reads one, and a
            // host that turned it off does not get a write it never asked for. A runner built with no options (by hand)
            // writes it.
            if (opensQueued && (_services.Reconciliation?.Enabled ?? true))
            {
                await writer.TouchHeartbeatAsync(CancellationToken.None).ConfigureAwait(false);
            }

            yield return FirstEvent(execution);

            // A run that answers a request names, in its own history, the run and
            // the request it answers - the only link back, since the answer starts a new row
            // Written on None like the closing event: the link must not be lost to a
            // stop that comes before the graph does anything.
            if (execution.AnsweredRequest is { } answered)
            {
                yield return await writer.AppendAsync(
                        new RunEventDraft(RunEventType.WorkflowResponseApplied)
                        {
                            Text = answered.RequestId,
                            Payload = WorkflowResponseLink.Write(answered.SourceRunId, answered.RequestId),
                        },
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            // Graph setup is tried separately: an error here (agent not found,
            // invalid definition) is one the user can fix, and it must not leave
            // the run open.
            //
            // Set up BEFORE the slot wait, not after it. The definition
            // carries its own run timeout, and the deadline counts the slot wait;
            // read after the wait, a workflow allowed an hour timed out in the
            // queue on the installation's ten minutes. Setup spends no model call,
            // so it needs no slot.
            Workflow? workflow = null;

            try
            {
                lookupFailure?.Throw();
                lineageFailure?.Throw();

                var resolved = found
                    ?? throw new TraconException(
                        $"There is no workflow named '{execution.WorkflowName}'.");

                var graph = await _catalog.CompileAsync(resolved, linked.Token).ConfigureAwait(false);

                await ApplyRunTimeoutAsync(execution, resolved, settings, deadline).ConfigureAwait(false);

                // Values are checked against the schema of the SAME
                // definition the graph was built from, before any model
                // call. A failure here closes the run like a graph-setup
                // failure: the caller can fix it and nothing was spent.
                scope = await _binder.BindAsync(execution, resolved, scope, linked.Token)
                    .ConfigureAwait(false);

                workflow = graph;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                // Stopped during setup: closed below like any other stop, with its reason.
                status = RunStatus.Canceled;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, linked.Token))
            {
                status = RunStatus.Failed;
                error = _errors.ToRunError(exception, execution.RunId);
            }

            // The wait for a concurrency slot belongs to the run: the deadline
            // already counts it, so a run that times out here closes like any
            // other timed-out run — Canceled, naming the setting.
            //
            // The tenant's slot comes FIRST, the shared one second, and they are given back
            // the other way round. A run held back by its tenant's limit then waits holding no shared
            // slot, so it delays nobody else; taken the other way round it would sit on a shared slot
            // that another tenant's run could use. Both are released in the finally below.
            if (workflow is not null)
            {
                try
                {
                    if (_tenantSlots is not null)
                    {
                        tenantSlot = await _tenantSlots.AcquireAsync(tenantId, linked.Token).ConfigureAwait(false);
                    }

                    await _concurrency.AcquireAsync(linked.Token).ConfigureAwait(false);
                    holdsSlot = true;
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    status = RunStatus.Canceled;
                }
            }

            if (workflow is not null && holdsSlot)
            {
                // The run has what it waited for: a row opened Queued becomes Running now, on the token that
                // cannot be canceled, because the wait is over and the next write is the run's own.
                if (opensQueued)
                {
                    await writer.PromoteAsync(startInfo with { Status = RunStatus.Running }, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                await foreach (var produced in PumpAsync(
                                       workflow,
                                       execution,
                                       settings,
                                       scope,
                                       writer,
                                       _services.Recording,
                                       linked,
                                       activity)
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
                // returns false. Measured on a real sequential graph (2026-08-18):
                // the caller cancels, the graph stops after the
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
            }

            // 🚨 The deadline is the one stop nobody asked for, so it is the one
            // that has to name itself. A caller who cancels already knows why
            // and gets no reason; a run that ran past its run timeout gets the
            // setting's name, on every path that ends it - setup, the slot wait,
            // the graph. Only the timeout source is consulted, never the linked
            // one: the linked source is also tripped by the caller and by
            // POST /api/runs/{id}/cancel, and blaming the deadline for those
            // would put a wrong reason on a correct stop.
            if (status == RunStatus.Canceled && error is null)
            {
                error = StopReason(deadline);
            }

            await CompleteAsync(execution, scope, writer, status, error, activity, startedAt, requestTimeout)
                .ConfigureAwait(false);

            closed = true;

            // The client must also see the closing event: if the stream cuts off
            // before saying "done", the client cannot tell a dropped connection
            // from a finished job.
            yield return LastEvent(execution, writer, status, error);
        }
        finally
        {
            if (holdsSlot)
            {
                _concurrency.Release();
            }

            tenantSlot?.Dispose();

            // Reached only when the body did not close the run itself. The
            // status is Canceled rather than Failed because nothing failed:
            // either the caller cancelled, or it stopped reading, and both
            // are "somebody stopped this". CompleteAsync passes
            // CancellationToken.None all the way down, so the closing write
            // still lands on an already-cancelled token.
            if (!closed)
            {
                await CompleteAsync(execution, scope, writer, RunStatus.Canceled, error ?? StopReason(deadline), activity, startedAt, requestTimeout)
                    .ConfigureAwait(false);
            }
        }
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
        CancellationTokenSource linked,
        Activity? runActivity)
    {
        // 🚨 The run's root span is made current BEFORE execution starts, for the same reason as the scope below:
        // the task the framework starts captures the ambient Activity too. The assignment made in the body of
        // RunGuardedAsync does not survive the first `yield return` (the next MoveNextAsync runs in the consumer's
        // context), so without it every span of every executor and every agent step was a root of its own trace and
        // the collector, which finds the owning run by walking the parent chain, gave it to no run (measured in
        // A stored workflow's run trace held `tracon.run` and nothing else).
        if (runActivity is not null)
        {
            Activity.Current = runActivity;
        }

        // 🚨 The scope is written BEFORE execution starts; the assignment
        // inside the loop is not enough. Measured:
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
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            startupFailure = PumpedEvent.FromCancellation();
        }
        catch (Exception exception)
        {
            startupFailure = PumpedEvent.FromFailure(_errors.ToRunError(exception, execution.RunId));
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

            // The failure an executor event reports is logged
            // ONCE, under one ref, and the same ref names it on the event and
            // on the run row - MAF can report one exception as an executor
            // failure, a graph error and a thrown step.
            (Exception Exception, string CorrelationId)? loggedFailure = null;

            // 🚨 The stream must be reopened AFTER the response is sent.
            // Measured: SendResponseAsync queues a message, but the
            // WatchStreamAsync enumerator being consumed at that moment has
            // already decided to finish; only a new enumerator sees the
            // continuing super-steps.
            var reopened = false;

            while (true)
            {
                var responded = false;

                // Whether this enumerator read any event AFTER the last answer it delivered. See the reopen decision below.
                var readSinceResponse = false;

                // The enumerator's own token: cancelled by the run's, and by StalledStreamGuard when a reopened stream
                // has nothing left to wait for.
                using var streamSource = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                var enumerator = run
                    .WatchStreamAsync(blockOnPendingRequest: false, streamSource.Token)
                    .GetAsyncEnumerator(streamSource.Token);

                try
                {
                    while (true)
                    {
                        // 🚨 MAF does NOT honor an external cancellation token in
                        // the middle of a running step. Measured (2026-08-18)
                        // on a real sequential graph: the caller
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
                            await CancelAsync(run, linked.Token).ConfigureAwait(false);

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

                            // The step is asked for here, in this body, as before; only the wait for it is guarded.
                            var moving = enumerator.MoveNextAsync().AsTask();

                            if (reopened
                                ? await StalledStreamGuard.MoveNextAsync(
                                    moving,
                                    () => run.GetStatusAsync(CancellationToken.None),
                                    streamSource,
                                    StalledStreamGuard.Grace,
                                    _services.TimeProvider,
                                    linked.Token).ConfigureAwait(false)
                                : await moving.ConfigureAwait(false))
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
                            stepFailure = PumpedEvent.FromFailure(_errors.ToRunError(exception, execution.RunId, loggedFailure));
                        }

                        if (stepFailure is { } failed)
                        {
                            yield return failed;
                            yield break;
                        }

                        if (workflowEvent is null)
                        {
                            // A reopened stream the guard ended: the framework's run loop was waiting for input and the
                            // stream for a halt signal it had already given to the first stream (see StalledStreamGuard).
                            if (reopened && streamSource.IsCancellationRequested && !linked.IsCancellationRequested
                                && _logger.IsEnabled(LogLevel.Debug))
                            {
                                _logger.LogDebug(
                                    "Workflow run {RunId}: a reopened event stream stayed silent for {Grace} while the run was halted; the wait was ended.",
                                    execution.RunId,
                                    StalledStreamGuard.Grace);
                            }

                            break;
                        }

                        readSinceResponse = true;

                        if (workflowEvent is SuperStepStartedEvent && ++superSteps > settings.MaxSuperSteps)
                        {
                            await CancelAsync(run, linked.Token).ConfigureAwait(false);

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
                                readSinceResponse = false;

                                await RecordDeliveryAsync(execution).ConfigureAwait(false);

                                continue;
                            }
                        }

                        string? failureText = null;

                        if (WorkflowRunErrors.FailureOf(workflowEvent) is { } reported)
                        {
                            loggedFailure = _errors.LogFailure(reported, execution.RunId, workflowEvent, loggedFailure);
                            failureText = SafeErrorText.ForPersistence(loggedFailure.Value.Exception, loggedFailure.Value.CorrelationId);
                        }

                        var asked = await DescribeApprovalAsync(workflowEvent, linked.Token).ConfigureAwait(false);
                        var mapping = WorkflowEventMapper.Map(
                            workflowEvent, failureText, recording.MaxPayloadLength, asked.Presentation, asked.AgentName);

                        // An unrecognized event is in the run record now
                        // (tracon.workflow.event), so the log drops to Debug: it is no
                        // longer the only trace of it.
                        if (!mapping.IsKnown && _logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug(
                                "Unrecognized workflow event '{EventType}' was recorded in run {RunId}. " +
                                "A code workflow raised its own event, or Microsoft Agent Framework added a new event type.",
                                workflowEvent.GetType().Name,
                                execution.RunId);
                        }

                        if (mapping.Draft is not { } draft)
                        {
                            continue;
                        }

                        if (draft.Type == RunEventType.MessageDelta && !recording.RecordMessageDeltas)
                        {
                            continue;
                        }

                        RunEvent? appended = null;

                        try
                        {
                            // The reserved path only for the event no recognized type
                            // covers: the public AppendAsync refuses a `tracon.` type.
                            appended = mapping.IsKnown
                                ? await writer.AppendAsync(draft, linked.Token).ConfigureAwait(false)
                                : await writer.AppendReservedAsync(draft, linked.Token).ConfigureAwait(false);
                        }
                        // The deadline or the caller can stop the run between
                        // reading this event and writing it. That is the run stopping, as at
                        // a step boundary above, not an error for the caller to catch: the
                        // exception used to leave the stream, which ended without its closing
                        // event (measured on CI, the run timeout test, one run in hundreds).
                        catch (OperationCanceledException) when (linked.IsCancellationRequested)
                        {
                        }

                        if (appended is null)
                        {
                            await CancelAsync(run, linked.Token).ConfigureAwait(false);

                            yield return PumpedEvent.FromCancellation();

                            yield break;
                        }

                        yield return PumpedEvent.FromEvent(appended);

                        // 🚨 A graph-level error FAILS the run. Measured
                        // When it was only written to the event
                        // stream without changing the status, an executor had
                        // crashed, no output was ever produced, and the run
                        // was still recorded as "Completed" - a row that
                        // looked green in the list but had no result.
                        if (workflowEvent is WorkflowErrorEvent graphError)
                        {
                            yield return PumpedEvent.FromFailure(_errors.ToRunError(graphError, execution.RunId, loggedFailure));
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
                // has work left. Measured (2026-08-14): a Magentic orchestration
                // that hits its round limit
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

                // 🚨 An IDLE status is not proof that nothing is left to read. Measured (2026-11-02, with
                // Microsoft Agent Framework 1.26.0 and no Tracon code at all): an enumerator that ends right
                // after the answer was sent has read nothing since, because it ended at the halt that was
                // written BEFORE the answer. The work the answer set going then runs on the framework's own
                // loop and, when it is short, is over before the status is read here, so the status is Idle.
                // The earlier rule broke out on Idle and left the output event unread in the framework's
                // channel: the run was recorded as completed with no output (1 in 1 700 runs of the
                // functional tests; a one-millisecond await between the send and this read lost 98 % of
                // them). A reopened enumerator drains that channel, so the output comes back.
                //
                // Idle after events WERE read since the answer is the end of the work the enumerator watched,
                // so that case still stops, and so does a run the framework reports as ended (the Magentic
                // case above). Pending requests and a running graph reopen as before.
                var reopen = statusBeforeReopen is Microsoft.Agents.AI.Workflows.RunStatus.PendingRequests
                        or Microsoft.Agents.AI.Workflows.RunStatus.Running
                    || (statusBeforeReopen is Microsoft.Agents.AI.Workflows.RunStatus.Idle && !readSinceResponse);

                if (!reopen)
                {
                    break;
                }

                reopened = true;
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
    /// Names the agent behind a tool approval request and asks the registered
    /// <see cref="IToolApprovalPresenter"/> how to show it.
    /// </summary>
    /// <param name="workflowEvent">The event about to be recorded.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The agent and the presentation; both <see langword="null"/> for any other event. A presenter decorates and
    /// never blocks (<see cref="ToolApprovalPresenterRunner"/>), and an agent whose wrapper is not known stays unnamed.
    /// </returns>
    private async ValueTask<(string? AgentName, ToolApprovalPresentation? Presentation)> DescribeApprovalAsync(
        WorkflowEvent workflowEvent,
        CancellationToken cancellationToken)
    {
        if (workflowEvent is not RequestInfoEvent { Request: var request } ||
            !WorkflowRequestDescriptor.IsToolApproval(request) ||
            !request.TryGetDataAs<ToolApprovalRequestContent>(out var approval))
        {
            return (null, null);
        }

        // The id carries the name only as the framework rewrote it; the wrapper knows the real one.
        var agentName = WorkflowRequestDescriptor.ExecutorIdentityOf(request.PortInfo.PortId) is { } identity
            ? _agentCache?.NameOf(identity)
            : null;

        if (_services.ApprovalPresenterRunner is null)
        {
            return (agentName, null);
        }

        var presentations = await _services.ApprovalPresenterRunner
            .ResolveAllAsync([approval], _services.TenantContext.TenantId, agentName, cancellationToken)
            .ConfigureAwait(false);

        return (agentName, presentations.GetValueOrDefault(approval.RequestId));
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

    /// <summary>
    /// Records that this run handed its answer to the workflow, so the same answer is not
    /// given again for a run that goes on to fail.
    /// </summary>
    /// <param name="execution">The run that delivered the answer.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// Best-effort, like every write of the run's own record: a failure is logged and the run
    /// goes on. A lost write leaves the record <see cref="WorkflowResponseStatus.Accepted"/>, and
    /// the same answer can then start the work again if this run fails - at least once, the
    /// guarantee a resume already gives. A run with no record (a recovery continuation, a caller
    /// of the runner that skipped the HTTP layer) changes nothing.
    /// </remarks>
    private async ValueTask RecordDeliveryAsync(WorkflowExecution execution)
    {
        if (_responses is null || execution.AnsweredRequest is null)
        {
            return;
        }

        try
        {
            await _responses.MarkDeliveredAsync(execution.RunId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            _logger.LogWarning(
                exception,
                "Workflow run {RunId} delivered its answer, but the delivery could not be recorded. The run " +
                "continues; if it fails, the same answer can start the work again.",
                execution.RunId);
        }
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
            ? CheckpointManager.CreateJson(new TraconCheckpointStore(_checkpointStore, _services.TenantContext), null)
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
            var recorded = await _planner.FindCheckpointMetadataAsync(execution.SessionId, checkpointId, cancellationToken)
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
                // not say what to do about it. The cause is that executor ids
                // changed. A restart does not change them: WorkflowAgentIdentity
                // gives every agent step a permanent id. A change to the
                // graph does: the pattern, the steps, their order or the
                // node-list form, or a code-defined graph whose own
                // executors take a new id in each process.
                throw new TraconException(
                    $"Workflow '{execution.WorkflowName}' cannot be resumed from this checkpoint: " +
                    "the graph's structure differs from when the checkpoint was written. " +
                    "The workflow's pattern, its steps, their order or its node list changed, " +
                    "or a code-defined graph builds an executor whose id changes between processes. " +
                    "Start a new run.",
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

        // An EXTRA TurnToken must NOT be sent on a /respond call
        // (Answers non-empty): the checkpoint already republishes the pending
        // request BY ITSELF - the extra token
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
        long startedAt,
        TimeSpan? requestTimeout)
    {
        // 🚨 Classified HERE, where the row closes, so every error the runner
        // builds gets a class: a participant's exception, the deadline, the
        // super-step limit, an undeliverable answer. Before, none did,
        // and a workflow failure was invisible on the error-class dashboard
        // while its own participant's row carried the class.
        if (error is not null)
        {
            error = _errors.Classify(error);
        }

        // A workflow row has no model of its own: only the agent
        // runs beneath it carry token cost, which shows up in the tree total. What
        // its own function nodes reported beyond tokens is the row's own snapshot,
        // read once here; an item reported later stays in the run's detail.
        var nonTokenCost = scope.NonTokenCost?.Seal();

        await writer.CompleteAsync(
            status,
            usage: null,
            error,
            cost: null,
            nonTokenCost: nonTokenCost,
            modelId: null,
            modelProvider: null,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        await RecordQuotaAsync(execution, scope, nonTokenCost, CancellationToken.None).ConfigureAwait(false);

        // The requests the run waits on are read once: the inbox rows and the webhooks share it.
        var awaiting = status == RunStatus.AwaitingInput
            ? await ReadAwaitingRequestsAsync(execution).ConfigureAwait(false)
            : [];

        await RecordRequestsAsync(execution, scope, awaiting, requestTimeout).ConfigureAwait(false);

        await PublishWebhooksAsync(execution, scope, status, error, startedAt, nonTokenCost, awaiting).ConfigureAwait(false);

        // 🚨 The checkpoints of a run awaiting a human are NEVER deleted: the
        // response resumes from exactly those checkpoints. The cleanup
        // setting is only for runs that have genuinely finished.
        if (!_options.Value.KeepCheckpointsAfterCompletion &&
            status != RunStatus.AwaitingInput &&
            !IsLeftToItsJob(status) &&
            !await SessionHasOpenRunAsync(execution).ConfigureAwait(false))
        {
            await DiscardCheckpointsAsync(execution).ConfigureAwait(false);
        }

        var elapsed = _services.TimeProvider.GetElapsedTime(startedAt);

        _services.Metrics?.RecordRun(
            execution.WorkflowName,
            status,
            scope.TenantId ?? _services.TenantContext.TenantId,
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

        if (_services.TraceCollector is not null)
        {
            await _services.TraceCollector.CompleteRunAsync(
                activity.TraceId.ToString(),
                activity.SpanId.ToString(),
                execution.RunId,
                scope.TenantId ?? _services.TenantContext.TenantId,
                status,
                CancellationToken.None).ConfigureAwait(false);
        }

        activity.Dispose();
    }

    /// <summary>
    /// Reads the requests a run that just closed as <see cref="RunStatus.AwaitingInput"/> waits
    /// on. A failure is logged and answered with none: the run is already closed, and neither the
    /// inbox rows nor the webhooks may break it.
    /// </summary>
    private async ValueTask<IReadOnlyList<(WorkflowPendingRequest Request, long Sequence)>> ReadAwaitingRequestsAsync(
        WorkflowExecution execution)
    {
        try
        {
            return await _reader.ReadRequestEventsAsync(execution.RunId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            _logger.LogWarning(
                exception,
                "The requests workflow run {RunId} waits on could not be read; it writes no inbox row and publishes no request webhook.",
                execution.RunId);

            return [];
        }
    }

    /// <summary>
    /// Writes the inbox rows of the requests a run waits on, with the time each expires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one write point: the run has just closed as <see cref="RunStatus.AwaitingInput"/>, and
    /// every request it published is in its event stream. The time counts from the request's own
    /// event, so a continuation that republishes a request starts the count again.
    /// </para>
    /// <para>
    /// A row that cannot be written does not fail the run - the same rule as every other
    /// observation of a run. The request is still listed by <c>GET /api/workflows/runs/{runId}/requests</c>
    /// and can still be answered; it is missing from the tenant-wide list and it never expires, so
    /// retention keeps the rows of its run. That is logged as a warning.
    /// </para>
    /// </remarks>
    private async ValueTask RecordRequestsAsync(
        WorkflowExecution execution,
        AgentRunScope scope,
        IReadOnlyList<(WorkflowPendingRequest Request, long Sequence)> awaiting,
        TimeSpan? requestTimeout)
    {
        if (_requests is null || awaiting.Count == 0)
        {
            return;
        }

        var tenantId = scope.TenantId ?? _services.TenantContext.TenantId;

        try
        {
            var rows = new List<WorkflowRequestRecord>(awaiting.Count);

            foreach (var (request, sequence) in awaiting)
            {
                rows.Add(new WorkflowRequestRecord
                {
                    RunId = execution.RunId,
                    RequestId = request.RequestId,
                    SessionId = execution.SessionId,
                    WorkflowName = execution.WorkflowName,
                    PortId = request.PortId,
                    EventSequence = sequence,
                    RequestedAt = request.RequestedAt,
                    ExpiresAt = requestTimeout is { } timeout ? request.RequestedAt + timeout : null,
                    Status = WorkflowRequestStatus.Open,
                    TenantId = tenantId,
                });
            }

            await _requests.AddAsync(rows, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            _logger.LogWarning(
                exception,
                "The inbox rows of workflow run {RunId} could not be written. Its requests can still be answered from the run, but they are missing from GET /api/workflows/requests and never expire.",
                execution.RunId);
        }
    }

    /// <summary>
    /// Publishes the workflow root's webhooks: <c>run.completed</c> or
    /// <c>run.failed</c> when the run ends, and one
    /// <c>workflow.request.pending</c> per request the run waits on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The agent steps are child runs and publish nothing (only a root does).
    /// The run summary carries the tree's tokens and cost: the workflow row has
    /// none of its own.
    /// </para>
    /// <para>
    /// The request event carries the ids only, never the prompt: the payload is
    /// a summary that is also stored with the delivery; a receiver reads the
    /// request from <c>GET /api/workflows/runs/{id}/requests</c>. A failure here
    /// is logged and never fails the run (observability must not break
    /// functionality).
    /// </para>
    /// </remarks>
    // Before this a workflow produced no run.* event at all, and the
    // documented workflow.request.pending was never sent.
    private async ValueTask PublishWebhooksAsync(
        WorkflowExecution execution,
        AgentRunScope scope,
        RunStatus status,
        RunError? error,
        long startedAt,
        RunNonTokenCost? nonTokenCost,
        IReadOnlyList<(WorkflowPendingRequest Request, long Sequence)> awaiting)
    {
        if (_services.WebhookPublisher is null)
        {
            return;
        }

        var tenantId = scope.TenantId ?? _services.TenantContext.TenantId;

        try
        {
            if (status == RunStatus.AwaitingInput)
            {
                foreach (var (request, _) in awaiting)
                {
                    await _services.WebhookPublisher.PublishAsync(
                        tenantId,
                        WebhookEvents.WorkflowRequestPending,
                        new WebhookEventPayload
                        {
                            OccurredAt = _services.TimeProvider.GetUtcNow(),
                            Approval = new WebhookApprovalSummary
                            {
                                RequestId = request.RequestId,
                                RunId = execution.RunId.ToString(),
                                WorkflowName = execution.WorkflowName,
                            },
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }

                return;
            }

            if (WorkflowRunWebhooks.EventFor(status) is not { } eventType)
            {
                return;
            }

            var record = await _services.RunStore.GetRunAsync(execution.RunId, CancellationToken.None).ConfigureAwait(false);

            await _services.WebhookPublisher.PublishAsync(
                tenantId,
                eventType,
                WorkflowRunWebhooks.RunPayload(
                    _services.TimeProvider.GetUtcNow(),
                    execution.RunId,
                    execution.SessionId,
                    execution.WorkflowName,
                    status,
                    _services.TimeProvider.GetElapsedTime(startedAt),
                    record,
                    nonTokenCost,
                    error?.Message),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            _logger.LogWarning(exception, "The webhooks of workflow run {RunId} could not be published.", execution.RunId);
        }
    }

    /// <summary>Writes the workflow root's own consumption to the quota counters: one run, and what its function nodes reported beyond tokens.</summary>
    /// <remarks>
    /// <para>
    /// The workflow counts as a SINGLE "run" — not per step (the same "root vs. step"
    /// design decision as <see cref="RunRecordingAgent"/>'s root <c>Runs = 1</c>).
    /// </para>
    /// <para>
    /// It writes ONLY its own share. A workflow row has no <c>usage</c>/<c>cost</c>
    /// of its own (see the note on <see cref="CompleteAsync"/>); the agent runs beneath it
    /// are child runs and each writes its own tokens and cost under this workflow's name
    /// when it ends. This used to read the whole tree from the store after the row closed,
    /// and a step's background task that ended later was never in it.
    /// </para>
    /// </remarks>
    private async ValueTask RecordQuotaAsync(
        WorkflowExecution execution,
        AgentRunScope scope,
        RunNonTokenCost? nonTokenCost,
        CancellationToken cancellationToken)
    {
        if (scope.Budget?.Account is not { Quota: not null } account)
        {
            return;
        }

        // Only this root's own share is written here; its steps wrote theirs (this keeps
        // the workflow one run). The quota.threshold webhook names the run that crossed
        // the threshold and its user, as it does for an agent run. The token cost is the steps';
        // RunSpend adds nothing across currencies, so the root's amount is the one it reported.
        _ = await TreeQuota.RecordAsync(
            account,
            execution.RunId,
            runs: 1,
            tokens: 0,
            amount: RunSpend.Total((RunCost?)null, nonTokenCost),
            _services.TimeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether another run of the same session still needs the session's
    /// checkpoints: one waiting for a human, or one still running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The checkpoint store deletes per SESSION, and an answer run
    /// shares its source run's session. When the answer run finished — even
    /// as Failed, after a wrong answer — the cleanup deleted the source run's
    /// checkpoints, and the request it still listed as pending could never be
    /// answered again, although a waiting run's checkpoints are meant to be kept.
    /// </para>
    /// <para>
    /// A store that cannot answer counts as "yes": cleanup is optional, a
    /// deleted checkpoint is not.
    /// </para>
    /// <para>
    /// <see cref="RunStatus.Queued"/> counts as open too: a run that waits for a
    /// concurrency slot is <c>Queued</c> and was <c>Running</c> before, and an answer or a resume
    /// that is queued reads the session's checkpoints as soon as it starts.
    /// </para>
    /// </remarks>
    private static readonly RunStatus[] SessionOpenStatuses = [RunStatus.AwaitingInput, RunStatus.Running, RunStatus.Queued];

    /// <summary>
    /// Whether a run that closes now is not finished: a worker that is stopping
    /// cancelled it, or its job's lease went to another worker, and the job continues
    /// it from the latest checkpoint when it is leased again (or, in the second case,
    /// already has).
    /// </summary>
    /// <param name="status">The status the run closes with.</param>
    /// <returns><see langword="true"/> when the run's checkpoints must outlive its close.</returns>
    /// <remarks>
    /// With <see cref="TraconWorkflowOptions.KeepCheckpointsAfterCompletion"/> off,
    /// a graceful shutdown deleted the checkpoints of the run it stopped, and the
    /// continuation ran every finished step again. The continuation shares the session,
    /// so the cleanup runs when it finishes. A worker that lost its lease must not delete
    /// them either: the worker that took the job over may be reading them. A run its job
    /// cancelled or timed out is never continued and is cleaned up at once.
    /// </remarks>
    private bool IsLeftToItsJob(RunStatus status)
        => status == RunStatus.Canceled &&
           _options.Value.ResumeInterruptedRuns &&
           AmbientJobAttempt.Current is { Reason: JobStopReason.WorkerStopping or JobStopReason.LeaseLost };

    private async ValueTask<bool> SessionHasOpenRunAsync(WorkflowExecution execution)
    {
        try
        {
            foreach (var open in SessionOpenStatuses)
            {
                var runs = await _services.RunStore
                    .QueryRunsAsync(
                        new RunQuery
                        {
                            TenantId = _services.TenantContext.TenantId,
                            SessionId = execution.SessionId,
                            Status = open,
                            Take = 2,
                        },
                        CancellationToken.None)
                    .ConfigureAwait(false);

                if (runs.Any(run => run.Id != execution.RunId))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            // CancellationToken.None again, so no cancellation of ours.
            _logger.LogWarning(
                exception,
                "Could not check the session of run {RunId} for runs that still need its checkpoints; they are kept.",
                execution.RunId);

            return true;
        }
    }

    private async ValueTask DiscardCheckpointsAsync(WorkflowExecution execution)
    {
        try
        {
            await _checkpointStore
                .DeleteAsync(_services.TenantContext.TenantId, execution.SessionId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // No filter: the delete runs on CancellationToken.None, so no
            // OperationCanceledException here is a cancellation of ours.
            // Cleanup is not functional; its failure does not affect the run.
            _logger.LogWarning(
                exception,
                "Could not delete checkpoints for run {RunId}.",
                execution.RunId);
        }
    }

    private static async ValueTask CancelAsync(StreamingRun run, CancellationToken cancellationToken)
    {
        try
        {
            await run.CancelRunAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // The run's own token decides: after the caller cancelled,
            // the cancellation propagates as before; on the super-step limit
            // path nothing of ours is cancelled, so an OperationCanceledException
            // here is a failure and must not turn the limit into "Canceled".
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
            Timestamp = _services.TimeProvider.GetUtcNow(),
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
            Timestamp = _services.TimeProvider.GetUtcNow(),
            Text = error?.Message,
        };

    /// <summary>
    /// The reason a cancelled run carries when nobody asked for the stop: its own
    /// deadline, or the time limit of the job running it. <see langword="null"/> for a
    /// stop its caller asked for.
    /// </summary>
    /// <param name="deadline">The run's own deadline, and the token the run was started with.</param>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// Written once and read from the places that close a cancelled run. The status
    /// stays <see cref="RunStatus.Canceled"/> - a deadline stops a run, it does not
    /// fault one - so this text is the only thing that tells an operator a timeout
    /// apart from a cancellation somebody asked for.
    /// </para>
    /// <para>
    /// The job's time limit is read only when the run's own token was cancelled, and
    /// only from the job attempt the worker opened around its handler: a run that a
    /// job cancelled for another reason (its cancel, the worker stopping) gets no
    /// reason, as before. Its error type is what keeps recovery from continuing it.
    /// </para>
    /// </remarks>
    private static RunError? StopReason(RunDeadline deadline)
    {
        if (deadline.Fired)
        {
            return deadline.ToRunError();
        }

        // The job's time limit is a reason of its own, not an
        // interruption; WorkflowRecovery never continues a run closed with it.
        return deadline.CallerStopped &&
               AmbientJobAttempt.Current is { Reason: JobStopReason.MaxDurationExceeded } attempt
            ? attempt.ToRunError("Workflow run")
            : null;
    }

    /// <summary>
    /// Re-arms the run's deadline from the workflow's own run timeout, cut to the host's ceiling.
    /// </summary>
    /// <param name="execution">The run.</param>
    /// <param name="found">The workflow the graph came from: a stored definition, or a registration in code.</param>
    /// <param name="settings">The engine's settings.</param>
    /// <param name="deadline">The run's deadline.</param>
    /// <returns>A task that completes when the deadline is re-armed.</returns>
    /// <remarks>
    /// The save refuses a value above the ceiling, and so does the host's start for a workflow
    /// registered in code (<see cref="TraconWorkflowOptionsValidator"/>); a value that is above a
    /// ceiling lowered later still runs, cut to it, and the cut is logged.
    /// </remarks>
    private async ValueTask ApplyRunTimeoutAsync(
        WorkflowExecution execution,
        WorkflowCatalog.FoundWorkflow found,
        TraconWorkflowOptions settings,
        RunDeadline deadline)
    {
        if ((found.Definition?.RunTimeout ?? found.Code?.RunTimeout) is not { } requested)
        {
            return;
        }

        var ceiling = WorkflowDefinitionValidator.RunTimeoutCeiling(settings.RunTimeout, settings.MaxRunTimeout);
        var limit = requested;

        if (requested > ceiling)
        {
            _logger.LogWarning(
                "Workflow {Workflow} sets a run timeout of {Requested}, above the host's ceiling of {Ceiling}; run {RunId} is cut to the ceiling. {Fix}",
                execution.WorkflowName,
                requested,
                ceiling,
                execution.RunId,
                found.Code is null
                    ? "Save the workflow again with a value that fits."
                    : "Lower WorkflowRegistrationOptions.RunTimeout, or raise 'Tracon:Workflows:MaxRunTimeout'.");

            limit = ceiling;
        }

        await deadline.RearmAsync(limit, _services.TimeProvider, byRegistration: found.Code is not null).ConfigureAwait(false);
    }
}
