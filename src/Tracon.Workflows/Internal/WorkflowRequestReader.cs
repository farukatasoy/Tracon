using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Reads what the workflow runs wait on: the requests a run published, with the facts the inbox rows add, and the run
/// row a resume or an answer starts from.
/// </summary>
/// <remarks>
/// A read here never writes. A failure to read the inbox rows is logged and answered with no rows: answering a request
/// must not depend on the inbox.
/// </remarks>
internal sealed class WorkflowRequestReader
{
    private readonly IRunStore _runStore;
    private readonly ITenantContext _tenantContext;
    private readonly IWorkflowRequestStore? _requests;
    private readonly ILogger _logger;

    /// <summary>Creates the reader.</summary>
    /// <param name="runStore">The run store the request events and the run rows are read from.</param>
    /// <param name="tenantContext">The tenant context. A run of another tenant is reported as not found.</param>
    /// <param name="requests">The inbox of the requests a run waits on. When <see langword="null"/>, no inbox row is read.</param>
    /// <param name="logger">The logger of the runner that owns the run.</param>
    public WorkflowRequestReader(
        IRunStore runStore,
        ITenantContext tenantContext,
        IWorkflowRequestStore? requests,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);

        _runStore = runStore;
        _tenantContext = tenantContext;
        _requests = requests;
        _logger = logger;
    }

    /// <summary>Lists the requests a run that waits for human input is waiting on.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requests; empty when the run does not wait.</returns>
    /// <exception cref="TraconException">The run does not exist, belongs to another tenant or is not a workflow run.</exception>
    internal async ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireWorkflowRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // A run that was answered STAYS in AwaitingInput (status history is not
        // rewritten afterwards) and still lists its request; the
        // continuing work lives on a new run row. Answering it again opens a
        // branch, on purpose.
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

    /// <summary>Lists the requests the inbox rows of the tenant name.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requests; empty when no inbox store is set.</returns>
    internal async ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListRequestsAsync(
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
    internal async ValueTask<IReadOnlyList<(WorkflowPendingRequest Request, long Sequence)>> ReadRequestEventsAsync(
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

    /// <summary>Finds the run and validates that it is a resumable workflow row.</summary>
    /// <exception cref="TraconException">
    /// The run does not exist, belongs to another tenant, is not a workflow row,
    /// or carries no execution session.
    /// </exception>
    internal async ValueTask<RunRecord> RequireWorkflowRunAsync(Guid runId, CancellationToken cancellationToken)
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
}
