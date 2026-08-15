using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Schedule definitions, manual triggering, and job queue viewing endpoints (Phase 17).
/// </summary>
/// <remarks>
/// 🚨 All dependencies other than <see cref="IJobScheduleStore"/>/<see cref="IJobStore"/>
/// are marked <strong>explicitly</strong> with <c>[FromServices]</c> — the rationale
/// is the same as <see cref="WorkflowEndpoints"/>. Unlike Workflow, there is no
/// optional engine here: the queue and schedule stores are always registered
/// (K-018), so the <c>501</c> pattern is not needed.
/// </remarks>
internal static class SchedulingEndpoints
{
    /// <summary>Maps the scheduling and job endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/schedules", ListSchedulesAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismListSchedules")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Lists a tenant's schedules.");

        builder.MapGet("/api/schedules/{name}", GetScheduleAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismGetSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Gets a single schedule.");

        builder.MapPut("/api/schedules/{name}", SaveScheduleAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismSaveSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Creates or updates a schedule.")
            .Accepts<JobScheduleSaveRequest>("application/json")
            .WithDescription(
                "The cron expression and time zone are validated here; the next run " +
                "time is computed at save time. The payload cannot exceed the MaxItemsPerJob limit.");

        builder.MapDelete("/api/schedules/{name}", DeleteScheduleAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismDeleteSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Deletes a schedule.");

        builder.MapPost("/api/schedules/{name}/trigger", TriggerScheduleAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismTriggerSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Runs a schedule immediately, without waiting for the cron schedule.")
            .Accepts<JobTriggerRequest>(true, "application/json");

        builder.MapGet("/api/jobs", ListJobsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismListJobs")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Lists jobs, filtered by kind, status, or schedule.");

        builder.MapGet("/api/jobs/{id:guid}", GetJobAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismGetJob")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Gets a job and its items.");

        builder.MapPost("/api/jobs/{id:guid}/cancel", CancelJobAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismCancelJob")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Cancels a job.")
            .WithDescription(
                "Only a job in the Pending, Leased, or Running status can be canceled. " +
                "The executing worker checks the cancellation request between items and stops cooperatively.");
    }

    private static async Task<Ok<IReadOnlyList<JobSchedule>>> ListSchedulesAsync(
        [FromServices] IJobScheduleStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var schedules = await store.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(schedules);
    }

    private static async Task<Results<Ok<JobSchedule>, ProblemHttpResult>> GetScheduleAsync(
        string name,
        [FromServices] IJobScheduleStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var schedule = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        return schedule is null ? ScheduleNotFound(name) : TypedResults.Ok(schedule);
    }

    private static async Task<Results<Ok<JobSchedule>, ProblemHttpResult>> SaveScheduleAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IJobScheduleStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IOptionsMonitor<AgentPrismSchedulingOptions> schedulingOptions,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<JobScheduleSaveRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (string.IsNullOrWhiteSpace(request.TargetName))
        {
            return InvalidSchedule("'targetName' is required.");
        }

        TimeZoneInfo timeZone;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return InvalidSchedule($"'{request.TimeZone}' is not a valid time zone.");
        }

        DateTimeOffset? nextRunAt = null;

        if (request.Cron is { Length: > 0 } cron)
        {
            if (!CronExpression.TryParse(cron, out var parsed))
            {
                return InvalidSchedule($"'{cron}' does not match the supported five-field cron subset.");
            }

            nextRunAt = parsed!.GetNextOccurrence(DateTimeOffset.UtcNow, timeZone);
        }

        var itemCount = JobPayload.ExtractItems(request.Payload).Count;
        var maxItems = schedulingOptions.CurrentValue.MaxItemsPerJob;

        if (itemCount > maxItems)
        {
            return InvalidSchedule($"The payload has {itemCount} items; at most {maxItems} are supported.");
        }

        var existing = await store.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var schedule = new JobSchedule
        {
            Id = existing?.Id ?? Guid.Empty,
            TenantId = tenants.TenantId,
            Name = name,
            Kind = request.Kind,
            TargetName = request.TargetName,
            Cron = request.Cron,
            TimeZone = request.TimeZone,
            Payload = request.Payload,
            Enabled = request.Enabled,
            NextRunAt = nextRunAt,
            LastRunAt = existing?.LastRunAt,
            CreatedBy = existing?.CreatedBy,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        var saved = await store.SaveAsync(schedule, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteScheduleAsync(
        string name,
        [FromServices] IJobScheduleStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
        => await store.DeleteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : ScheduleNotFound(name);

    private static async Task<Results<Ok<JobRecord>, ProblemHttpResult>> TriggerScheduleAsync(
        string name,
        HttpContext httpContext,
        [FromServices] IJobScheduleStore scheduleStore,
        [FromServices] IJobStore jobStore,
        [FromServices] ITenantContext tenants,
        [FromServices] IOptionsMonitor<AgentPrismSchedulingOptions> schedulingOptions,
        CancellationToken cancellationToken)
    {
        var (request, bindError) = await RequestBodyBinding
            .ReadOptionalAsync<JobTriggerRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var schedule = await scheduleStore.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

        if (schedule is null)
        {
            return ScheduleNotFound(name);
        }

        var payload = request?.Payload ?? schedule.Payload;
        var items = JobPayload.ExtractItems(payload);
        var maxItems = schedulingOptions.CurrentValue.MaxItemsPerJob;

        if (items.Count > maxItems)
        {
            return TypedResults.Problem(
                title: "Trigger failed",
                detail: $"The payload has {items.Count} items; at most {maxItems} are supported.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var now = DateTimeOffset.UtcNow;

        var job = await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = AgentPrismId.NewId(),
                TenantId = tenants.TenantId,
                ScheduleId = schedule.Id,
                Kind = schedule.Kind,
                TargetName = schedule.TargetName,
                Status = JobStatus.Pending,
                Payload = payload,
                ScheduledFor = now,
                CreatedAt = now,
            },
            items,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(job);
    }

    private static async Task<Ok<IReadOnlyList<JobRecord>>> ListJobsAsync(
        [FromServices] IJobStore store,
        [FromServices] ITenantContext tenants,
        [FromQuery] JobKind? kind,
        [FromQuery] JobStatus? status,
        [FromQuery] Guid? scheduleId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var jobs = await store.QueryAsync(
            new JobQuery
            {
                TenantId = tenants.TenantId,
                Kind = kind,
                Status = status,
                ScheduleId = scheduleId,
                Skip = skip ?? 0,
                Take = take ?? 50,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(jobs);
    }

    private static async Task<Results<Ok<JobDetailResponse>, ProblemHttpResult>> GetJobAsync(
        Guid id,
        [FromServices] IJobStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var job = await store.GetAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return JobNotFound(id);
        }

        var items = await store.ListItemsAsync(id, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new JobDetailResponse { Job = job, Items = items });
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> CancelJobAsync(
        Guid id,
        [FromServices] IJobStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        if (await store.CancelAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NoContent();
        }

        // The cancellation failed: either the job does not exist or it is already
        // in a status that cannot be canceled (Completed/Failed/Cancelled). Reading
        // the record again to distinguish between the two leaves a meaningful
        // difference between "not found" (404) and "status not eligible" (409).
        var job = await store.GetAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);

        return job is null
            ? JobNotFound(id)
            : TypedResults.Problem(
                title: "Job could not be canceled",
                detail: $"The job is already in status '{job.Status}'.",
                statusCode: StatusCodes.Status409Conflict);
    }

    private static ProblemHttpResult InvalidSchedule(string detail)
        => TypedResults.Problem(title: "Schedule invalid", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult ScheduleNotFound(string name)
        => TypedResults.Problem(
            title: "Schedule not found",
            detail: $"There is no schedule named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult JobNotFound(Guid id)
        => TypedResults.Problem(
            title: "Job not found",
            detail: $"There is no job with id '{id}'.",
            statusCode: StatusCodes.Status404NotFound);
}
