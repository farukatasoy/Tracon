using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The default <see cref="IJobDispatcher"/>: validates the request against
/// the registered handler keys, then writes it to <see cref="IJobStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The two things <see cref="IJobStore.EnqueueAsync"/> cannot do on its own —
/// filling in the persistence fields (identifier, status, timestamps) and
/// refusing a key no handler serves. The store surface is unchanged; this
/// type sits in front of it.
/// </para>
/// <para>
/// For the built-in workflow and agent batch keys it also carries the caller's
/// attribution into the job: the worker sees no request and no ambient
/// scope of the caller, so the user and labels
/// <see cref="IRunAttributionContext"/> resolves at the time of the call are
/// written into the payload's envelope (<see cref="JobPayload"/>). A payload
/// that already names an attribution keeps it.
/// </para>
/// </remarks>
internal sealed class JobDispatcher(
    IJobStore jobStore,
    JobHandlerRegistry registry,
    IOptionsMonitor<TraconSchedulingOptions> schedulingOptions,
    TimeProvider? timeProvider = null,
    IRunAttributionContext? attributionContext = null) : IJobDispatcher
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async ValueTask<JobRecord> EnqueueAsync(
        JobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            throw new ArgumentException("The job request must name a tenant.", nameof(request));
        }

        if (request.TargetName is null)
        {
            throw new ArgumentException(
                "The job request must carry a target name; a handler that needs none passes an empty string.",
                nameof(request));
        }

        if (!registry.IsRegistered(request.HandlerKey))
        {
            throw new ArgumentException(
                $"No job handler is registered for the key '{request.HandlerKey}'. Register one with " +
                $"AddJobHandler<T>(\"{request.HandlerKey}\") before queueing work for it.",
                nameof(request));
        }

        // The worker arms this value as the attempt's deadline, and a
        // timer refuses a value outside its range where it is armed - in the worker,
        // long after this call returned.
        if (request.MaxDuration is { } maxDuration && !TimerPeriod.IsValid(maxDuration))
        {
            throw new ArgumentException(
                TimerPeriod.Failure($"{nameof(JobRequest)}.{nameof(JobRequest.MaxDuration)}", maxDuration),
                nameof(request));
        }

        var content = JobPayload.Read(request.HandlerKey, request.Payload);

        if (content.Problem is { } problem)
        {
            throw new ArgumentException(problem, nameof(request));
        }

        var items = request.Items.Count > 0
            ? request.Items
            : content.Items;

        var maxItems = schedulingOptions.CurrentValue.MaxItemsPerJob;

        if (items.Count > maxItems)
        {
            throw new ArgumentException(
                $"The request has {items.Count} items; at most {maxItems} are supported.",
                nameof(request));
        }

        var payload = request.Payload;

        if (JobPayload.CarriesAttribution(request.HandlerKey))
        {
            // Read HERE, while the caller's request or ambient scope is
            // still current; the worker that runs the job has neither. Through
            // RunAttributionReader, so a faulty consumer implementation drops
            // the attribution instead of failing the enqueue.
            var (userId, labels) = content.HasAttribution
                ? (content.UserId, content.Labels)
                : RunAttributionReader.Read(attributionContext);

            if (userId is not null || labels is { Count: > 0 })
            {
                payload = JobPayload.Envelope(items, userId, labels);
            }
        }

        var now = _clock.GetUtcNow();

        return await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = request.TenantId,
                HandlerKey = request.HandlerKey,
                Lane = request.Lane,
                TargetName = request.TargetName,
                Status = JobStatus.Pending,
                Payload = payload,
                MaxAttempts = request.MaxAttempts,
                MaxDuration = request.MaxDuration,
                ScheduledFor = request.ScheduledFor ?? now,
                CreatedAt = now,
            },
            items,
            cancellationToken).ConfigureAwait(false);
    }
}
