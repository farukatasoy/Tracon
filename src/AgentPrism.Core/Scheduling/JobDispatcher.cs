using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// The default <see cref="IJobDispatcher"/>: validates the request against
/// the registered handler keys, then writes it to <see cref="IJobStore"/>.
/// </summary>
/// <remarks>
/// The two things <see cref="IJobStore.EnqueueAsync"/> cannot do on its own —
/// filling in the persistence fields (identifier, status, timestamps) and
/// refusing a key no handler serves. The store surface is unchanged; this
/// type sits in front of it.
/// </remarks>
internal sealed class JobDispatcher(
    IJobStore jobStore,
    JobHandlerRegistry registry,
    IOptionsMonitor<AgentPrismSchedulingOptions> schedulingOptions,
    TimeProvider? timeProvider = null) : IJobDispatcher
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

        var items = request.Items.Count > 0
            ? request.Items
            : JobPayload.ExtractItems(request.Payload);

        var maxItems = schedulingOptions.CurrentValue.MaxItemsPerJob;

        if (items.Count > maxItems)
        {
            throw new ArgumentException(
                $"The request has {items.Count} items; at most {maxItems} are supported.",
                nameof(request));
        }

        var now = _clock.GetUtcNow();

        return await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = AgentPrismId.NewId(),
                TenantId = request.TenantId,
                HandlerKey = request.HandlerKey,
                Lane = request.Lane,
                TargetName = request.TargetName,
                Status = JobStatus.Pending,
                Payload = request.Payload,
                MaxAttempts = request.MaxAttempts,
                ScheduledFor = request.ScheduledFor ?? now,
                CreatedAt = now,
            },
            items,
            cancellationToken).ConfigureAwait(false);
    }
}
