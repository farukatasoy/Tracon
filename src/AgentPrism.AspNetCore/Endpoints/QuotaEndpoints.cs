using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Kota tanimi ve kullanim uclari (Faz 21).</summary>
/// <remarks>
/// 🚨 Tum bagimliliklar <c>[FromServices]</c> ile <strong>acikca</strong>
/// isaretlenir: minimal API'de kayitli olmayabilecek bir tip uc imzasinda
/// isaretsiz kalirsa "Body was inferred" hatasi TUM uclari kirar (Faz 9 dersi,
/// <c>docs/hafiza/aspnetcore-di.md</c>).
/// </remarks>
internal static class QuotaEndpoints
{
    /// <summary>Kota uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/quotas", ListAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismListQuotas")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Bir kiracinin kota kurallarini listeler.");

        builder.MapPut("/api/quotas", SaveAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismSaveQuota")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Kota kurali olusturur veya gunceller.")
            .WithDescription(
                "Kapsam (kiraci + agent + donem) benzersizdir: ayni kapsam icin ikinci bir " +
                "kural yazmak mevcut kuralin uzerine yazar. Uc sinir da bos birakilabilir; " +
                "yalnizca dolu olanlar uygulanir.");

        builder.MapDelete("/api/quotas/{id:guid}", DeleteAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismDeleteQuota")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Bir kota kuralini siler.");

        builder.MapGet("/api/quotas/usage", GetUsageAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismGetQuotaUsage")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Gecerli donemin kota kullanimini dondurur.")
            .WithDescription(
                "Bos 'agentName' degeri kiraci geneli sayacini gosterir. Sayaclar yaklasiktir: " +
                "denetim calistirma oncesinde, tuketim sonrasinda yazilir.");
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
        [FromBody] QuotaSaveRequest request,
        [FromServices] IQuotaStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxRuns is null && request.MaxTokens is null && request.MaxCost is null)
        {
            return Invalid("En az bir sinir ('maxRuns', 'maxTokens' veya 'maxCost') verilmelidir.");
        }

        if (request.MaxRuns is < 0 || request.MaxTokens is < 0 || request.MaxCost is < 0)
        {
            return Invalid("Sinir degerleri negatif olamaz.");
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
                Id = previous?.Id ?? AgentPrismId.NewId(),
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
            loggerFactory.CreateLogger("AgentPrism.QuotaEndpoints"),
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
        CancellationToken cancellationToken)
    {
        var existing = await store.GetAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return TypedResults.Problem(
                title: "Kota bulunamadi",
                detail: $"'{id}' kimlikli bir kota kurali yok.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await store.DeleteAsync(tenants.TenantId, id, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.QuotaEndpoints"),
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
        [FromServices] IOptionsMonitor<AgentPrismQuotaOptions> quotaOptions,
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

        // Yalnizca GECERLI donemin sayaclari dondurulur: gecmis donemler
        // "kullanim cubugu" icin gurultudur ve arayuzde yanlis yuzde uretirdi.
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

    /// <summary>Bir kurali denetim izi icin JSON olarak ozetler.</summary>
    /// <remarks>
    /// Kural hicbir sir tasimaz, bu yuzden sir suzgecinin gizleyecegi bir alan
    /// yoktur; yine de kayit <see cref="AuditRecorder"/> uzerinden gecer ve
    /// suzgec her zaman uygulanir.
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
            title: "Gecersiz kota",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>Bir kota kuralini kaydetmek icin istek govdesi.</summary>
public sealed record QuotaSaveRequest
{
    /// <summary>
    /// Kuralin baglandigi agent. Bos birakilirsa kural kiracinin tum
    /// calistirmalarina uygulanir.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>Sayacin sifirlanma araligi.</summary>
    public QuotaPeriod Period { get; init; } = QuotaPeriod.Daily;

    /// <summary>Donem basina en fazla calistirma sayisi.</summary>
    public long? MaxRuns { get; init; }

    /// <summary>Donem basina en fazla token.</summary>
    public long? MaxTokens { get; init; }

    /// <summary>Donem basina en fazla para tutari.</summary>
    public decimal? MaxCost { get; init; }

    /// <summary>Kural etkin mi.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Kota kullanim ucunun yaniti.</summary>
public sealed record QuotaUsageResponse
{
    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Donem sinirlarinin hesaplandigi saat dilimi.</summary>
    public required string TimeZone { get; init; }

    /// <summary>Gecerli donemin sayaclari.</summary>
    public required IReadOnlyList<QuotaUsageRecord> Usage { get; init; }

    /// <summary>Tanimli kota kurallari. Arayuz yuzdeyi bunlardan hesaplar.</summary>
    public required IReadOnlyList<QuotaDefinition> Definitions { get; init; }

    /// <summary>Gunluk sayaclarin sifirlanacagi an (UTC).</summary>
    public required DateTimeOffset DailyResetsAt { get; init; }

    /// <summary>Aylik sayaclarin sifirlanacagi an (UTC).</summary>
    public required DateTimeOffset MonthlyResetsAt { get; init; }
}
