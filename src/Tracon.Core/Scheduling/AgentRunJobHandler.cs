using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Executes <see cref="JobHandlerKeys.AgentRun"/> jobs: runs a single, queued
/// (<c>Prefer: respond-async</c>) agent run from the queue.
/// </summary>
/// <remarks>
/// <para>
/// Follows the same pattern as <see cref="AgentBatchJobHandler"/>: the agent is
/// resolved through <see cref="IAgentCatalog.ResolveAsync(string, string, CancellationToken)"/>,
/// the resolved agent is already wrapped by the recording decorator, and the
/// run is written to <see cref="IRunStore"/> as a normal <c>runs</c> row.
/// </para>
/// <para>
/// The identity handed out here is the SAME identity the HTTP layer
/// promised the client through <c>Location</c> (<see cref="TraconRunOptions.RunId"/>):
/// this keeps the promise of the <c>202</c> response VALID even after the job
/// actually runs from the queue. If the lease expires and the job is re-leased
/// within its attempt limit, this method is called a second time with the SAME
/// identity; the store treats <c>StartRunAsync</c> as an UPSERT (see
/// <c>RunStartInfo.Status</c>) — no new <c>runs</c> row is OPENED. Past the
/// limit (the default <c>AsyncRun.MaxAttempts</c> is one), the worker fails the
/// job without calling this method (<see cref="JobErrorCodes.AttemptsExhausted"/>),
/// and the run row left <c>Running</c> is closed by run reconciliation.
/// </para>
/// <para>
/// If the response carries a tool call awaiting approval, <see cref="RunRecordingAgent"/>
/// closes the run with <see cref="RunStatus.AwaitingApproval"/> (valid behavior
/// for ALL root runs, not specific to the queue path alone —
/// see <c>RunRecordingAgent</c>). The queue path additionally writes a
/// <see cref="PendingApproval"/> row for each request in that case, through
/// <see cref="QueuedTurn"/> (writing to this store REMAINS specific to the
/// queue path, to avoid the risk of a double-decision race); the decision is
/// made through <c>POST /api/approvals/{id}/decide</c> and queues a NEW run
/// (see <c>ApprovalResumeJobHandler</c>). The session is saved before the
/// approvals are written, and a store error on that save is tried again
/// rather than running the turn a second time (see <see cref="QueuedTurn"/>).
/// </para>
/// <para>
/// The HTTP layer assigns the experiment arm and captures the culture at
/// enqueue time and writes both into the payload; this handler resolves the
/// agent with that version and culture and records the arm on the run, as the
/// synchronous path does. A payload is durable data an administrator can also
/// write by hand through a schedule, so an arm is honored only when it matches
/// a started experiment of this tenant for this agent, and the run is refused
/// otherwise.
/// </para>
/// <para>
/// The payload also carries the run's parameter <c>values</c> and its
/// reference <c>documents</c>. The values are checked against the schema of the
/// version being run (a payload is durable data, and the agent may have changed
/// since the 202), bound with the same compile-outside-the-catalog sequence the
/// synchronous endpoint uses, and recorded with the run. A field that is absent
/// means none, so a payload an older build wrote runs as it always did; a field
/// that is malformed fails the run once, naming the field, and closes the
/// placeholder row.
/// </para>
/// <para>
/// The <c>userId</c> and <c>labels</c> the HTTP layer resolved travel in the
/// payload too, and the run replays both through
/// <see cref="AmbientRunAttributionScope"/>: the run's sub-agents and tools read
/// them from there. Unlike the fields above, a malformed one is dropped, not
/// thrown on (see <see cref="QueuedRunAttribution"/>).
/// </para>
/// </remarks>
internal sealed class AgentRunJobHandler(
    IAgentCatalog catalog,
    AgentSessionManager sessions,
    IRunStore runStore,
    IPendingApprovalStore approvalStore,
    IExperimentStore experimentStore,
    IOptions<TraconOptions> options,
    IOptionsMonitor<TraconApprovalOptions>? approvalOptionsMonitor = null,
    IWebhookPublisher? webhookPublisher = null,
    TimeProvider? timeProvider = null,
    ILogger<AgentRunJobHandler>? logger = null,
    ParameterizedAgentFactory? parameterFactory = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (runId, message, sessionId, userId, culture) = ParsePayload(context.Job.Payload);
        var labels = ReadOptionalLabels(context.Job.Payload, runId);

        // 🚨 Opened in THIS method's own body, wrapping every await that
        // follows. An AsyncLocal write made inside an async helper does not
        // flow back to its caller, so resolving the identity in a helper and
        // returning it here would leave the scope closed by the time the
        // session is saved. Everything the run touches - the session manager's
        // owner resolution, the run record's attribution, its sub-agents and
        // tools - reads from this one scope, which is why the value has to be
        // replayed rather than passed as a parameter to one of them. F-369: the
        // labels travel too; opened with the user alone, the run's sub-agents
        // and tools saw none of the labels its own row carried.
        using var attribution = AmbientRunAttributionScope.Begin(userId, labels);

        Microsoft.Agents.AI.AIAgent agent;
        Microsoft.Agents.AI.AgentSession? session;
        QueuedRunArm arm;
        IReadOnlyDictionary<string, string>? recordedValues;
        IReadOnlyList<AgentRunDocument> documents;

        try
        {
            // F-351: the version, the arm and the culture were fixed at enqueue
            // time. Resolving without them ran the current version with the
            // default instructions, and the run counted in no arm.
            arm = await VerifyArmAsync(context.Job, cancellationToken).ConfigureAwait(false);

            // Parsed here, inside the try, so a malformed field closes the
            // queued placeholder row as Failed instead of leaving it Queued.
            var suppliedValues = ReadOptionalParameters(context.Job.Payload);
            documents = ReadOptionalDocuments(context.Job.Payload);

            agent = await catalog.ResolveAsync(context.Job.TargetName, arm.Version, culture, cancellationToken).ConfigureAwait(false)
                ?? throw new TraconException(
                    $"No agent named '{context.Job.TargetName}' was found. The job will be marked as failed.");

            // F-294: an agent that declares a schema carries the guard the
            // compiler puts on it, which tells us without a store read; an
            // agent without one and a payload without values skips the lookup
            // entirely (the common case pays nothing).
            recordedValues = null;

            if (suppliedValues is { Count: > 0 } || agent.GetService<UnboundParameterGuard>() is not null)
            {
                if (parameterFactory is null)
                {
                    throw new TraconException(
                        "This host cannot bind run parameters: the parameter factory is not registered.");
                }

                var binding = await parameterFactory
                    .BindAsync(context.Job.TargetName, arm.Version, culture, suppliedValues, cancellationToken)
                    .ConfigureAwait(false);

                agent = binding.Agent ?? agent;
                recordedValues = binding.Values;
            }

            session = string.IsNullOrWhiteSpace(sessionId)
                ? null
                : await sessions.GetOrCreateSessionAsync(agent, sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // 🚨 The agent never resolved: RunRecordingAgent never engaged, so
            // we must close the 'runs' row (Queued) that the 202 promised HERE,
            // to Failed -- otherwise the client sees 'Queued' forever through
            // GET /api/runs/{id}. Real run failures (AFTER the agent resolves)
            // do not enter this block; RunRecordingAgent already closes those
            // as Failed (see RunRecordingAgent.CompleteAsync).
            await FailQueuedRunAsync(runId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        // Documents first, one message each, then the request: the order the
        // synchronous run builds (AgentEndpoints.BuildMessagesAsync).
        List<ChatMessage> messages = [.. documents.Select(DocumentChannelMessageBuilder.Build), new(ChatRole.User, message)];

        // 🚨 The session and the approvals are written BEFORE the run's terminal
        // AwaitingApproval status is published, session first (F-133) - see
        // QueuedTurn. A store error on the save no longer fails the turn or
        // runs it again (F-367).
        var turn = new QueuedTurn(
            sessions, approvalStore, options, approvalOptionsMonitor, webhookPublisher, _clock, logger,
            context, runId, agent, session);

        var response = await agent.RunAsync(
            messages,
            session,
            new TraconRunOptions
            {
                RunId = runId,
                AgentVersion = arm.Version,
                ExperimentId = arm.ExperimentId,
                Variant = arm.Variant,
                ParameterValues = recordedValues,
                BeforePendingApprovalIsPublished = turn.BeforePendingApprovalIsPublishedAsync,
            },
            cancellationToken).ConfigureAwait(false);

        await turn.SaveSessionAsync(cancellationToken).ConfigureAwait(false);

        var pendingRequests = ChildRunApproval.CollectRequests(response.Messages);

        if (pendingRequests.Count == 0)
        {
            return;
        }

        if (session is null)
        {
            // Approval is the input to the next turn and can ONLY be resolved
            // from session history; a run without a session can never have its
            // pending request answered. See AgentEndpoints.RunAsync for the
            // same constraint on the synchronous path ("session required for
            // approval"). RunRecordingAgent already closed this run with
            // AwaitingApproval; it is CORRECTED to Failed here.
            await FailQueuedRunAsync(
                runId,
                context.Job.TenantId,
                new TraconException(
                    "The queued run requested approval, but no 'sessionId' was given; " +
                    "approval is the input to the next turn and cannot be resolved without a session."),
                cancellationToken).ConfigureAwait(false);

            return;
        }

        // The approvals themselves were already written inside the hook above.
    }

    private async ValueTask FailQueuedRunAsync(
        Guid runId,
        string tenantId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = SafeErrorText.NewCorrelationId();

        if (logger is not null && logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(exception, "Queued run {RunId} failed. (ref: {CorrelationId})", runId, correlationId);
        }

        try
        {
            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = RunStatus.Failed,
                    CompletedAt = _clock.GetUtcNow(),
                    Error = new RunError
                    {
                        Type = exception.GetType().Name,
                        Message = SafeErrorText.ForPersistence(exception, correlationId),
                    },

                    // The job's own tenant; the party that enqueued it wrote it (K-355).
                    TenantId = tenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception storeException) when (OperationCancellation.IsFailure(storeException, cancellationToken))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    storeException,
                    "Could not close the queued run to Failed status: {RunId}.",
                    runId);
            }
        }
    }

    /// <summary>
    /// Reads the version and experiment arm the payload carries, and checks the
    /// arm against the experiment store.
    /// </summary>
    /// <param name="job">The job, which names the tenant, the agent, and the payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The version to resolve and the arm to record. All three are
    /// <see langword="null"/> for a job enqueued before these fields existed,
    /// which keeps that job's earlier behavior: the current version, no arm.
    /// </returns>
    /// <exception cref="TraconException">
    /// A field is malformed, the arm is only partly given, or the arm does not
    /// match a started experiment of this tenant for this agent.
    /// </exception>
    private async ValueTask<QueuedRunArm> VerifyArmAsync(JobRecord job, CancellationToken cancellationToken)
    {
        var payload = job.Payload;
        var version = ReadOptionalVersion(payload);
        var experimentId = ReadOptionalExperimentId(payload);
        var variant = ReadOptionalVariant(payload);

        // A version alone is a pinned run, which a caller outside any
        // experiment may ask for; only an arm label needs an experiment
        // behind it.
        if (experimentId is null && variant is null)
        {
            return new QueuedRunArm(version, null, null);
        }

        if (experimentId is null || variant is null || version is null)
        {
            throw new TraconException(
                "The AgentRun job payload names an experiment arm only in part: 'experimentId', 'variant' " +
                "and 'agentVersion' go together. The run will not start.");
        }

        // 🚨 Fail closed (F-351). AgentRun is a built-in handler key an
        // administrator can schedule with a hand-written payload. Recording a
        // run under an arm whose version it did not run would corrupt the
        // experiment's results, so the arm must name a variant of this
        // tenant's experiment for this agent, with this exact version. A
        // Draft experiment is refused as well: the HTTP layer only assigns
        // from a started one, and a draft's variants can still change.
        var experiments = await experimentStore.ListAsync(job.TenantId, cancellationToken).ConfigureAwait(false);
        var experiment = experiments.FirstOrDefault(candidate => candidate.Id == experimentId.Value);

        var matches = experiment is not null &&
            experiment.Status != ExperimentStatus.Draft &&
            string.Equals(experiment.AgentName, job.TargetName, StringComparison.Ordinal) &&
            experiment.Variants.Any(candidate =>
                string.Equals(candidate.Name, variant, StringComparison.Ordinal) &&
                candidate.Version == version.Value);

        if (!matches)
        {
            throw new TraconException(
                "The AgentRun job payload names an experiment arm that is not a variant of a started experiment " +
                $"for agent '{job.TargetName}' in this tenant with version {version.Value}. The run will not start.");
        }

        return new QueuedRunArm(version, experimentId, variant);
    }

    /// <summary>
    /// Reads the optional <c>labels</c> object the HTTP layer captured at
    /// enqueue time. A malformed value is dropped with a warning: labels only
    /// describe the run, and a throw would fail the job on every attempt.
    /// </summary>
    private IReadOnlyDictionary<string, string>? ReadOptionalLabels(JsonElement payload, Guid runId)
    {
        var labels = QueuedRunAttribution.ReadLabels(payload, out var problem);

        if (problem is not null && logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("Queued run {RunId} runs without labels: {Problem}", runId, problem);
        }

        return labels;
    }

    /// <summary>Reads the optional <c>parameters</c> object: every value must be a string.</summary>
    /// <exception cref="TraconException">The field is present and is not an object of strings.</exception>
    private static Dictionary<string, string>? ReadOptionalParameters(JsonElement payload)
    {
        if (!payload.TryGetProperty("parameters", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new TraconException("The AgentRun job payload's 'parameters' field must be an object of string values.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new TraconException(
                    $"The AgentRun job payload's 'parameters' field must be an object of string values; '{property.Name}' is not a string.");
            }

            values[property.Name] = property.Value.GetString()!;
        }

        return values;
    }

    /// <summary>Reads the optional <c>documents</c> array: each element needs a string <c>name</c> and <c>content</c>.</summary>
    /// <exception cref="TraconException">The field is present and malformed.</exception>
    private static List<AgentRunDocument> ReadOptionalDocuments(JsonElement payload)
    {
        if (!payload.TryGetProperty("documents", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new TraconException("The AgentRun job payload's 'documents' field must be an array of {name, content} objects.");
        }

        var documents = new List<AgentRunDocument>(element.GetArrayLength());

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            {
                throw new TraconException(
                    "The AgentRun job payload's 'documents' field must be an array of {name, content} objects with string members.");
            }

            documents.Add(new AgentRunDocument { Name = name.GetString()!, Content = content.GetString()! });
        }

        return documents;
    }

    private static int? ReadOptionalVersion(JsonElement payload)
    {
        if (!payload.TryGetProperty("agentVersion", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        // Thrown on, unlike a malformed userId: dropping a version would run
        // the CURRENT one, a different prompt than the caller was promised.
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var version) || version < 1)
        {
            throw new TraconException("The AgentRun job payload's 'agentVersion' field must be a positive integer.");
        }

        return version;
    }

    private static Guid? ReadOptionalExperimentId(JsonElement payload)
    {
        if (!payload.TryGetProperty("experimentId", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var experimentId))
        {
            throw new TraconException("The AgentRun job payload's 'experimentId' field must be a valid identifier.");
        }

        return experimentId;
    }

    private static string? ReadOptionalVariant(JsonElement payload)
    {
        if (!payload.TryGetProperty("variant", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { Length: > 0 } variant)
        {
            throw new TraconException("The AgentRun job payload's 'variant' field must be a non-empty string.");
        }

        return variant;
    }

    private static (Guid RunId, string Message, string? SessionId, string? UserId, string? Culture) ParsePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("runId", out var runIdElement) ||
            runIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(runIdElement.GetString(), out var runId))
        {
            throw new TraconException("The AgentRun job payload must carry a valid 'runId' field.");
        }

        if (!payload.TryGetProperty("message", out var messageElement) ||
            messageElement.ValueKind != JsonValueKind.String ||
            messageElement.GetString() is not { Length: > 0 } message)
        {
            throw new TraconException("The AgentRun job payload must carry a valid 'message' field.");
        }

        var sessionId = payload.TryGetProperty("sessionId", out var sessionElement) &&
            sessionElement.ValueKind == JsonValueKind.String
            ? sessionElement.GetString()
            : null;

        // Absent on every job enqueued before this field existed, and on every
        // job enqueued by a caller Tracon could not identify. Both read
        // back as null, which is what an unattributed run has always been.
        var userId = payload.TryGetProperty("userId", out var userElement) &&
            userElement.ValueKind == JsonValueKind.String
            ? userElement.GetString()
            : null;

        // 🚨 Dropped rather than thrown on: AmbientRunAttributionScope.Begin
        // REJECTS an over-long identity, and a job payload is durable data
        // that may have been written by an older build or by hand. Letting it
        // throw would fail the job on every one of its retries and never
        // recover. Dropping it leaves the run unattributed, which with
        // ownership on means the session write is refused - fail-closed, and
        // for a stated reason.
        if (RunLabels.ValidateUserId(userId) is not null)
        {
            userId = null;
        }

        // Absent on a job enqueued before this field existed; that job keeps
        // the default instructions it always had. A culture only selects among
        // the agent's own instructions, so a malformed one is dropped, not
        // thrown on: the worst it does is fall back to the default text.
        var culture = payload.TryGetProperty("culture", out var cultureElement) &&
            cultureElement.ValueKind == JsonValueKind.String &&
            cultureElement.GetString() is { Length: > 0 } cultureValue
            ? cultureValue
            : null;

        return (runId, message, sessionId, userId, culture);
    }

    /// <summary>The version and experiment arm a queued run runs and is recorded under.</summary>
    /// <param name="Version">The pinned definition version; <see langword="null"/> for the current one.</param>
    /// <param name="ExperimentId">The experiment the run counts in, if any.</param>
    /// <param name="Variant">The experiment variant the run counts in, if any.</param>
    private readonly record struct QueuedRunArm(int? Version, Guid? ExperimentId, string? Variant);
}
