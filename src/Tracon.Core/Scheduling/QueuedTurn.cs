using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Writes what one queued root turn leaves behind after its model call: the
/// session and, when the turn ends by asking for approval, one
/// <see cref="PendingApproval"/> per request. The session comes first.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the three handlers that run a queued root turn:
/// <see cref="AgentRunJobHandler"/>, <see cref="ApprovalResumeJobHandler"/> and
/// <see cref="RunContinuationJobHandler"/>. Each of them can end by asking for
/// approval, and nothing but the mailbox can answer it: no caller is waiting to
/// send the next turn. When only the first handler wrote the mailbox, a resumed
/// or continued turn that asked for another approval closed
/// <see cref="RunStatus.AwaitingApproval"/> with nothing to list.
/// </para>
/// <para>
/// <strong>Order.</strong> <see cref="BeforePendingApprovalIsPublishedAsync"/>
/// runs before the run reports <see cref="RunStatus.AwaitingApproval"/>. It saves
/// the session first, because the decision queues a resume that reads it, and
/// writes the approvals second, because a consumer lists them once it sees the
/// status.
/// </para>
/// <para>
/// <strong>A store error on the session save.</strong> The save is part of the
/// turn, but the model has already answered when it runs. A store error is
/// therefore tried again, with a pause that doubles, until the lease the worker
/// can prove runs out — a window fixed at the first error, the same rule the
/// worker uses to write a job's outcome. Past it the save throws
/// <see cref="NonRetryableJobException"/>: the job fails with
/// <see cref="SessionSaveFailedCode"/>, and the handler, with its model call, does
/// not run again. A <see cref="TraconException"/> (a turn that conflicts with
/// another, an owner that cannot be resolved) is Tracon's own answer to this
/// write, and it is not tried again: the same write gets the same answer.
/// </para>
/// <para>
/// <strong>A store error on an approval write</strong> gets the same
/// window: the session is already saved, and the model and the tools already ran.
/// Each approval keeps the id it was given for the whole window, and a write that
/// is tried again reads that id first: a write the store committed but reported as
/// failed is not written twice. Past the window the job fails with
/// <see cref="ApprovalSaveFailedCode"/>, and the run, whose approval nobody can
/// list, closes <see cref="RunStatus.Failed"/>.
/// </para>
/// <para>
/// One instance per turn: it remembers whether the session was already saved.
/// </para>
/// </remarks>
internal sealed class QueuedTurn
{
    /// <summary>
    /// The code a job fails with when its turn ended but the session could not be
    /// saved. The run keeps the status the turn gave it.
    /// </summary>
    internal const string SessionSaveFailedCode = "tracon.job.session-save-failed";

    /// <summary>
    /// The code a job fails with when its turn ended at an approval but the approval
    /// could not be written. Nobody can list it, so the run closes as failed.
    /// </summary>
    internal const string ApprovalSaveFailedCode = "tracon.job.approval-save-failed";

    /// <summary>The pause before a failed session save is tried again.</summary>
    private static readonly TimeSpan RetryFirstDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>The longest pause between two tries; the pause doubles up to it.</summary>
    private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(5);

    private readonly AgentSessionManager _sessions;
    private readonly IPendingApprovalStore _approvalStore;
    private readonly TraconOptions _options;
    private readonly IOptionsMonitor<TraconApprovalOptions>? _approvalOptions;
    private readonly IWebhookPublisher? _webhookPublisher;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly JobContext _job;
    private readonly Guid _runId;
    private readonly AIAgent _agent;
    private readonly AgentSession? _session;
    private bool _sessionSaved;

    /// <summary>Starts the bookkeeping of one turn.</summary>
    /// <param name="sessions">Saves the session.</param>
    /// <param name="approvalStore">Holds the pending approvals.</param>
    /// <param name="options">Decides whether a tool call's arguments are recorded.</param>
    /// <param name="approvalOptions">The expiration of a pending approval; 24 hours without it.</param>
    /// <param name="webhookPublisher">Announces each pending approval, when registered.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="job">The job that runs the turn.</param>
    /// <param name="runId">The turn's run.</param>
    /// <param name="agent">The agent that runs the turn; it serializes the session.</param>
    /// <param name="session">The turn's session; <see langword="null"/> for a sessionless run.</param>
    public QueuedTurn(
        AgentSessionManager sessions,
        IPendingApprovalStore approvalStore,
        IOptions<TraconOptions> options,
        IOptionsMonitor<TraconApprovalOptions>? approvalOptions,
        IWebhookPublisher? webhookPublisher,
        TimeProvider clock,
        ILogger? logger,
        JobContext job,
        Guid runId,
        AIAgent agent,
        AgentSession? session)
    {
        _sessions = sessions;
        _approvalStore = approvalStore;
        _options = options.Value;
        _approvalOptions = approvalOptions;
        _webhookPublisher = webhookPublisher;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;
        _job = job;
        _runId = runId;
        _agent = agent;
        _session = session;
    }

    /// <summary>
    /// The <see cref="TraconRunOptions.BeforePendingApprovalIsPublished"/> callback:
    /// saves the session, then writes and announces one <see cref="PendingApproval"/>
    /// per request, before the run reports <see cref="RunStatus.AwaitingApproval"/>.
    /// </summary>
    /// <param name="producedMessages">The messages the run produced.</param>
    /// <param name="presentations">
    /// The presentation already resolved for each request, keyed by <c>RequestId</c> —
    /// resolved once by <see cref="RunRecordingAgent"/>, not a second time here.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    public async ValueTask BeforePendingApprovalIsPublishedAsync(
        IEnumerable<ChatMessage> producedMessages,
        IReadOnlyDictionary<string, ToolApprovalPresentation?> presentations,
        CancellationToken cancellationToken)
    {
        var requests = ChildRunApproval.CollectRequests(producedMessages);

        // A sessionless run can never have its request answered; the queued-run
        // handler corrects that run to Failed after the call returns.
        if (requests.Count == 0 || _session is null)
        {
            return;
        }

        await SaveAsync(_session, cancellationToken).ConfigureAwait(false);
        await RecordPendingApprovalsAsync(_session, requests, presentations, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves the session after the model call returned, unless
    /// <see cref="BeforePendingApprovalIsPublishedAsync"/> already saved it.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <exception cref="NonRetryableJobException">The session could not be saved.</exception>
    public ValueTask SaveSessionAsync(CancellationToken cancellationToken)
        => _session is null || _sessionSaved ? default : SaveAsync(_session, cancellationToken);

    private ValueTask SaveAsync(AgentSession session, CancellationToken cancellationToken)
        => WithinLeaseAsync(
            async (_, token) =>
            {
                await _sessions.SaveSessionAsync(_agent, session, token).ConfigureAwait(false);
                _sessionSaved = true;
            },
            (exception, delay) => _logger.LogWarning(
                exception,
                "Queued run {RunId}: could not save session {SessionId}; trying again in {Delay}.",
                _runId,
                AgentSessionIdentity.GetId(session),
                delay),
            exception => GiveUp(session, exception),
            cancellationToken);

    /// <summary>
    /// Runs one write of the turn, trying a store error again with a pause that
    /// doubles until the lease the worker can prove runs out.
    /// </summary>
    /// <param name="write">The write; its first argument says whether this is a second try or later.</param>
    /// <param name="logRetry">Logs a failed try and the pause before the next one.</param>
    /// <param name="giveUp">Builds what is thrown when the window is spent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the write succeeded.</returns>
    /// <exception cref="NonRetryableJobException">The window is spent, or Tracon refused the write.</exception>
    private async ValueTask WithinLeaseAsync(
        Func<bool, CancellationToken, ValueTask> write,
        Action<Exception, TimeSpan> logRetry,
        Func<Exception, NonRetryableJobException> giveUp,
        CancellationToken cancellationToken)
    {
        var delay = RetryFirstDelay;
        long? firstFailureAt = null;
        var window = TimeSpan.Zero;

        while (true)
        {
            try
            {
                await write(firstFailureAt is not null, cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                if (firstFailureAt is null)
                {
                    // Fixed here and not moved by later renewals: a store that keeps
                    // the lease alive while it refuses the write would otherwise hold
                    // the job, and the worker's slot, without end. Without a worker
                    // there is no lease to prove, and the write is tried once.
                    firstFailureAt = _clock.GetTimestamp();
                    window = _job.ProvableLeaseRemaining?.Invoke() ?? TimeSpan.Zero;
                }

                var left = window - _clock.GetElapsedTime(firstFailureAt.Value);

                if (exception is TraconException || left <= TimeSpan.Zero)
                {
                    throw giveUp(exception);
                }

                if (delay > left)
                {
                    delay = left;
                }

                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    logRetry(exception, delay);
                }
            }

            await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, RetryMaxDelay.Ticks));
        }
    }

    private NonRetryableJobException GiveUp(AgentSession session, Exception exception)
    {
        var correlationId = SafeErrorText.NewCorrelationId();

        if (_logger.IsEnabled(LogLevel.Error))
        {
            _logger.LogError(
                exception,
                "Queued run {RunId}: the turn ended, but session {SessionId} could not be saved. Job {JobId} fails with {ErrorCode} and the model is not called again. (ref: {CorrelationId})",
                _runId,
                AgentSessionIdentity.GetId(session),
                _job.Job.Id,
                SessionSaveFailedCode,
                correlationId);
        }

        return new NonRetryableJobException(SessionSaveFailedCode, correlationId, exception);
    }

    private NonRetryableJobException GiveUp(PendingApproval approval, Exception exception)
    {
        var correlationId = SafeErrorText.NewCorrelationId();

        if (_logger.IsEnabled(LogLevel.Error))
        {
            _logger.LogError(
                exception,
                "Queued run {RunId}: the turn ended at an approval, but approval {RequestId} could not be written. Job {JobId} fails with {ErrorCode} and the model is not called again. (ref: {CorrelationId})",
                _runId,
                approval.RequestId,
                _job.Job.Id,
                ApprovalSaveFailedCode,
                correlationId);
        }

        return new NonRetryableJobException(ApprovalSaveFailedCode, correlationId, exception);
    }

    /// <summary>Writes one <see cref="PendingApproval"/> per request and announces each.</summary>
    private async ValueTask RecordPendingApprovalsAsync(
        AgentSession session,
        IReadOnlyList<ToolApprovalRequestContent> requests,
        IReadOnlyDictionary<string, ToolApprovalPresentation?> presentations,
        CancellationToken cancellationToken)
    {
        // Saved a moment ago through the same identity, so it is there.
        var sessionId = AgentSessionIdentity.GetId(session)!;
        var expiration = _approvalOptions?.CurrentValue.DefaultExpiration ?? TimeSpan.FromHours(24);
        var now = _clock.GetUtcNow();

        foreach (var request in requests)
        {
            var approval = new PendingApproval
            {
                Id = TraconId.NewId(),
                TenantId = _job.Job.TenantId,
                RunId = _runId,
                SessionId = sessionId,
                RequestId = request.RequestId,
                ToolName = request.ToolCall is FunctionCallContent call ? call.Name : "unknown",
                Arguments = _options.RunRecording.RecordToolPayloads && request.ToolCall is FunctionCallContent argsCall
                    ? FormatArguments(argsCall)
                    : null,
                Presentation = presentations.GetValueOrDefault(request.RequestId),
                Status = ApprovalStatus.Pending,
                ExpiresAt = now + expiration,
                CreatedAt = now,
            };

            await WithinLeaseAsync(
                async (retry, token) =>
                {
                    // F-391: a failed write may have committed before it failed. The id is
                    // fixed for this approval, so a second try reads it back first.
                    if (retry && await _approvalStore.GetAsync(approval.Id, token).ConfigureAwait(false) is not null)
                    {
                        return;
                    }

                    await _approvalStore.CreateAsync(approval, token).ConfigureAwait(false);
                },
                (exception, delay) => _logger.LogWarning(
                    exception,
                    "Queued run {RunId}: could not write approval {RequestId}; trying again in {Delay}.",
                    _runId,
                    approval.RequestId,
                    delay),
                exception => GiveUp(approval, exception),
                cancellationToken).ConfigureAwait(false);

            if (_webhookPublisher is not null)
            {
                await _webhookPublisher.PublishAsync(
                    _job.Job.TenantId,
                    WebhookEvents.ApprovalPending,
                    new WebhookEventPayload
                    {
                        OccurredAt = now,
                        Approval = new WebhookApprovalSummary
                        {
                            RequestId = approval.Id.ToString(),
                            RunId = approval.RunId.ToString(),
                            ToolName = approval.ToolName,
                        },
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string? FormatArguments(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
        {
            return null;
        }

        // SAME rationale as RunRecordingAgent.FormatArguments: formatted by
        // hand to stay AOT compatible; reflection-based JSON serialization is
        // not used.
        return string.Join(", ", call.Arguments.Select(static pair => $"{pair.Key}={pair.Value}"));
    }
}
