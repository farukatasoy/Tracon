using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>The outcome of validating an inbound trigger request.</summary>
internal enum InboundTriggerOutcome
{
    /// <summary>The request is valid; <see cref="InboundTriggerValidationResult.Validated"/> is populated.</summary>
    Valid = 0,

    /// <summary>
    /// There is no trigger for this tenant + name, it is disabled, the
    /// signature is missing or does not match, or the timestamp is outside
    /// the tolerance window. Maps to <c>401</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately ONE outcome for all five cases ("the
    /// response does not distinguish 'trigger missing' from 'wrong
    /// signature' — the same body and code in both"). An unknown tenant,
    /// an unknown or disabled trigger name, and every signature
    /// failure produce the IDENTICAL generic response; a caller without a
    /// valid secret must not be able to enumerate which trigger names exist
    /// by comparing responses.
    /// </remarks>
    Unauthorized = 1,

    /// <summary>The trigger's per-minute request limit was exceeded. Maps to <c>429</c>.</summary>
    RateLimited = 2,

    /// <summary>The same signature was already accepted. Maps to <c>409</c>.</summary>
    Replayed = 3,

    /// <summary>The body is not valid JSON, or the payload path did not resolve. Maps to <c>400</c>.</summary>
    InvalidPayload = 4,
}

/// <summary>The result of <see cref="InboundTriggerDispatcher.ValidateAsync"/>.</summary>
internal sealed record InboundTriggerValidationResult
{
    /// <summary>The validation outcome.</summary>
    public required InboundTriggerOutcome Outcome { get; init; }

    /// <summary>
    /// A human-readable detail for <see cref="InboundTriggerOutcome.InvalidPayload"/>;
    /// otherwise <see langword="null"/>.
    /// </summary>
    public string? ErrorDetail { get; init; }

    /// <summary>Populated only when <see cref="Outcome"/> is <see cref="InboundTriggerOutcome.Valid"/>.</summary>
    public InboundTriggerValidatedRequest? Validated { get; init; }
}

/// <summary>A request that passed every check and is ready to be queued.</summary>
internal sealed record InboundTriggerValidatedRequest
{
    /// <summary>The trigger definition the request matched.</summary>
    public required InboundTrigger Trigger { get; init; }

    /// <summary>The message extracted from the request body.</summary>
    public required string Message { get; init; }

    /// <summary>The idempotency key this request reserved (the request's signature).</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>The result of <see cref="InboundTriggerDispatcher.EnqueueAsync"/>.</summary>
internal sealed record InboundTriggerDispatchResult
{
    /// <summary>
    /// The identifier of the <c>runs</c> row, when the target is an agent —
    /// known synchronously because the row is written before the job runs
    /// (the same pattern as the queued agent runs). <see langword="null"/>
    /// for a workflow target: a workflow job is not tied to a single run id
    /// until an engine actually picks it up.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>The identifier of the queued job.</summary>
    public required Guid JobId { get; init; }
}

/// <summary>
/// Validates and dispatches inbound trigger requests: HMAC signature and
/// timestamp window, replay protection, payload extraction, and queuing.
/// </summary>
/// <remarks>
/// <para>
/// Host-agnostic by design: this type has no dependency on ASP.NET Core.
/// The HTTP layer (<c>TriggerEndpoints</c>) owns request body size bounding
/// and the quota check (<c>QuotaGate</c>) — both are
/// naturally HTTP-shaped concerns (a <c>413</c>/<c>429</c> response with
/// specific headers) and sit BETWEEN <see cref="ValidateAsync"/> and
/// <see cref="EnqueueAsync"/>.
/// </para>
/// <para>
/// Replay protection reuses <see cref="IIdempotencyStore"/>
/// keyed by the request's own signature, not a client-supplied
/// <c>Idempotency-Key</c>. The reservation is deliberately never completed
/// on success: leaving it <see cref="IdempotencyState.Reserved"/> forever
/// makes every later replay of the same signature report
/// <see cref="IdempotencyState.InProgress"/>, which this type maps to
/// <see cref="InboundTriggerOutcome.Replayed"/> — simpler than tracking and
/// replaying a stored response, and correct here because a replayed webhook
/// delivery must always be rejected, never re-served (unlike the general
/// idempotency-key contract, which intentionally replays the original response).
/// </para>
/// </remarks>
internal sealed class InboundTriggerDispatcher
{
    private readonly IInboundTriggerStore _triggerStore;
    private readonly InboundTriggerSecretResolver _secretResolver;
    private readonly InboundTriggerRateLimiter _rateLimiter;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IJobStore _jobStore;
    private readonly IRunStore _runStore;
    private readonly IOptionsMonitor<TraconInboundTriggerOptions> _options;
    private readonly TimeProvider _clock;
    private readonly IRunAttributionContext? _attributionContext;
    private readonly ILogger _logger;

    /// <summary>Creates a new dispatcher.</summary>
    /// <param name="triggerStore">The trigger definitions.</param>
    /// <param name="secretResolver">Resolves a trigger's signing secret.</param>
    /// <param name="rateLimiter">The per-trigger request limit.</param>
    /// <param name="idempotencyStore">The replay protection store.</param>
    /// <param name="jobStore">The queue the run is written to.</param>
    /// <param name="runStore">The store of the agent run's placeholder row.</param>
    /// <param name="options">The inbound trigger options.</param>
    /// <param name="timeProvider">The clock; the system clock when <see langword="null"/>.</param>
    /// <param name="attributionContext">
    /// Resolves the accept request's user and labels, which the queued run
    /// carries; <see langword="null"/> queues it unattributed.
    /// </param>
    /// <param name="logger">Logs a queue that refuses an agent run's job; a no-op logger when <see langword="null"/>.</param>
    public InboundTriggerDispatcher(
        IInboundTriggerStore triggerStore,
        InboundTriggerSecretResolver secretResolver,
        InboundTriggerRateLimiter rateLimiter,
        IIdempotencyStore idempotencyStore,
        IJobStore jobStore,
        IRunStore runStore,
        IOptionsMonitor<TraconInboundTriggerOptions> options,
        TimeProvider? timeProvider = null,
        IRunAttributionContext? attributionContext = null,
        ILogger<InboundTriggerDispatcher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(triggerStore);
        ArgumentNullException.ThrowIfNull(secretResolver);
        ArgumentNullException.ThrowIfNull(rateLimiter);
        ArgumentNullException.ThrowIfNull(idempotencyStore);
        ArgumentNullException.ThrowIfNull(jobStore);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(options);

        _triggerStore = triggerStore;
        _secretResolver = secretResolver;
        _rateLimiter = rateLimiter;
        _idempotencyStore = idempotencyStore;
        _jobStore = jobStore;
        _runStore = runStore;
        _options = options;
        _clock = timeProvider ?? TimeProvider.System;
        _attributionContext = attributionContext;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>
    /// Validates a request: trigger lookup, signature and timestamp, rate
    /// limit, replay protection, and payload extraction — in that order.
    /// </summary>
    /// <param name="tenantId">The tenant identifier, from the route.</param>
    /// <param name="name">The trigger name, from the route.</param>
    /// <param name="rawBody">The raw, already size-bounded request body.</param>
    /// <param name="timestampHeader">The <c>X-Tracon-Timestamp</c> header value.</param>
    /// <param name="signatureHeader">The <c>X-Tracon-Signature</c> header value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation result.</returns>
    public async ValueTask<InboundTriggerValidationResult> ValidateAsync(
        string tenantId,
        string name,
        string rawBody,
        string? timestampHeader,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(rawBody);

        var trigger = await _triggerStore.GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

        if (trigger is null || !trigger.Enabled)
        {
            return new InboundTriggerValidationResult { Outcome = InboundTriggerOutcome.Unauthorized };
        }

        // 🚨 Signature verification runs BEFORE the rate limit, not after: an
        // unsigned or wrongly signed request never reaches the queue this
        // limit protects, so counting it against the budget would let an
        // unauthenticated flood exhaust the legitimate sender's own allowance
        // — the opposite of what section 66.5 asks the limit to do. Only a
        // request that already proved it holds the secret can consume budget.
        if (!TryVerifySignature(trigger, rawBody, timestampHeader, signatureHeader))
        {
            return new InboundTriggerValidationResult { Outcome = InboundTriggerOutcome.Unauthorized };
        }

        if (!await _rateLimiter.TryAcquireAsync(trigger.Id, cancellationToken).ConfigureAwait(false))
        {
            return new InboundTriggerValidationResult { Outcome = InboundTriggerOutcome.RateLimited };
        }

        // 🚨 Replay protection is keyed by the signature ITSELF (Open Question
        // 5, option A): external systems do not reliably send their own event
        // id, but the signature is always present and unique per (body,
        // timestamp, secret) triple.
        var reservation = await _idempotencyStore.ReserveAsync(
            new IdempotencyRequest
            {
                TenantId = tenantId,
                Key = signatureHeader!,
                Fingerprint = "inbound-trigger",
                CreatedAt = _clock.GetUtcNow(),
            },
            cancellationToken).ConfigureAwait(false);

        if (reservation.State != IdempotencyState.Reserved)
        {
            return new InboundTriggerValidationResult { Outcome = InboundTriggerOutcome.Replayed };
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            await _idempotencyStore.ReleaseAsync(tenantId, signatureHeader!, cancellationToken).ConfigureAwait(false);

            return new InboundTriggerValidationResult
            {
                Outcome = InboundTriggerOutcome.InvalidPayload,
                ErrorDetail = "The request body is not valid JSON.",
            };
        }

        using (document)
        {
            if (!InboundTriggerPayloadReader.TryExtractMessage(
                    document.RootElement, trigger.PayloadMode, trigger.PayloadPath, out var message) ||
                string.IsNullOrWhiteSpace(message))
            {
                await _idempotencyStore.ReleaseAsync(tenantId, signatureHeader!, cancellationToken).ConfigureAwait(false);

                return new InboundTriggerValidationResult
                {
                    Outcome = InboundTriggerOutcome.InvalidPayload,
                    ErrorDetail = trigger.PayloadMode == InboundTriggerPayloadMode.Path
                        ? $"The payload path '{trigger.PayloadPath}' did not resolve to a value in the request body."
                        : "The request body cannot be empty.",
                };
            }

            return new InboundTriggerValidationResult
            {
                Outcome = InboundTriggerOutcome.Valid,
                Validated = new InboundTriggerValidatedRequest
                {
                    Trigger = trigger,
                    Message = message,
                    IdempotencyKey = signatureHeader!,
                },
            };
        }
    }

    /// <summary>Queues the validated request's run.</summary>
    /// <param name="validated">The result of a successful <see cref="ValidateAsync"/> call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The identifiers of the queued work.</returns>
    /// <remarks>
    /// Call it inside the accept request: the run carries the user and labels
    /// <see cref="IRunAttributionContext"/> resolves now, because the worker
    /// that runs it sees no request. The accept endpoint has already
    /// refused an attribution that breaks a limit (<c>RunAttributionGate</c>).
    /// </remarks>
    public async ValueTask<InboundTriggerDispatchResult> EnqueueAsync(
        InboundTriggerValidatedRequest validated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validated);

        var trigger = validated.Trigger;
        var now = _clock.GetUtcNow();

        // F-364: the endpoint checked this attribution before queuing (F-337)
        // and then dropped it; the queued run started with no user and no labels.
        var (userId, labels) = RunAttributionReader.Read(_attributionContext);

        if (trigger.TargetKind == InboundTriggerTargetKind.Agent)
        {
            var runId = TraconId.NewId();

            // Same placeholder-row pattern as phase 46's queued agent runs
            // (AgentEndpoints.RunQueuedAsync): the run id is promised to the
            // caller before the worker picks up the job.
            await _runStore.StartRunAsync(
                new RunStartInfo
                {
                    RunId = runId,
                    AgentName = trigger.TargetName,
                    Status = RunStatus.Queued,
                    StartedAt = now,
                    TenantId = trigger.TenantId,

                    // Captured here, as the queued agent run endpoint does: the
                    // worker's second StartRunAsync carries no user, and the
                    // store's upsert keeps these.
                    UserId = userId,
                    Labels = labels,
                },
                cancellationToken).ConfigureAwait(false);

            // F-432: a retry opens a new run id, so a row the refused job left
            // Queued would never be leased; the helper closes it.
            var job = await QueuedRunPlaceholder.EnqueueAsync(
                _jobStore,
                _runStore,
                new JobRecord
                {
                    Id = runId,
                    TenantId = trigger.TenantId,
                    HandlerKey = JobHandlerKeys.AgentRun,
                    TargetName = trigger.TargetName,
                    Status = JobStatus.Pending,
                    Payload = BuildAgentRunPayload(runId, validated.Message, trigger.Name, userId, labels),
                    ScheduledFor = now,
                    CreatedAt = now,
                },
                [],
                _logger,
                cancellationToken).ConfigureAwait(false);

            return new InboundTriggerDispatchResult { RunId = runId, JobId = job.Id };
        }

        // The body, or the field taken from it, is the sender's text. A bare item
        // would be read as a structured one when it looks like one, and the sender
        // would then choose the workflow's parameter values. The envelope keeps all
        // of it the message.
        var item = BatchItemInput.Envelope(validated.Message);

        // The attribution rides in the payload's envelope (JobPayload), which
        // WorkflowJobHandler replays. Without one the payload stays the message.
        var workflowPayload = userId is not null || labels is { Count: > 0 }
            ? JobPayload.Envelope([item], userId, labels)
            : JsonSerializer.SerializeToElement(validated.Message, TraconCoreJsonContext.Default.String);

        var workflowJob = await _jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = trigger.TenantId,
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = trigger.TargetName,
                Status = JobStatus.Pending,
                Payload = workflowPayload,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [item],
            cancellationToken).ConfigureAwait(false);

        return new InboundTriggerDispatchResult { RunId = null, JobId = workflowJob.Id };
    }

    /// <summary>
    /// Releases a reservation made by <see cref="ValidateAsync"/> when a
    /// later, HTTP-layer check (the quota gate) rejects the request — so a
    /// legitimate retry is not permanently mistaken for a replay.
    /// </summary>
    /// <param name="validated">The result of a successful <see cref="ValidateAsync"/> call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask ReleaseAsync(InboundTriggerValidatedRequest validated, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validated);

        return _idempotencyStore.ReleaseAsync(validated.Trigger.TenantId, validated.IdempotencyKey, cancellationToken);
    }

    private bool TryVerifySignature(InboundTrigger trigger, string rawBody, string? timestampHeader, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(timestampHeader) || string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        if (!long.TryParse(timestampHeader, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return false;
        }

        DateTimeOffset timestamp;

        try
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var tolerance = _options.CurrentValue.TimestampTolerance;
        var drift = (_clock.GetUtcNow() - timestamp).Duration();

        if (drift > tolerance)
        {
            return false;
        }

        string? secret;

        try
        {
            secret = _secretResolver.Resolve(trigger);
        }
        catch (TraconException)
        {
            // The signing secret's configuration key fell outside the allowed
            // prefix AFTER the trigger was saved (the setting changed since).
            // The caller cannot fix this; it reads the same as "wrong signature".
            return false;
        }

        return secret is not null && WebhookSigner.Verify(rawBody, timestamp, secret, signatureHeader);
    }

    /// <remarks>
    /// Built by hand with <see cref="Utf8JsonWriter"/> + <c>JsonDocument.Parse</c>
    /// rather than <c>JsonSerializer</c> — the same AOT-safe technique
    /// <c>SqlJobScheduleStore.ReadJsonb</c> uses, avoiding a source-gen entry
    /// for a one-off anonymous shape. The extra <c>trigger</c> field is
    /// ignored by <c>AgentRunJobHandler.ParsePayload</c>, which reads only
    /// <c>runId</c>/<c>message</c>/<c>sessionId</c>; it exists so
    /// <c>GET /api/jobs/{id}</c> shows which trigger queued the run.
    /// </remarks>
    private static JsonElement BuildAgentRunPayload(
        Guid runId,
        string message,
        string triggerName,
        string? userId,
        IReadOnlyDictionary<string, string>? labels)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("runId", runId);
            writer.WriteString("message", message);
            writer.WriteString("trigger", triggerName);

            // The field names the agent run job reads; AgentRunJobHandler
            // replays the user through AmbientRunAttributionScope for the
            // run's children.
            if (userId is not null)
            {
                writer.WriteString("userId", userId);
            }

            if (labels is { Count: > 0 })
            {
                writer.WriteStartObject("labels");

                foreach (var (key, value) in labels)
                {
                    writer.WriteString(key, value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());

        return document.RootElement.Clone();
    }
}
