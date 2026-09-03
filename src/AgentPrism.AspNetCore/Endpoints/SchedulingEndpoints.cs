using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Schedule definitions, manual triggering, and job queue viewing endpoints.
/// </summary>
/// <remarks>
/// All dependencies other than <see cref="IJobScheduleStore"/>/<see cref="IJobStore"/>
/// are marked <strong>explicitly</strong> with <c>[FromServices]</c> — the rationale
/// is the same as <see cref="WorkflowEndpoints"/>. Unlike Workflow, there is no
/// optional engine here: the queue and schedule stores are always registered,
/// so the <c>501</c> pattern is not needed.
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
            .WithSummary("Lists a tenant's schedules.")
            .WithDescription(
                "Enabled and disabled schedules are returned together; 'enabled' tells them " +
                "apart. Each entry carries 'nextRunAt' as computed at the last save and " +
                "'lastRunAt' from the last execution, which is the quickest way to see that a " +
                "schedule has stopped firing. A schedule with no cron expression never fires on " +
                "its own and exists only to be triggered by hand.");

        builder.MapGet("/api/schedules/{name}", GetScheduleAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismGetSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Gets a single schedule.")
            .WithDescription(
                "The response is the definition, including the stored payload the schedule fires " +
                "with; the jobs it produced are read from the job endpoints, filtered by this " +
                "schedule's id. Names are scoped to the calling tenant, and an unknown name " +
                "returns 404.");

        builder.MapPut("/api/schedules/{name}", SaveScheduleAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismSaveSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Creates or updates a schedule.")
            .Accepts<JobScheduleSaveRequest>("application/json")
            .WithDescription(
                "The cron expression, time zone, and lane are validated here; the next run " +
                "time is computed at save time. The payload cannot exceed the MaxItemsPerJob limit. " +
                "'lane' defaults to 'default' and every job this schedule produces — cron-dispatched " +
                "or manually triggered — inherits it.");

        builder.MapGet("/api/schedules/handler-keys", ListHandlerKeysAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismListSchedulableHandlerKeys")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Lists the handler keys a schedule may be created for.")
            .WithDescription(
                "The allow-list PUT /api/schedules/{name} enforces, so a client can offer exactly " +
                "the keys that will be accepted rather than guessing. It is AgentPrism's own " +
                "built-in keys unless the host set " +
                "AgentPrismSchedulingOptions.HttpSchedulableHandlerKeys, and it is NOT the full " +
                "set of registered handlers: a handler with no entry here runs jobs queued in " +
                "process but cannot be scheduled from outside. Admin only — a consumer's key " +
                "names are deployment detail, so this list is deliberately not on the " +
                "unauthenticated meta endpoint.");

        builder.MapDelete("/api/schedules/{name}", DeleteScheduleAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismDeleteSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Deletes a schedule.")
            .WithDescription(
                "The schedule stops firing, but jobs it already queued are not withdrawn — " +
                "cancel those individually if they must not run. Job history keeps pointing at " +
                "the deleted schedule's id, so past executions stay traceable. To pause a " +
                "schedule instead, save it with 'enabled: false'. An unknown name returns 404.");

        builder.MapPost("/api/schedules/{name}/trigger", TriggerScheduleAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismTriggerSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Runs a schedule immediately, without waiting for the cron schedule.")
            .WithDescription(
                "The job is queued, not executed inline: the response is the queued job record, " +
                "so poll the job endpoint for the outcome. The body is optional — without one " +
                "the schedule's stored payload is used, and a body's payload overrides it for " +
                "this run only without changing the schedule. A trigger fires even when the " +
                "schedule is disabled, and it does not move 'nextRunAt'. The payload's item " +
                "count is capped by the same limit that applies on save.")
            .Accepts<JobTriggerRequest>(true, "application/json");

        builder.MapGet("/api/jobs", ListJobsAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismListJobs")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Lists jobs, filtered by handler key, status, lane, or schedule.")
            .WithDescription(
                "Every queued unit of work shares this queue — scheduled runs, retention " +
                "cleanups, webhook deliveries, and queued agent runs — so filter by 'handlerKey' to " +
                "narrow it. 'scheduleId' returns the executions of one schedule. 'lane' returns " +
                "only the jobs queued under that lane — the way to see whether a lane nobody's " +
                "worker subscribes to is quietly piling up. Job items are not included here; read " +
                "them from the single-job endpoint. Paging is offset based, with 'skip' defaulting " +
                "to 0 and 'take' to 50.");

        builder.MapGet("/api/jobs/{id:guid}", GetJobAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismGetJob")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Gets a job and its items.")
            .WithDescription(
                "This is the endpoint to poll after queuing work: it carries the job's status " +
                "and attempt count together with its items, each with its own status, so partial " +
                "progress is visible while the job is still running. A failed job keeps its " +
                "error text here rather than only in the logs. An unknown id, or one belonging " +
                "to another tenant, returns 404.");

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

    private static Ok<IReadOnlyList<string>> ListHandlerKeysAsync(
        [FromServices] IOptionsMonitor<AgentPrismSchedulingOptions> schedulingOptions)
    {
        var allowed = schedulingOptions.CurrentValue.HttpSchedulableHandlerKeys;

        return TypedResults.Ok<IReadOnlyList<string>>(
            allowed.Count == 0 ? [.. JobHandlerKeys.BuiltIn] : [.. allowed]);
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

        // 🚨 A handler key is a DISPATCH identity, so an unrestricted endpoint
        // would turn every registered handler -- including the internal ones a
        // consumer registered for its own background work -- into an externally
        // callable surface. Only an explicitly allowed key gets through; the
        // default list is AgentPrism's own built-in keys.
        if (!IsSchedulableOverHttp(request.HandlerKey, schedulingOptions.CurrentValue))
        {
            return InvalidSchedule(
                $"'{request.HandlerKey}' cannot be scheduled over HTTP. Add it to " +
                "AgentPrismSchedulingOptions.HttpSchedulableHandlerKeys to allow it.");
        }

        var lane = request.Lane ?? JobLanes.Default;

        if (!JobLanes.IsValidName(lane))
        {
            return InvalidSchedule(
                $"'{lane}' is not a valid lane name. A lane name must be 1-64 characters: lowercase " +
                "ASCII letters, digits, '.', '_', or '-', starting with a letter or digit.");
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
            HandlerKey = request.HandlerKey,
            Lane = lane,
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
                HandlerKey = schedule.HandlerKey,
                Lane = schedule.Lane,
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
        [FromQuery] string? handlerKey,
        [FromQuery] JobStatus? status,
        [FromQuery] Guid? scheduleId,
        [FromQuery] string? lane,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var jobs = await store.QueryAsync(
            new JobQuery
            {
                TenantId = tenants.TenantId,
                HandlerKey = handlerKey,
                Status = status,
                ScheduleId = scheduleId,
                Lane = lane,
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

    /// <summary>
    /// Whether <paramref name="handlerKey"/> may be scheduled through the HTTP
    /// endpoint.
    /// </summary>
    /// <param name="handlerKey">The key from the request body.</param>
    /// <param name="options">The scheduling settings.</param>
    /// <returns><see langword="true"/> if the key is allowed.</returns>
    /// <remarks>
    /// An empty <see cref="AgentPrismSchedulingOptions.HttpSchedulableHandlerKeys"/>
    /// means the built-in keys only, which is exactly what the endpoint
    /// accepted before handler keys existed — an upgraded deployment keeps
    /// working with no configuration change.
    /// </remarks>
    private static bool IsSchedulableOverHttp(string? handlerKey, AgentPrismSchedulingOptions options)
    {
        if (handlerKey is not { Length: > 0 })
        {
            return false;
        }

        var allowed = options.HttpSchedulableHandlerKeys;

        return allowed.Count == 0
            ? JobHandlerKeys.BuiltIn.Contains(handlerKey, StringComparer.Ordinal)
            : allowed.Contains(handlerKey, StringComparer.Ordinal);
    }

}
