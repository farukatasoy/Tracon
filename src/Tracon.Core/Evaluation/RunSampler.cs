using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Decides whether to sample a completed run for online evaluation and, when
/// sampled, queues a <see cref="JobHandlerKeys.OnlineEval"/> job.
/// </summary>
/// <remarks>
/// <para>
/// This class only <strong>writes to the queue</strong>. The actual judge call,
/// which spends money, runs in the background through <see cref="OnlineEvalJobHandler"/>.
/// Like <see cref="WebhookPublisher"/>, it slows the main run path only by a fast
/// queue write, so a slow or unreachable judge has no impact.
/// </para>
/// <para>
/// <see cref="RunRecordingAgent"/> wraps this call in <c>try/catch</c>. A sampling
/// failure does not affect the run, as required by the observability rule.
/// </para>
/// </remarks>
public sealed class RunSampler(
    IJobStore jobStore,
    IOptionsMonitor<OnlineEvaluationOptions> optionsMonitor,
    TimeProvider? timeProvider = null,
    ILogger<RunSampler>? logger = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, HourlyWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>Evaluates a run for sampling and queues it when required.</summary>
    /// <param name="request">The summary of the run to sample.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when a job was queued.</returns>
    public async ValueTask<bool> SampleAsync(RunSampleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled || options.SampleRate <= 0.0)
        {
            return false;
        }

        // Evaluation runs, including the judge's own calls, are synthetic traffic
        // and stop the infinite loop here. Failed runs belong to error classification;
        // a judge cannot score an error. Child runs do not reach here because the
        // caller, RunRecordingAgent, checks Depth == 0.
        if (AmbientSamplingSuppressionScope.IsSuppressed || request.Kind == RunKind.Eval || request.Status != RunStatus.Completed)
        {
            return false;
        }

        if (options.AgentNames.Count > 0 &&
            !options.AgentNames.Contains(request.AgentName, StringComparer.Ordinal))
        {
            return false;
        }

        if (!IsSampled(request.RunId, options.SampleRate))
        {
            return false;
        }

        if (!TryConsumeHourlyBudget(request.TenantId, options.MaxScoresPerHour))
        {
            return false;
        }

        try
        {
            var now = _clock.GetUtcNow();
            var runIdText = request.RunId.ToString();

            // 🚨 Payload must be assigned (K-166). Otherwise JsonElement remains
            // `default` and the complete /api/jobs list returns 500. It shares the
            // webhook-delivery job shape, string[], so a second JsonSerializerContext is unnecessary.
            var payload = JsonSerializer.SerializeToElement(
                new[] { runIdText },
                WebhookJobPayloadJsonContext.Default.StringArray);

            await jobStore.EnqueueAsync(
                new JobRecord
                {
                    Id = TraconId.NewId(),
                    TenantId = request.TenantId,
                    HandlerKey = JobHandlerKeys.OnlineEval,
                    TargetName = request.AgentName,
                    Status = JobStatus.Pending,
                    Payload = payload,
                    ScheduledFor = now,
                    CreatedAt = now,
                },
                [runIdText],
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "The online evaluation job could not be queued: run={RunId} tenant={TenantId}.",
                    request.RunId,
                    request.TenantId);
            }

            return false;
        }
    }

    /// <summary>
    /// Deterministically decides whether to sample a run from its identifier.
    /// </summary>
    /// <remarks>
    /// <see cref="HashCode"/> is deliberately not used because it produces a
    /// differently salted result for each process start and breaks the guarantee
    /// that the same run always receives the same decision. FNV-1a is process- and salt-independent.
    /// </remarks>
    private static bool IsSampled(Guid runId, double sampleRate)
    {
        if (sampleRate >= 1.0)
        {
            return true;
        }

        Span<byte> bytes = stackalloc byte[16];
        runId.TryWriteBytes(bytes);

        var hash = 14695981039346656037UL;

        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        // The upper 53 bits are the integer range a double can represent exactly.
        // They produce a uniformly distributed fraction in [0, 1).
        var fraction = (hash >> 11) * (1.0 / (1UL << 53));

        return fraction < sampleRate;
    }

    /// <summary>
    /// Consumes one unit from a tenant's sampling budget for the current hour.
    /// </summary>
    /// <remarks>
    /// This is an in-memory, fixed rather than sliding hourly window. It is a
    /// second defense against sampling-rate calculation errors, not a precise rate
    /// limiter. The budget resets after a process restart, which is accepted by the no-surprises rule:
    /// simplicity does not require a durable counter store.
    /// </remarks>
    private bool TryConsumeHourlyBudget(string tenantId, int maxPerHour)
    {
        if (maxPerHour <= 0)
        {
            return false;
        }

        var now = _clock.GetUtcNow();
        var windowStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var window = _windows.GetOrAdd(tenantId, static _ => new HourlyWindow());

        lock (window)
        {
            if (window.WindowStart != windowStart)
            {
                window.WindowStart = windowStart;
                window.Count = 0;
            }

            if (window.Count >= maxPerHour)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    private sealed class HourlyWindow
    {
        public DateTimeOffset WindowStart;
        public int Count;
    }
}

/// <summary>Represents the run summary required for a sampling decision.</summary>
public sealed record RunSampleRequest
{
    /// <summary>Gets the run identifier.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the executed agent name.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the run kind. <see cref="RunKind.Eval"/> is never sampled.</summary>
    public required RunKind Kind { get; init; }

    /// <summary>Gets the final status. Only <see cref="RunStatus.Completed"/> is sampled.</summary>
    public required RunStatus Status { get; init; }
}
