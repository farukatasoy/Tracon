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
    private readonly IRunErrorClassifier? _errorClassifier;
    private readonly IWebhookPublisher? _webhookPublisher;
    private readonly IRunAttributionContext? _attributionContext;
    private readonly IReadOnlyList<IRunEventSink> _sinks;
    private readonly IRunInputStore? _runInputs;
    private readonly ParameterizedAgentFactory? _parameterFactory;
    private readonly IWorkflowResponseStore? _responses;
    private readonly IWorkflowRequestStore? _requests;
    private readonly ToolApprovalPresenterRunner? _approvalPresenter;
    private readonly WorkflowAgentCache? _agentCache;

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
    /// <param name="errorClassifier">
    /// The error classifier. When <see langword="null"/>, a failed workflow row
    /// carries no class — the same rule the agent run path follows.
    /// </param>
    /// <param name="webhookPublisher">
    /// The webhook publisher. When <see langword="null"/>, the workflow root
    /// publishes no <c>run.*</c> and no <c>workflow.request.pending</c> event.
    /// </param>
    /// <param name="runInputStore">
    /// Where a parameterized run's final values are recorded. When <see langword="null"/>
    /// nothing is recorded, so a run with parameters cannot be resumed or answered.
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
    /// <param name="approvalPresenter">
    /// Asks the registered <see cref="IToolApprovalPresenter"/> how to show a tool approval request. When
    /// <see langword="null"/> a request names its tool and nothing more.
    /// </param>
    /// <param name="agentCache">
    /// The agent wrappers of the workflows, which name the agent behind an executor id. When <see langword="null"/> a
    /// tool approval request does not name the agent that asked, and an approval of it cannot be remembered.
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
        IEnumerable<IRunEventSink>? sinks = null,
        IRunErrorClassifier? errorClassifier = null,
        IWebhookPublisher? webhookPublisher = null,
        IRunInputStore? runInputStore = null,
        ParameterizedAgentFactory? parameterFactory = null,
        IWorkflowResponseStore? responseStore = null,
        IWorkflowRequestStore? requestStore = null,
        ToolApprovalPresenterRunner? approvalPresenter = null,
        WorkflowAgentCache? agentCache = null)
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
        _errorClassifier = errorClassifier;
        _webhookPublisher = webhookPublisher;
        _runInputs = runInputStore;
        _parameterFactory = parameterFactory;
        _responses = responseStore;
        _requests = requestStore;
        _approvalPresenter = approvalPresenter;
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
    public async ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireWorkflowRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // A run that was answered STAYS in AwaitingInput (status history is not
        // rewritten afterwards, K-014) and still lists its request; the
        // continuing work lives on a new run row. Answering it again opens a
        // branch, on purpose (K-130).
        if (record.Status != RunStatus.AwaitingInput)
        {
            return [];
        }

        var events = await ReadRequestEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        var rows = await TryReadRowsAsync(runId, cancellationToken).ConfigureAwait(false);
        var requests = new List<WorkflowPendingRequest>(events.Count);

        foreach (var (request, _) in events)
        {
            var row = rows.FirstOrDefault(candidate => string.Equals(candidate.RequestId, request.RequestId, StringComparison.Ordinal));

            requests.Add(Enrich(request, record.WorkflowName, record.SessionId, row));
        }

        return requests;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListRequestsAsync(
        WorkflowRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_requests is null)
        {
            return [];
        }

        var rows = await _requests.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var items = new List<WorkflowPendingRequest>(rows.Count);

        foreach (var row in rows)
        {
            items.Add(await ReadRequestOfRowAsync(row, cancellationToken).ConfigureAwait(false));
        }

        return items;
    }

    /// <summary>
    /// Reads the request a row names from its run's event, so the prompt and the form are the
    /// ones the event holds and never a copy.
    /// </summary>
    /// <param name="row">The inbox row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The request. When the event is gone or is not the request the row names, a request with
    /// the row's own facts only: it can still be answered, and the card of its run shows it.
    /// </returns>
    /// <remarks>
    /// The run closes right after it publishes its requests, so the events from the request's
    /// sequence on are few; reading stops at the first.
    /// </remarks>
    private async ValueTask<WorkflowPendingRequest> ReadRequestOfRowAsync(
        WorkflowRequestRecord row,
        CancellationToken cancellationToken)
    {
        await foreach (var runEvent in _runStore
                           .ReadEventsAsync(row.RunId, row.EventSequence, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (runEvent.Sequence == row.EventSequence &&
                runEvent.Type == RunEventType.WorkflowRequest &&
                WorkflowRequestDescriptor.Parse(runEvent) is { } parsed &&
                string.Equals(parsed.RequestId, row.RequestId, StringComparison.Ordinal))
            {
                return Enrich(parsed, row.WorkflowName, row.SessionId, row);
            }

            break;
        }

        return Enrich(
            new WorkflowPendingRequest
            {
                RunId = row.RunId,
                RequestId = row.RequestId,
                PortId = row.PortId,
                Form = WorkflowRequestForm.Json,
                RequestedAt = row.RequestedAt,
            },
            row.WorkflowName,
            row.SessionId,
            row);
    }

    /// <summary>Adds what the run and the inbox row know to a request read from the event.</summary>
    private static WorkflowPendingRequest Enrich(
        WorkflowPendingRequest request,
        string? workflowName,
        string? sessionId,
        WorkflowRequestRecord? row)
        => request with
        {
            WorkflowName = workflowName,
            SessionId = sessionId,
            ExpiresAt = row?.ExpiresAt,
            Answered = row?.AnsweredAt is not null,
            LastResponseRunId = row?.LastResponseRunId,
        };

    /// <summary>
    /// Reads the run's <see cref="RunEventType.WorkflowRequest"/> events as requests, with the
    /// sequence of the event each came from.
    /// </summary>
    private async ValueTask<IReadOnlyList<(WorkflowPendingRequest Request, long Sequence)>> ReadRequestEventsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var requests = new List<(WorkflowPendingRequest, long)>();

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
                requests.Add((request, runEvent.Sequence));
            }
        }

        return requests;
    }

    /// <summary>
    /// Reads a run's inbox rows for the facts a request carries beyond its event. A failure is
    /// logged and answered with no rows: answering a request must not depend on the inbox.
    /// </summary>
    private async ValueTask<IReadOnlyList<WorkflowRequestRecord>> TryReadRowsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        if (_requests is null)
        {
            return [];
        }

        try
        {
            return await _requests.ListByRunAsync(runId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            _logger.LogWarning(
                exception,
                "The inbox rows of workflow run {RunId} could not be read; its requests are listed without an expiry or an answer mark.",
                runId);

            return [];
        }
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

        var record = await _runStore.GetRunAsync(request.RunId, cancellationToken).ConfigureAwait(false);

        // Missing, another tenant's, not a workflow, not waiting: RespondStreamingAsync
        // answers each with the message it always gave. There is no request to judge.
        if (record is null ||
            !string.Equals(record.TenantId, _tenantContext.TenantId, StringComparison.Ordinal) ||
            record.Kind != RunKind.Workflow ||
            record.Status != RunStatus.AwaitingInput)
        {
            return null;
        }

        await foreach (var runEvent in _runStore
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

        // The run resumes with the definition it recorded, not the one the workflow has now;
        // an operator names another version to carry it over a fix.
        var definitionVersion = request.DefinitionVersion ?? record.AgentVersion;

        return new WorkflowExecution
        {
            WorkflowName = record.WorkflowName!,
            RunId = request.NewRunId ?? TraconId.NewId(),
            SessionId = record.SessionId!,
            Message = null,
            ResumeFrom = await RequireCheckpointAsync(request.RunId, request.CheckpointId, cancellationToken)
                .ConfigureAwait(false),
            Parameters = await ReadRecordedParametersAsync(record, "resumed", definitionVersion, cancellationToken)
                .ConfigureAwait(false),
            DefinitionVersion = definitionVersion,
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

        var definitionVersion = request.DefinitionVersion ?? record.AgentVersion;

        return new WorkflowExecution
        {
            WorkflowName = record.WorkflowName!,
            RunId = request.NewRunId ?? TraconId.NewId(),
            SessionId = record.SessionId!,
            Message = null,
            ResumeFrom = await RequireCheckpointAsync(request.RunId, request.CheckpointId, cancellationToken)
                .ConfigureAwait(false),
            Answers = [new WorkflowAnswer(request.RequestId, request.Approved, request.Text, request.Json, request.Remember)],
            AnsweredRequest = new AnsweredRequest(request.RunId, request.RequestId),
            Parameters = await ReadRecordedParametersAsync(record, "answered", definitionVersion, cancellationToken)
                .ConfigureAwait(false),
            DefinitionVersion = definitionVersion,
        };
    }

    /// <summary>
    /// Reads the parameter values a run was started with, for the run that
    /// continues it (a resume or a response).
    /// </summary>
    /// <param name="source">The run being continued.</param>
    /// <param name="verb">"resumed" or "answered", for the refusal text.</param>
    /// <param name="definitionVersion">
    /// The definition version the continuing run will be compiled from; its schema is the one
    /// the values are read against. <see langword="null"/> reads the current one.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The recorded values that the schema of that definition still declares;
    /// <see langword="null"/> when it takes no parameters.
    /// </returns>
    /// <exception cref="TraconException">
    /// The workflow declares parameters now but the source run recorded none, or
    /// the schema gained a required parameter the recorded values lack, or the workflow has
    /// no such version.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>Fail closed.</strong> A continued run that found no recorded values
    /// and ran on the defaults would change the instructions its agents were
    /// given halfway through the work. NULL ("nothing was recorded") is
    /// therefore refused whenever the workflow declares parameters now; a
    /// workflow that declares none keeps working exactly as before.
    /// </para>
    /// <para>
    /// A name the schema no longer declares is dropped rather than refused:
    /// removing a parameter is the operator's change to make, and a value no
    /// instruction can reach is harmless. A required name the recorded values
    /// lack is refused, because the run would otherwise start with an unbound
    /// placeholder.
    /// </para>
    /// </remarks>
    private async ValueTask<IReadOnlyDictionary<string, string>?> ReadRecordedParametersAsync(
        RunRecord source,
        string verb,
        int? definitionVersion,
        CancellationToken cancellationToken)
    {
        // The schema comes from the version the run continues with, never from a newer one: a
        // parameter added since the run started is not a parameter the run was given.
        var descriptor = await _catalog.GetAsync(source.WorkflowName!, definitionVersion, cancellationToken)
            .ConfigureAwait(false);
        var schema = descriptor?.Parameters ?? [];

        if (schema.Count == 0)
        {
            return null;
        }

        var recorded = _runInputs is null
            ? null
            : (await _runInputs.GetAsync(_tenantContext.TenantId, source.Id, cancellationToken).ConfigureAwait(false))
                ?.Parameters;

        var values = ParameterValueSet.Intersect(
            schema,
            recorded ?? throw new TraconException(
                $"Run '{source.Id}' cannot be {verb}: workflow '{source.WorkflowName}' declares parameters, and " +
                "this run did not record the values it started with. A run records them when it starts, " +
                "unless 'Tracon:RunRecording:RecordRunInput' is off, the run predates parameter recording, " +
                "or the run input store failed while saving them (the host log names the run); " +
                "continuing it on the defaults would change the instructions its agents were given."));
        var check = AgentParameterValidator.ValidateValues(schema, values);

        if (!check.IsValid)
        {
            throw new TraconException(
                $"Run '{source.Id}' cannot be {verb}: workflow '{source.WorkflowName}' changed its parameters " +
                $"after the run started. {ParameterValueSet.Describe(check)}");
        }

        return values;
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
        // Armed with the installation's value now, when the run starts; a definition
        // that sets its own (F-405) re-arms the same source from this moment once it
        // is read, so the deadline still counts from the start of the run.
        using var timeout = new CancellationTokenSource(settings.RunTimeout, _timeProvider);
        var deadline = new RunDeadline(timeout, _timeProvider.GetTimestamp(), settings.RunTimeout, cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        // The workflow row is the root of its own tree (RunId == RootRunId): an
        // external cancellation (POST /api/runs/{id}/cancel) triggers this source,
        // and the agent runs below the same RootRunId are canceled too.
        using var cancellationRegistration = _cancellationRegistry?.Register(
            execution.RunId,
            execution.RunId,
            _tenantContext.TenantId,
            linked);

        // 🚨 The concurrency slot is taken INSIDE RunGuardedAsync, after the run
        // row is open, not here. Waiting here made the slot wait the one stretch
        // of a run with no row: a run whose deadline passed while it queued
        // ended with an OperationCanceledException before its first event, the
        // stream closed without a closing frame, and the run id the endpoint had
        // already announced pointed at nothing (F-325).
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
        var recording = _traconOptions.Value.RunRecording;
        var writer = new RunEventWriter(_runStore, recording, _logger, execution.RunId, _metrics, _sinks);

        // 🚨 The root span is started IN THE BODY OF THIS METHOD. Activity.Current
        // is an AsyncLocal, and an assignment made inside a helper method does not
        // flow back to the caller; if the span were started there, the spans of the
        // agents would be siblings of the root span instead of its children
        // (measured in phase 6).
        var activity = ActivitySource.StartActivity(TraconDiagnostics.RunActivityName, ActivityKind.Internal);

        // F-337: read BEFORE the scope is built. The scope promises the same
        // user and labels the run record carries (AgentRunScope.UserId/Labels),
        // and every node and tool of the root reads them from it; built first,
        // it carried neither. The read writes no AsyncLocal.
        var attribution = RunAttributionReader.Read(
            _attributionContext,
            (message, exception) => _logger.LogWarning(
                exception,
                "{Message} Workflow run {RunId} continues and is recorded with no user and no labels.",
                message,
                execution.RunId));

        // Phase 200: the definition is read ONCE, here, BEFORE the row opens. The row stamps the
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

        // Phase 209: how long a request this run publishes may wait. Taken from the SAME read as the
        // graph, so a run compiled from a version carries that version's limit; the row it writes
        // freezes the time, and an edit made while the run waits does not move it.
        var requestTimeout = found?.Code?.RequestTimeout ?? found?.Definition?.RequestTimeout;

        var budget = _traconOptions.Value.AgentGraph.CreateBudget(_timeProvider);

        // F-418: the workflow names the counter its steps add to. An agent step is a child
        // run of this root and writes its own tokens and cost when it ends, even one that a
        // step left running in the background past the workflow's last event.
        budget.Account = new TreeAccount(_tenantContext.TenantId, execution.WorkflowName, attribution.UserId, _quotaEnforcer);

        var scope = new AgentRunScope
        {
            RunId = execution.RunId,
            RootRunId = execution.RunId,
            Depth = 0,
            AgentName = execution.WorkflowName,
            TenantId = _tenantContext.TenantId,
            SessionId = execution.SessionId,
            UserId = attribution.UserId,
            Labels = attribution.Labels,
            Budget = budget,
            Writer = writer,

            // What the function nodes of this workflow report beyond tokens adds up
            // here; the agent steps below keep their own (a tool's measurement) and
            // reach the same budget (phase 199).
            NonTokenCost = new NonTokenCostAccumulator(_traconOptions.Value.Pricing.Currency, budget),
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

                // The version the run is compiled from. A workflow defined in code has no
                // versions, and a run whose definition could not be read stamps none.
                AgentVersion = found?.Definition?.Version,
            },

            // Workflow runs start with a structured input, not a single user
            // message; phase 45's eval case promotion covers only agent
            // runs (Phase 45).
            query: null,
            linked.Token).ConfigureAwait(false);

        var startedAt = _timeProvider.GetTimestamp();
        var status = RunStatus.Completed;
        RunError? error = null;

        // 🚨 The run row is OPEN from writer.StartAsync above until
        // CompleteAsync below, and this method is an async iterator: every
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

        try
        {
            yield return FirstEvent(execution);

            // Phase 208: a run that answers a request names, in its own history, the run and
            // the request it answers - the only link back, since the answer starts a new row
            // (K-130). Written on None like the closing event: the link must not be lost to a
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
            // 🚨 F-405: set up BEFORE the slot wait, not after it. The definition
            // carries its own run timeout, and the deadline counts the slot wait;
            // read after the wait, a workflow allowed an hour timed out in the
            // queue on the installation's ten minutes. Setup spends no model call,
            // so it needs no slot.
            Workflow? workflow = null;

            try
            {
                lookupFailure?.Throw();

                var resolved = found
                    ?? throw new TraconException(
                        $"There is no workflow named '{execution.WorkflowName}'.");

                var graph = await _catalog.CompileAsync(resolved, linked.Token).ConfigureAwait(false);

                await ApplyRunTimeoutAsync(execution, resolved, settings, deadline).ConfigureAwait(false);

                // F-294: values are checked against the schema of the SAME
                // definition the graph was built from, before any model
                // call. A failure here closes the run like a graph-setup
                // failure: the caller can fix it and nothing was spent.
                scope = await BindParametersAsync(execution, resolved, scope, linked.Token)
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
                error = ToRunError(exception, execution.RunId);
            }

            // The wait for a concurrency slot belongs to the run: the deadline
            // already counts it, so a run that times out here closes like any
            // other timed-out run — Canceled, naming the setting.
            if (workflow is not null)
            {
                try
                {
                    await _concurrency.WaitAsync(linked.Token).ConfigureAwait(false);
                    holdsSlot = true;
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    status = RunStatus.Canceled;
                }
            }

            if (workflow is not null && holdsSlot)
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
    /// Checks the run's parameter values against the workflow's schema, proves
    /// every participant can be bound, records the final values and returns the
    /// scope the graph runs under.
    /// </summary>
    /// <param name="execution">What is being run.</param>
    /// <param name="found">The workflow the graph came from: a stored definition, or a registration in code.</param>
    /// <param name="scope">The run's scope so far.</param>
    /// <param name="cancellationToken">The run's token.</param>
    /// <returns>
    /// <paramref name="scope"/>, carrying the final values when the workflow
    /// declares parameters.
    /// </returns>
    /// <exception cref="TraconException">
    /// A required value is missing, a name is not declared, a value is too long,
    /// or a participant needs a required parameter nobody supplies.
    /// </exception>
    private async ValueTask<AgentRunScope> BindParametersAsync(
        WorkflowExecution execution,
        WorkflowCatalog.FoundWorkflow found,
        AgentRunScope scope,
        CancellationToken cancellationToken)
    {
        // A stored workflow declares its schema in its definition, a workflow defined in code in its
        // registration (F-537): the same schema, checked by the same code below.
        var definition = found.Definition;
        var schema = definition?.Parameters ?? found.Code?.Parameters ?? [];

        // The runner is the authority (the HTTP endpoint only answers 400
        // early): a consumer can call IWorkflowRunner directly and skip HTTP.
        var validation = _parameterFactory is { } factory
            ? factory.Validate(schema, execution.Parameters)
            : AgentParameterValidator.ValidateValues(
                schema,
                execution.Parameters,
                _traconOptions.Value.MaxParameterValueLength);

        if (!validation.IsValid)
        {
            throw new TraconException(
                $"The run's parameters are invalid for workflow '{execution.WorkflowName}'. " +
                ParameterValueSet.Describe(validation));
        }

        if (schema.Count == 0)
        {
            return scope;
        }

        var final = ParameterValueSet.Resolve(schema, execution.Parameters);

        // The participants of a stored workflow are named by its definition. A graph built in code has
        // no definition to read them from, so its agent steps are checked when they run (the step
        // validates the values it receives and fails by name).
        if (definition is not null)
        {
            await RequireParticipantCoverageAsync(execution.WorkflowName, definition, final, cancellationToken)
                .ConfigureAwait(false);
        }

        await SaveRunInputAsync(execution, scope, final, cancellationToken).ConfigureAwait(false);

        return scope with { ParameterValues = final };
    }

    /// <summary>
    /// Proves, before the first model call, that every required parameter of
    /// every participant has a value or a default.
    /// </summary>
    /// <remarks>
    /// Participants resolve lazily, one step at a time. Without this check the
    /// third step's missing parameter surfaced only after the first two steps had
    /// spent their tokens. The definitions are read here in the order code first,
    /// then the store (the same lookup <c>/run</c> uses); this check is
    /// skipped for a workflow that declares no parameters, which is the common
    /// case and pays nothing.
    /// </remarks>
    private async ValueTask RequireParticipantCoverageAsync(
        string workflowName,
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, string> final,
        CancellationToken cancellationToken)
    {
        if (_parameterFactory is not { } factory)
        {
            return;
        }

        foreach (var (participant, pinnedVersion) in ParticipantNames(definition))
        {
            // A pinned step is covered against ITS version's schema: that is the definition it runs.
            var (agentDefinition, _) = await factory.FindAsync(participant, pinnedVersion, cancellationToken)
                .ConfigureAwait(false);

            if (agentDefinition is not { Parameters.Count: > 0 })
            {
                continue;
            }

            var stepValues = ParameterValueSet.Intersect(agentDefinition.Parameters, final);
            var check = factory.Validate(agentDefinition.Parameters, stepValues);

            if (check.IsValid)
            {
                continue;
            }

            var missing = string.Join(
                ", ",
                check.Errors
                    .Where(static error => string.Equals(error.Code, AgentParameterValidator.MissingParameterCode, StringComparison.Ordinal))
                    .Select(static error => $"'{error.ParameterName}'"));

            throw new TraconException(
                $"Workflow '{workflowName}' cannot start: agent '{participant}' requires parameter(s) " +
                $"{(missing.Length > 0 ? missing : "it cannot be given")} with no default, and the workflow " +
                "does not supply them. Declare the parameter in the workflow's parameters, or give it a " +
                "default value in the agent.");
        }
    }

    /// <summary>Lists every agent the definition's graph can call, with the version a step pins.</summary>
    private static IEnumerable<(string Name, int? PinnedVersion)> ParticipantNames(WorkflowDefinition definition)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in definition.AgentNames)
        {
            if (seen.Add(name))
            {
                yield return (name, null);
            }
        }

        foreach (var node in definition.Nodes)
        {
            if (node.Kind == WorkflowNodeKind.Agent && seen.Add(node.Name))
            {
                yield return (node.Name, node.AgentVersion);
            }
        }

        if (definition.ManagerAgentName is { Length: > 0 } manager && seen.Add(manager))
        {
            yield return (manager, null);
        }
    }

    /// <summary>
    /// Records the run's final parameter values beside its row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The workflow row stores NO messages: the agent path masks its input
    /// through the content guard before it records it, and this
    /// path never sees a guarded copy. Nothing reads a workflow's message back;
    /// a workflow resumes from its checkpoints, and only the values it was
    /// started with are needed.
    /// </para>
    /// <para>
    /// A failure is logged and counted, never thrown: the same contract
    /// <c>IRunInputStore</c> has for the agent path. The consequence is stated
    /// where it bites - a later resume or response finds no values and refuses.
    /// </para>
    /// </remarks>
    private async ValueTask SaveRunInputAsync(
        WorkflowExecution execution,
        AgentRunScope scope,
        IReadOnlyDictionary<string, string> final,
        CancellationToken cancellationToken)
    {
        if (_runInputs is null || !_traconOptions.Value.RunRecording.RecordRunInput)
        {
            return;
        }

        try
        {
            await _runInputs.SaveAsync(
                new RunInputRecord
                {
                    RunId = execution.RunId,
                    TenantId = scope.TenantId ?? _tenantContext.TenantId,
                    Messages = [],
                    Parameters = final,
                    CreatedAt = _timeProvider.GetUtcNow(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            _metrics?.RecordRunRecordingFailure(scope.TenantId, RunRecordingStages.Input);

            _logger.LogWarning(
                exception,
                "Failed to save the parameter values of workflow run {RunId}. The run continues, but it " +
                "cannot be resumed or answered, because the values it started with were not recorded.",
                execution.RunId);
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

            // F-334 (K-640): the failure an executor event reports is logged
            // ONCE, under one ref, and the same ref names it on the event and
            // on the run row - MAF can report one exception as an executor
            // failure, a graph error and a thrown step.
            (Exception Exception, string CorrelationId)? loggedFailure = null;

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
                            stepFailure = PumpedEvent.FromFailure(ToRunError(exception, execution.RunId, loggedFailure));
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

                                await RecordDeliveryAsync(execution).ConfigureAwait(false);

                                continue;
                            }
                        }

                        string? failureText = null;

                        if (FailureOf(workflowEvent) is { } reported)
                        {
                            loggedFailure = LogFailure(reported, execution.RunId, workflowEvent, loggedFailure);
                            failureText = SafeErrorText.ForPersistence(loggedFailure.Value.Exception, loggedFailure.Value.CorrelationId);
                        }

                        var asked = await DescribeApprovalAsync(workflowEvent, linked.Token).ConfigureAwait(false);
                        var mapping = WorkflowEventMapper.Map(
                            workflowEvent, failureText, recording.MaxPayloadLength, asked.Presentation, asked.AgentName);

                        // F-589: an unrecognized event is in the run record now
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
                        // 🚨 F-550: the deadline or the caller can stop the run between
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
                        // (phase 16): when it was only written to the event
                        // stream without changing the status, an executor had
                        // crashed, no output was ever produced, and the run
                        // was still recorded as "Completed" - a row that
                        // looked green in the list but had no result.
                        if (workflowEvent is WorkflowErrorEvent graphError)
                        {
                            yield return PumpedEvent.FromFailure(ToRunError(graphError, execution.RunId, loggedFailure));
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

        if (_approvalPresenter is null)
        {
            return (agentName, null);
        }

        var presentations = await _approvalPresenter
            .ResolveAllAsync([approval], _tenantContext.TenantId, agentName, cancellationToken)
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
                // not say what to do about it. The cause is that executor ids
                // changed. A restart does not change them: WorkflowAgentIdentity
                // gives every agent step a permanent id (K-127). A change to the
                // graph does: the pattern, the steps, their order or the
                // node-list form (K-901), or a code-defined graph whose own
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

        // 🚨 MT-WF-062: an EXTRA TurnToken must NOT be sent on a /respond call
        // (Answers non-empty): the checkpoint already republishes the pending
        // request BY ITSELF (Phase 16 #3) - the extra token
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
        // super-step limit, an undeliverable answer (F-314). Before, none did,
        // and a workflow failure was invisible on the error-class dashboard
        // while its own participant's row carried the class.
        if (error is not null)
        {
            error = Classify(error);
        }

        // A workflow row has no model of its own (phase 20): only the agent
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
            return await ReadRequestEventsAsync(execution.RunId, CancellationToken.None).ConfigureAwait(false);
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

        var tenantId = scope.TenantId ?? _tenantContext.TenantId;

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
    // F-333: before this a workflow produced no run.* event at all, and the
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
        if (_webhookPublisher is null)
        {
            return;
        }

        var tenantId = scope.TenantId ?? _tenantContext.TenantId;

        try
        {
            if (status == RunStatus.AwaitingInput)
            {
                foreach (var (request, _) in awaiting)
                {
                    await _webhookPublisher.PublishAsync(
                        tenantId,
                        WebhookEvents.WorkflowRequestPending,
                        new WebhookEventPayload
                        {
                            OccurredAt = _timeProvider.GetUtcNow(),
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

            var record = await _runStore.GetRunAsync(execution.RunId, CancellationToken.None).ConfigureAwait(false);

            await _webhookPublisher.PublishAsync(
                tenantId,
                eventType,
                WorkflowRunWebhooks.RunPayload(
                    _timeProvider.GetUtcNow(),
                    execution.RunId,
                    execution.SessionId,
                    execution.WorkflowName,
                    status,
                    _timeProvider.GetElapsedTime(startedAt),
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

        // F-418: only this root's own share is written here; its steps wrote theirs (K-394 keeps
        // the workflow one run). F-337: the quota.threshold webhook names the run that crossed
        // the threshold and its user, as it does for an agent run. The token cost is the steps';
        // RunSpend adds nothing across currencies, so the root's amount is the one it reported.
        _ = await TreeQuota.RecordAsync(
            account,
            execution.RunId,
            runs: 1,
            tokens: 0,
            amount: RunSpend.Total((RunCost?)null, nonTokenCost),
            _timeProvider.GetUtcNow(),
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
    /// </remarks>
    private static readonly RunStatus[] SessionOpenStatuses = [RunStatus.AwaitingInput, RunStatus.Running];

    /// <summary>
    /// Whether a run that closes now is not finished: a worker that is stopping
    /// cancelled it, and its job continues it from the latest checkpoint when the job
    /// is leased again.
    /// </summary>
    /// <param name="status">The status the run closes with.</param>
    /// <returns><see langword="true"/> when the run's checkpoints must outlive its close.</returns>
    /// <remarks>
    /// With <see cref="TraconWorkflowOptions.KeepCheckpointsAfterCompletion"/> off,
    /// a graceful shutdown deleted the checkpoints of the run it stopped, and the
    /// continuation ran every finished step again. The continuation shares the session,
    /// so the cleanup runs when it finishes. A run its job cancelled or timed out is
    /// never continued and is cleaned up at once.
    /// </remarks>
    private bool IsLeftToItsJob(RunStatus status)
        => status == RunStatus.Canceled &&
           _options.Value.ResumeInterruptedRuns &&
           AmbientJobAttempt.Current is { Reason: JobStopReason.WorkerStopping };

    private async ValueTask<bool> SessionHasOpenRunAsync(WorkflowExecution execution)
    {
        try
        {
            foreach (var open in SessionOpenStatuses)
            {
                var runs = await _runStore
                    .QueryRunsAsync(
                        new RunQuery
                        {
                            TenantId = _tenantContext.TenantId,
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
            // CancellationToken.None again, so no cancellation of ours (K-840).
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
                .DeleteAsync(_tenantContext.TenantId, execution.SessionId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // No filter: the delete runs on CancellationToken.None, so no
            // OperationCanceledException here is a cancellation of ours (K-840).
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
            // The run's own token decides (K-840): after the caller cancelled,
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

        // F-323, open question 13-A: the job's time limit is a reason of its own, not an
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

        await deadline.RearmAsync(limit, _timeProvider, byRegistration: found.Code is not null).ConfigureAwait(false);
    }

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
    private RunError ToRunError(Exception exception, Guid runId, (Exception Exception, string CorrelationId)? logged = null)
    {
        var unwrapped = UnwrapInvocation(exception);
        string correlationId;

        if (logged is { } previous && ReferenceEquals(previous.Exception, unwrapped))
        {
            // Already logged with its detail when its event was written; the
            // row names the same entry instead of logging it again.
            correlationId = previous.CorrelationId;
        }
        else
        {
            correlationId = SafeErrorText.NewCorrelationId();
            _logger.LogError(unwrapped, "Workflow run {RunId} failed. (ref: {CorrelationId})", runId, correlationId);
        }

        // 🚨 The SAME stable identity the agent run path writes (F-314): a
        // TraconException's ErrorType, not its CLR name. The class is added
        // where the row closes (CompleteAsync), for every error, not only this one.
        return new RunError
        {
            Type = unwrapped is TraconException traconException
                ? traconException.ErrorType
                : unwrapped.GetType().FullName ?? unwrapped.GetType().Name,
            Message = SafeErrorText.ForPersistence(unwrapped, correlationId),
        };
    }

    /// <summary>
    /// Fills <see cref="RunError.Class"/> and <see cref="RunError.Fingerprint"/>,
    /// falling back to the built-in classifier when a consumer's throws.
    /// </summary>
    private RunError Classify(RunError error)
    {
        if (_errorClassifier is null)
        {
            return error;
        }

        RunErrorClassification classification;

        try
        {
            classification = _errorClassifier.Classify(error);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The registered IRunErrorClassifier threw while classifying a workflow run error; falling back to the built-in classifier.");

            classification = new DefaultRunErrorClassifier().Classify(error);
        }

        return error with { Class = classification.Class, Fingerprint = classification.Fingerprint };
    }

    /// <summary>Strips one reflection/handler-invocation wrapper (the rule <c>ToRunError</c> describes).</summary>
    private static Exception UnwrapInvocation(Exception exception)
        => exception switch
        {
            TargetInvocationException { InnerException: { } inner } => inner,
            AggregateException { InnerExceptions.Count: 1 } aggregate => aggregate.InnerExceptions[0],
            _ => exception,
        };

    /// <summary>The exception a failure event reports, or <see langword="null"/> for any other event.</summary>
    private static Exception? FailureOf(WorkflowEvent workflowEvent)
        => workflowEvent switch
        {
            ExecutorFailedEvent failed => failed.Data,
            WorkflowErrorEvent error => error.Exception,
            _ => null,
        };

    /// <summary>
    /// Logs a failure event's exception with its full detail, once, and returns
    /// the exception and the ref its display-safe text carries.
    /// </summary>
    /// <remarks>
    /// The event table, the stream and every run event sink get only
    /// <see cref="SafeErrorText.ForPersistence"/>; the detail lives in the log
    /// under the ref. An exception already logged keeps its ref.
    /// </remarks>
    // K-640, F-334.
    private (Exception Exception, string CorrelationId) LogFailure(
        Exception failure,
        Guid runId,
        WorkflowEvent workflowEvent,
        (Exception Exception, string CorrelationId)? previous)
    {
        var unwrapped = UnwrapInvocation(failure);

        if (previous is { } logged && ReferenceEquals(logged.Exception, unwrapped))
        {
            return logged;
        }

        var correlationId = SafeErrorText.NewCorrelationId();

        _logger.LogError(
            unwrapped,
            "Workflow run {RunId} node {ExecutorId} failed. (ref: {CorrelationId})",
            runId,
            (workflowEvent as ExecutorFailedEvent)?.ExecutorId,
            correlationId);

        return (unwrapped, correlationId);
    }

    /// <summary>Converts a graph-level error into a run error.</summary>
    private RunError ToRunError(WorkflowErrorEvent failure, Guid runId, (Exception Exception, string CorrelationId)? logged = null)
        => failure.Exception is { } exception
            ? ToRunError(exception, runId, logged)
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

        /// <summary>The run and the request this execution answers; <see langword="null"/> for a run or a resume.</summary>
        public AnsweredRequest? AnsweredRequest { get; init; }

        /// <summary>
        /// The parameter values: what the caller supplied to a new run, or the
        /// source run's recorded values on a resume or a response.
        /// <see langword="null"/> means none.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Parameters { get; init; }

        /// <summary>
        /// The stored definition version the run is compiled from. <see langword="null"/>
        /// runs the current definition (a new run, or a source that recorded no version).
        /// </summary>
        public int? DefinitionVersion { get; init; }
    }

    /// <summary>
    /// The run's deadline: the source the runner cancels at the run timeout, when it
    /// was armed, and which setting set the value it carries.
    /// </summary>
    /// <param name="source">The source; owned by the caller.</param>
    /// <param name="armedAt">The monotonic timestamp taken when the run started.</param>
    /// <param name="installation">The installation's run timeout, the value the source was armed with.</param>
    /// <param name="callerToken">The token the run was started with.</param>
    private sealed class RunDeadline(CancellationTokenSource source, long armedAt, TimeSpan installation, CancellationToken callerToken)
    {
        private TimeSpan _limit = installation;
        private bool _setByDefinition;
        private bool _setByRegistration;

        /// <summary>Gets whether the deadline has passed.</summary>
        public bool Fired => source.IsCancellationRequested;

        /// <summary>Gets whether the run's caller cancelled the token the run was started with.</summary>
        public bool CallerStopped => callerToken.IsCancellationRequested;

        /// <summary>
        /// Moves the deadline to <paramref name="limit"/> after the start of the run; a
        /// limit that has already passed cancels at once.
        /// </summary>
        /// <param name="limit">The run timeout the workflow sets.</param>
        /// <param name="clock">The runner's time source.</param>
        /// <param name="byRegistration">Whether a registration in code, not a stored definition, sets it.</param>
        /// <returns>A task that completes when the source is re-armed.</returns>
        public async ValueTask RearmAsync(TimeSpan limit, TimeProvider clock, bool byRegistration)
        {
            _limit = limit;
            _setByDefinition = true;
            _setByRegistration = byRegistration;

            if (source.IsCancellationRequested)
            {
                return;
            }

            var remaining = limit - clock.GetElapsedTime(armedAt);

            // The source was created with the runner's TimeProvider, so CancelAfter
            // moves that provider's timer: a test clock still decides when it fires.
            if (remaining > TimeSpan.Zero)
            {
                source.CancelAfter(remaining);
            }
            else
            {
                await source.CancelAsync().ConfigureAwait(false);
            }
        }

        /// <summary>The reason a run that reached this deadline carries.</summary>
        /// <returns>The run error, naming the setting that set the value.</returns>
        public RunError ToRunError()
            => new()
            {
                Type = nameof(TimeoutException),
                Message = _setByRegistration
                    ? $"Workflow timed out and was stopped after {_limit}, the run timeout its registration sets. " +
                      "Raise WorkflowRegistrationOptions.RunTimeout (up to 'Tracon:Workflows:MaxRunTimeout'), or shorten the graph."
                    : _setByDefinition
                    ? $"Workflow timed out and was stopped after {_limit}, the run timeout its definition sets. " +
                      "Raise the workflow's 'runTimeout' (up to 'Tracon:Workflows:MaxRunTimeout'), or shorten the graph."
                    : "Workflow timed out and was stopped. " +
                      "Raise the 'Tracon:Workflows:RunTimeout' value, or " +
                      "shorten the graph.",
            };
    }

    /// <summary>The result of one attempt to send a response.</summary>
    /// <param name="Delivered">Whether the response was sent to execution.</param>
    /// <param name="Failure">The error, if the response could not be converted.</param>
    private readonly record struct ResponseDelivery(bool Delivered, RunError? Failure);

    /// <summary>The request a run answers, and the run that waited for it.</summary>
    /// <param name="SourceRunId">The run that waited.</param>
    /// <param name="RequestId">The request.</param>
    private readonly record struct AnsweredRequest(Guid SourceRunId, string RequestId);

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
