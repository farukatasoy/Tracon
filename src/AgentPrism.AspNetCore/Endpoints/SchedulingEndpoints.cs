using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Zamanlama tanimlari, elle tetikleme ve is kuyrugu goruntuleme uclari (Faz 17).
/// </summary>
/// <remarks>
/// 🚨 <see cref="IJobScheduleStore"/>/<see cref="IJobStore"/> disindaki tum
/// bagimliliklar <c>[FromServices]</c> ile <strong>acikca</strong> isaretlenir —
/// gerekce <see cref="WorkflowEndpoints"/> ile aynidir. Workflow'un aksine
/// burada opsiyonel bir motor yoktur: kuyruk ve zamanlama depolari her zaman
/// kayitlidir (K-018), bu yuzden <c>501</c> deseni gerekmez.
/// </remarks>
internal static class SchedulingEndpoints
{
    /// <summary>Zamanlama ve is uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/schedules", ListSchedulesAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismListSchedules")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Bir kiracinin zamanlamalarini listeler.");

        builder.MapGet("/api/schedules/{name}", GetScheduleAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismGetSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Tek bir zamanlamayi getirir.");

        builder.MapPut("/api/schedules/{name}", SaveScheduleAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismSaveSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Zamanlama olusturur veya gunceller.")
            .WithDescription(
                "Cron ifadesi ve saat dilimi burada dogrulanir; bir sonraki calisma " +
                "zamani kayit aninda hesaplanir. Yuk MaxItemsPerJob sinirini asamaz.");

        builder.MapDelete("/api/schedules/{name}", DeleteScheduleAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismDeleteSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Bir zamanlamayi siler.");

        builder.MapPost("/api/schedules/{name}/trigger", TriggerScheduleAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismTriggerSchedule")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Bir zamanlamayi hemen, cron beklemeden calistirir.");

        builder.MapGet("/api/jobs", ListJobsAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismListJobs")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Isleri turune, durumuna veya zamanlamasina gore filtreleyerek listeler.");

        builder.MapGet("/api/jobs/{id:guid}", GetJobAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismGetJob")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Bir isi ve ogelerini getirir.");

        builder.MapPost("/api/jobs/{id:guid}/cancel", CancelJobAsync)
            .RequireRole(roles.Operator)
            .WithName("AgentPrismCancelJob")
            .WithTags("AgentPrism", "Scheduling")
            .WithSummary("Bir isi iptal eder.")
            .WithDescription(
                "Yalnizca Pending, Leased veya Running durumundaki bir is iptal edilebilir. " +
                "Yurutucu isci ogeler arasinda iptal talebini kontrol eder ve isbirlikci sekilde durur.");
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
        [FromBody] JobScheduleSaveRequest request,
        [FromServices] IJobScheduleStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IOptionsMonitor<AgentPrismSchedulingOptions> schedulingOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TargetName))
        {
            return InvalidSchedule("'targetName' alani zorunludur.");
        }

        TimeZoneInfo timeZone;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return InvalidSchedule($"'{request.TimeZone}' gecerli bir saat dilimi degil.");
        }

        DateTimeOffset? nextRunAt = null;

        if (request.Cron is { Length: > 0 } cron)
        {
            if (!CronExpression.TryParse(cron, out var parsed))
            {
                return InvalidSchedule($"'{cron}' desteklenen bes alanli cron alt kumesiyle eslesmiyor.");
            }

            nextRunAt = parsed!.GetNextOccurrence(DateTimeOffset.UtcNow, timeZone);
        }

        var itemCount = JobPayload.ExtractItems(request.Payload).Count;
        var maxItems = schedulingOptions.CurrentValue.MaxItemsPerJob;

        if (itemCount > maxItems)
        {
            return InvalidSchedule($"Yuk {itemCount} oge tasiyor; en fazla {maxItems} oge desteklenir.");
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
        [FromBody] JobTriggerRequest? request,
        [FromServices] IJobScheduleStore scheduleStore,
        [FromServices] IJobStore jobStore,
        [FromServices] ITenantContext tenants,
        [FromServices] IOptionsMonitor<AgentPrismSchedulingOptions> schedulingOptions,
        CancellationToken cancellationToken)
    {
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
                title: "Tetikleme basarisiz",
                detail: $"Yuk {items.Count} oge tasiyor; en fazla {maxItems} oge desteklenir.",
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

        // Iptal basarisiz oldu: ya is yok ya da zaten iptal edilemeyecek bir
        // durumda (Completed/Failed/Cancelled). Ikisini ayirt etmek icin kaydi
        // tekrar okumak, "bulunamadi" (404) ile "durum uygun degil" (409)
        // arasinda anlamli bir fark birakir.
        var job = await store.GetAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);

        return job is null
            ? JobNotFound(id)
            : TypedResults.Problem(
                title: "Is iptal edilemedi",
                detail: $"Is zaten '{job.Status}' durumunda.",
                statusCode: StatusCodes.Status409Conflict);
    }

    private static ProblemHttpResult InvalidSchedule(string detail)
        => TypedResults.Problem(title: "Zamanlama gecersiz", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult ScheduleNotFound(string name)
        => TypedResults.Problem(
            title: "Zamanlama bulunamadi",
            detail: $"'{name}' adinda bir zamanlama yok.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult JobNotFound(Guid id)
        => TypedResults.Problem(
            title: "Is bulunamadi",
            detail: $"'{id}' kimlikli bir is yok.",
            statusCode: StatusCodes.Status404NotFound);
}
