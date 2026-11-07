namespace Tracon;

/// <summary>
/// Turns a request to resume a run, or to answer a request it waits on, into the recipe the runner executes: the run
/// it continues, the checkpoint it starts from, and the parameter values the source run recorded.
/// </summary>
/// <remarks>
/// It refuses, before anything runs, what could not continue honestly: a run of another tenant (reported as not
/// found), a run that does not wait for the answer, an unknown request, a run with no checkpoint, and a workflow whose
/// parameters changed since the run started.
/// </remarks>
internal sealed class WorkflowResumePlanner
{
    private readonly WorkflowRequestReader _reader;
    private readonly WorkflowCatalog _catalog;
    private readonly IWorkflowCheckpointStore _checkpointStore;
    private readonly IRunInputStore? _runInputStore;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates the planner.</summary>
    /// <param name="reader">Reads the run row and the requests the run waits on.</param>
    /// <param name="catalog">The workflow catalog, which holds the parameter schema of a definition version.</param>
    /// <param name="checkpointStore">The checkpoint store.</param>
    /// <param name="runInputStore">
    /// Where the source run recorded its parameter values. When <see langword="null"/>, a workflow that declares
    /// parameters cannot be continued.
    /// </param>
    /// <param name="tenantContext">The tenant context.</param>
    public WorkflowResumePlanner(
        WorkflowRequestReader reader,
        WorkflowCatalog catalog,
        IWorkflowCheckpointStore checkpointStore,
        IRunInputStore? runInputStore,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _reader = reader;
        _catalog = catalog;
        _checkpointStore = checkpointStore;
        _runInputStore = runInputStore;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Turns a resume request into a runnable execution recipe.
    /// </summary>
    /// <exception cref="TraconException">
    /// The run does not exist, is not a workflow run, belongs to another tenant,
    /// or the requested checkpoint cannot be found.
    /// </exception>
    internal async ValueTask<WorkflowExecution> PrepareResumeAsync(
        WorkflowResumeRequest request,
        CancellationToken cancellationToken)
    {
        var record = await _reader.RequireWorkflowRunAsync(request.RunId, cancellationToken).ConfigureAwait(false);

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
            SourceRunId = record.Id,
            RecordsContinuation = true,
            RowMayBeOpen = request.NewRunId is not null,
        };
    }

    /// <summary>
    /// Turns a response request into an execution recipe that resumes from a checkpoint.
    /// </summary>
    /// <exception cref="TraconException">
    /// The run cannot be found, does not wait for human input, or the given
    /// request id does not exist on that run.
    /// </exception>
    internal async ValueTask<WorkflowExecution> PrepareResponseAsync(
        WorkflowRespondRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId, nameof(request));

        var record = await _reader.RequireWorkflowRunAsync(request.RunId, cancellationToken).ConfigureAwait(false);

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
        var pending = await _reader.ListPendingRequestsAsync(request.RunId, cancellationToken).ConfigureAwait(false);

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

            // The answer names the run it answers in its own history (WorkflowResponseApplied), and the
            // budget is read back along that link. The row does not name it as a continuation.
            SourceRunId = record.Id,
            RowMayBeOpen = request.NewRunId is not null,
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

        var recorded = _runInputStore is null
            ? null
            : (await _runInputStore.GetAsync(_tenantContext.TenantId, source.Id, cancellationToken).ConfigureAwait(false))
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
    internal async ValueTask<WorkflowCheckpointRecord?> FindCheckpointMetadataAsync(
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
}
