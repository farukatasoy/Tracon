using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>The outcome of validating an inbound trigger request.</summary>
public enum InboundTriggerOutcome
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
public sealed record InboundTriggerValidationResult
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
public sealed record InboundTriggerValidatedRequest
{
    /// <summary>The trigger definition the request matched.</summary>
    public required InboundTrigger Trigger { get; init; }

    /// <summary>The message extracted from the request body.</summary>
    public required string Message { get; init; }

    /// <summary>The idempotency key this request reserved (the request's signature).</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>The result of <see cref="InboundTriggerDispatcher.EnqueueAsync"/>.</summary>
public sealed record InboundTriggerDispatchResult
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

    /// <summary>Creates a new dispatcher.</summary>
    public InboundTriggerDispatcher(
        IInboundTriggerStore triggerStore,
        InboundTriggerSecretResolver secretResolver,
        InboundTriggerRateLimiter rateLimiter,
        IIdempotencyStore idempotencyStore,
        IJobStore jobStore,
        IRunStore runStore,
        IOptionsMonitor<TraconInboundTriggerOptions> options,
        TimeProvider? timeProvider = null)
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

        if (!_rateLimiter.TryAcquire(trigger.Id))
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
    public async ValueTask<InboundTriggerDispatchResult> EnqueueAsync(
        InboundTriggerValidatedRequest validated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validated);

        var trigger = validated.Trigger;
        var now = _clock.GetUtcNow();

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
                },
                cancellationToken).ConfigureAwait(false);

            var job = await _jobStore.EnqueueAsync(
                new JobRecord
                {
                    Id = runId,
                    TenantId = trigger.TenantId,
                    HandlerKey = JobHandlerKeys.AgentRun,
                    TargetName = trigger.TargetName,
                    Status = JobStatus.Pending,
                    Payload = BuildAgentRunPayload(runId, validated.Message, trigger.Name),
                    ScheduledFor = now,
                    CreatedAt = now,
                },
                [],
                cancellationToken).ConfigureAwait(false);

            return new InboundTriggerDispatchResult { RunId = runId, JobId = job.Id };
        }

        var workflowJob = await _jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = trigger.TenantId,
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = trigger.TargetName,
                Status = JobStatus.Pending,
                Payload = JsonSerializer.SerializeToElement(validated.Message, TraconCoreJsonContext.Default.String),
                ScheduledFor = now,
                CreatedAt = now,
            },
            [validated.Message],
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
    private static JsonElement BuildAgentRunPayload(Guid runId, string message, string triggerName)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("runId", runId);
            writer.WriteString("message", message);
            writer.WriteString("trigger", triggerName);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());

        return document.RootElement.Clone();
    }
}
