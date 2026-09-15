using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Quota definition and usage endpoints.</summary>
/// <remarks>
/// All dependencies are marked <strong>explicitly</strong> with
/// <c>[FromServices]</c>: in a minimal API, if a type that may not be
/// registered is left unmarked in an endpoint signature, a "Body was inferred"
/// error breaks ALL endpoints.
/// </remarks>
internal static class QuotaEndpoints
{
    /// <summary>Maps the quota endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/quotas", ListAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("TraconListQuotas")
            .WithTags("Tracon", "Governance")
            .WithSummary("Lists a tenant's quota rules.")
            .WithDescription(
                "Rules are definitions, not counters — read the counters from the usage " +
                "endpoint. A rule with no agent name applies to the whole tenant, and a " +
                "tenant-wide rule and an agent-specific rule can both be in force at once. " +
                "A rule with 'enabled: false' is kept but not enforced. An agent with no " +
                "matching rule is unlimited.");

        builder.MapPut("/api/quotas", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("TraconSaveQuota")
            .WithTags("Tracon", "Governance")
            .WithSummary("Creates or updates a quota rule.")
            .Accepts<QuotaSaveRequest>("application/json")
            .WithDescription(
                "The scope (tenant + agent + period) is unique: writing a second " +
                "rule for the same scope overwrites the existing rule. Each limit can also be left " +
                "empty; only the ones that are set are enforced.");

        builder.MapDelete("/api/quotas/{id:guid}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("TraconDeleteQuota")
            .WithTags("Tracon", "Governance")
            .WithSummary("Deletes a quota rule.")
            .WithDescription(
                "Removing the last rule that covers an agent makes it unlimited, which is why " +
                "the removal is written to the audit trail with the rule's previous values. " +
                "Usage counters already recorded are not deleted; they simply stop being " +
                "enforced. To keep the limits but stop enforcing them, save the rule with " +
                "'enabled: false' instead. An unknown id returns 404.");

        builder.MapGet("/api/quotas/usage", GetUsageAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("TraconGetQuotaUsage")
            .WithTags("Tracon", "Governance")
            .WithSummary("Returns the current period's quota usage.")
            .WithDescription(
                "An empty 'agentName' value shows the tenant-wide counter. Counters are approximate: " +
                "the check happens before a run starts, and consumption is written after it finishes.");
    }

    private static async Task<Ok<IReadOnlyList<QuotaDefinition>>> ListAsync(
        [FromServices] IQuotaStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var quotas = await store.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(quotas);
    }

    private static async Task<Results<Ok<QuotaDefinition>, ProblemHttpResult>> SaveAsync(
        HttpContext httpContext,
        [FromServices] IQuotaStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TimeProvider? timeProvider,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<QuotaSaveRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (request.MaxRuns is null && request.MaxTokens is null && request.MaxCost is null)
        {
            return Invalid("At least one limit ('maxRuns', 'maxTokens', or 'maxCost') must be given.");
        }

        if (request.MaxRuns is < 0 || request.MaxTokens is < 0 || request.MaxCost is < 0)
        {
            return Invalid("Limit values cannot be negative.");
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var agentName = string.IsNullOrWhiteSpace(request.AgentName) ? null : request.AgentName;

        var existing = await store.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);

        var previous = existing.FirstOrDefault(candidate =>
            string.Equals(candidate.AgentName ?? string.Empty, agentName ?? string.Empty, StringComparison.Ordinal)
            && candidate.Period == request.Period);

        var saved = await store.SaveAsync(
            new QuotaDefinition
            {
                Id = previous?.Id ?? TraconId.NewId(),
                TenantId = tenants.TenantId,
                AgentName = agentName,
                Period = request.Period,
                MaxRuns = request.MaxRuns,
                MaxTokens = request.MaxTokens,
                MaxCost = request.MaxCost,
                Enabled = request.Enabled,
                CreatedAt = previous?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.QuotaEndpoints"),
            metrics,
            tenants.TenantId,
            action: previous is null ? "quota.create" : "quota.update",
            entity: $"quota:{saved.Id}",
            before: previous is null ? null : Describe(previous),
            after: Describe(saved),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        [FromServices] IQuotaStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        var existing = await store.GetAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return TypedResults.Problem(
                title: "Quota not found",
                detail: $"There is no quota rule with id '{id}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await store.DeleteAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.QuotaEndpoints"),
            metrics,
            tenants.TenantId,
            action: "quota.delete",
            entity: $"quota:{id}",
            before: Describe(existing),
            after: null,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<Ok<QuotaUsageResponse>> GetUsageAsync(
        [FromQuery] string? agentName,
        [FromQuery] QuotaPeriod? period,
        [FromServices] IQuotaStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IOptionsMonitor<TraconQuotaOptions> quotaOptions,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        var options = quotaOptions.CurrentValue;
        var timeZone = options.ResolveTimeZone();
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();

        var definitions = await store.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);

        var usage = await store.GetUsageAsync(
            new QuotaUsageQuery
            {
                TenantId = tenants.TenantId,
                AgentName = agentName,
                Period = period,
                AsOf = now,
            },
            cancellationToken).ConfigureAwait(false);

        // Only the counters of the CURRENT period are returned: past periods
        // are noise for the "usage bar" and would produce a wrong percentage in the UI.
        var current = usage
            .Where(record => record.PeriodStart == QuotaPeriodCalculator.GetPeriodStart(now, record.Period, timeZone))
            .ToList();

        return TypedResults.Ok(new QuotaUsageResponse
        {
            TenantId = tenants.TenantId,
            TimeZone = options.TimeZone,
            Usage = current,
            Definitions = definitions,
            DailyResetsAt = QuotaPeriodCalculator.GetPeriodEnd(now, QuotaPeriod.Daily, timeZone),
            MonthlyResetsAt = QuotaPeriodCalculator.GetPeriodEnd(now, QuotaPeriod.Monthly, timeZone),
        });
    }

    /// <summary>Summarizes a rule as JSON for the audit trail.</summary>
    /// <remarks>
    /// A rule carries no secret, so there is no field for the secret filter to
    /// redact; the entry still passes through <see cref="AuditRecorder"/> and
    /// the filter is always applied.
    /// </remarks>
    private static string Describe(QuotaDefinition definition)
    {
        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();

        if (definition.AgentName is { } agentName)
        {
            writer.WriteString("agentName", agentName);
        }
        else
        {
            writer.WriteNull("agentName");
        }

        writer.WriteString("period", definition.Period.ToString());
        WriteNullableNumber(writer, "maxRuns", definition.MaxRuns);
        WriteNullableNumber(writer, "maxTokens", definition.MaxTokens);
        WriteNullableDecimal(writer, "maxCost", definition.MaxCost);
        writer.WriteBoolean("enabled", definition.Enabled);
        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void WriteNullableDecimal(Utf8JsonWriter writer, string name, decimal? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static ProblemHttpResult Invalid(string detail)
        => TypedResults.Problem(
            title: "Quota invalid",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>Request body for saving a quota rule.</summary>
public sealed record QuotaSaveRequest
{
    /// <summary>
    /// Gets the agent the rule is bound to. If left empty, the rule applies to all
    /// of the tenant's runs.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>Gets the counter's reset interval.</summary>
    public QuotaPeriod Period { get; init; } = QuotaPeriod.Daily;

    /// <summary>Gets the maximum number of runs per period.</summary>
    public long? MaxRuns { get; init; }

    /// <summary>Gets the maximum number of tokens per period.</summary>
    public long? MaxTokens { get; init; }

    /// <summary>Gets the maximum monetary amount per period.</summary>
    public decimal? MaxCost { get; init; }

    /// <summary>Gets whether the rule is enabled.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Response for the quota usage endpoint.</summary>
public sealed record QuotaUsageResponse
{
    /// <summary>Gets the tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the time zone the period boundaries are computed in.</summary>
    public required string TimeZone { get; init; }

    /// <summary>Gets the current period's counters.</summary>
    public required IReadOnlyList<QuotaUsageRecord> Usage { get; init; }

    /// <summary>Gets the defined quota rules. The UI computes the percentage from these.</summary>
    public required IReadOnlyList<QuotaDefinition> Definitions { get; init; }

    /// <summary>Gets the moment (UTC) the daily counters reset.</summary>
    public required DateTimeOffset DailyResetsAt { get; init; }

    /// <summary>Gets the moment (UTC) the monthly counters reset.</summary>
    public required DateTimeOffset MonthlyResetsAt { get; init; }
}
