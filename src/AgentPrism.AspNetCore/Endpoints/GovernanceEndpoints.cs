using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Yonetisim uclari: kiracilar, MCP sunuculari ve kalici tool onay kurallari.
/// </summary>
/// <remarks>
/// Uclarin tamami korumali gruptadir. Ozellikle MCP sunucusu eklemek, disaridan
/// gelen tool tanimlarini kabul etmek demektir; bu uc bir guvenlik sinirdir.
/// </remarks>
internal static class GovernanceEndpoints
{
    /// <summary>Yonetisim uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    public static void Map(IEndpointRouteBuilder builder)
    {
        MapTenants(builder);
        MapMcpServers(builder);
        MapApprovalRules(builder);
    }

    private static void MapTenants(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/tenants/current", Ok<CurrentTenantResponse> (ITenantContext tenants)
                => TypedResults.Ok(new CurrentTenantResponse { TenantId = tenants.TenantId }))
            .WithName("AgentPrismCurrentTenant")
            .WithSummary("Gecerli istegin kiracisini dondurur.")
            .WithDescription(
                "Kiraci istekten cozulur. Tek kiracili kurulumda her zaman varsayilan " +
                "kiraci doner. Bu uc korumali gruptadir; /api/meta kiraci bilgisi tasimaz.");

        builder.MapGet("/api/tenants", async Task<Ok<IReadOnlyList<TenantDescriptor>>> (
                ITenantStore tenants,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await tenants.ListAsync(cancellationToken).ConfigureAwait(false)))
            .WithName("AgentPrismListTenants")
            .WithSummary("Kayitli kiracilari listeler.")
            .WithDescription(
                "Kiraci kaydi ZORUNLU DEGILDIR. Diger tablolardaki tenant_id bu kaydin " +
                "slug degeriyle ayni metindir ancak yabanci anahtarla baglanmaz; kaydi " +
                "olmayan bir kiraci calisma aninda hata uretmez.");

        builder.MapPut("/api/tenants/{slug}", async Task<Results<Ok<TenantDescriptor>, ProblemHttpResult>> (
                string slug,
                TenantRequest request,
                ITenantStore tenants,
                CancellationToken cancellationToken) =>
            {
                if (!HttpTenantContext.IsValidTenantId(slug))
                {
                    return TypedResults.Problem(
                        title: "Kiraci anahtari gecersiz",
                        detail: "Anahtar en fazla 64 karakter olmali ve yalnizca harf, rakam, " +
                                "nokta, alt cizgi ve tire icermelidir.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var saved = await tenants.SaveAsync(
                    new TenantDescriptor
                    {
                        Id = Guid.Empty,
                        Slug = slug,
                        DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? slug : request.DisplayName,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(saved);
            })
            .WithName("AgentPrismSaveTenant")
            .WithSummary("Bir kiraci kaydini ekler veya gunceller.");

        builder.MapDelete("/api/tenants/{slug}", async Task<Results<NoContent, ProblemHttpResult>> (
                string slug,
                ITenantStore tenants,
                CancellationToken cancellationToken)
                => await tenants.DeleteAsync(slug, cancellationToken).ConfigureAwait(false)
                    ? TypedResults.NoContent()
                    : TypedResults.Problem(
                        title: "Kiraci bulunamadi",
                        detail: $"'{slug}' anahtarli bir kiraci kaydi yok.",
                        statusCode: StatusCodes.Status404NotFound))
            .WithName("AgentPrismDeleteTenant")
            .WithSummary("Bir kiraci kaydini siler.")
            .WithDescription("Yalnizca kayit silinir; kiracinin agent'lari, oturumlari ve calistirmalari kalir.");
    }

    private static void MapMcpServers(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/mcp-servers", async Task<Ok<IReadOnlyList<McpServerDefinition>>> (
                IMcpServerStore servers,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => TypedResults.Ok(
                    await servers.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false)))
            .WithName("AgentPrismListMcpServers")
            .WithSummary("Kayitli uzak MCP sunucularini listeler.")
            .WithDescription(
                "Yanit SIR TASIMAZ: kimlik dogrulama degeri saklanmaz, yalnizca degerin " +
                "okunacagi yapilandirma anahtarinin adi doner.");

        builder.MapPut("/api/mcp-servers/{name}", async Task<Results<Ok<McpServerDefinition>, ProblemHttpResult>> (
                string name,
                McpServerRequest request,
                IMcpServerStore servers,
                ITenantContext tenants,
                CancellationToken cancellationToken) =>
            {
                if (Validate(name, request) is { } invalid)
                {
                    return invalid;
                }

                var saved = await servers.SaveAsync(
                    new McpServerDefinition
                    {
                        Id = Guid.Empty,
                        TenantId = tenants.TenantId,
                        Name = name,
                        Description = request.Description,
                        Endpoint = new Uri(request.Endpoint, UriKind.Absolute),
                        Transport = request.Transport,
                        AuthorizationConfigurationKey = request.AuthorizationConfigurationKey,
                        Headers = request.Headers ?? new Dictionary<string, string>(StringComparer.Ordinal),
                        Enabled = request.Enabled,
                        RequiresApproval = request.RequiresApproval,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(saved);
            })
            .WithName("AgentPrismSaveMcpServer")
            .WithSummary("Bir uzak MCP sunucusu ekler veya gunceller.")
            .WithDescription(
                "GUVENLIK SINIRI. MCP sunucusu eklemek, tool tanimlarini disaridan kabul " +
                "etmek demektir. Yalnizca http/https adresleri kabul edilir; yerel surec " +
                "(stdio) aktarimi desteklenmez. Tool'lar varsayilan olarak onay ister.");

        builder.MapDelete("/api/mcp-servers/{name}", async Task<Results<NoContent, ProblemHttpResult>> (
                string name,
                IMcpServerStore servers,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => await servers.DeleteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false)
                    ? TypedResults.NoContent()
                    : TypedResults.Problem(
                        title: "MCP sunucusu bulunamadi",
                        detail: $"'{name}' adinda bir sunucu yok.",
                        statusCode: StatusCodes.Status404NotFound))
            .WithName("AgentPrismDeleteMcpServer")
            .WithSummary("Bir uzak MCP sunucusunu siler.");

        builder.MapPost("/api/mcp-servers/refresh", async Task<Results<Ok<McpRefreshResponse>, ProblemHttpResult>> (
                IMcpToolRefresher? refresher,
                CancellationToken cancellationToken) =>
            {
                if (refresher is null)
                {
                    return TypedResults.Problem(
                        title: "MCP kayitli degil",
                        detail: "Tool kesfi icin AgentPrism.Mcp paketini ekleyin ve UseMcp() cagirin.",
                        statusCode: StatusCodes.Status501NotImplemented);
                }

                var count = await refresher.RefreshAsync(cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(new McpRefreshResponse { ToolCount = count });
            })
            .WithName("AgentPrismRefreshMcpTools")
            .WithSummary("Uzak MCP sunucularinin tool listesini simdi tazeler.")
            .WithDescription(
                "Tazeleme normalde arka planda belirli araliklarla yapilir. Bu uc, yeni " +
                "eklenen bir sunucunun tool'larinin bir sonraki tazelemeyi beklemeden " +
                "gorunmesi icindir.");
    }

    private static void MapApprovalRules(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/approvals/rules", async Task<Ok<IReadOnlyList<ToolApprovalRule>>> (
                IToolApprovalRuleStore rules,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await rules.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false)))
            .WithName("AgentPrismListApprovalRules")
            .WithSummary("Kalici 'bir daha sorma' onay kurallarini listeler.");

        builder.MapDelete("/api/approvals/rules/{ruleId:guid}", async Task<Results<NoContent, ProblemHttpResult>> (
                Guid ruleId,
                IToolApprovalRuleStore rules,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => await rules.DeleteAsync(tenants.TenantId, ruleId, cancellationToken).ConfigureAwait(false)
                    ? TypedResults.NoContent()
                    : TypedResults.Problem(
                        title: "Kural bulunamadi",
                        detail: $"'{ruleId}' kimlikli bir onay kurali yok.",
                        statusCode: StatusCodes.Status404NotFound))
            .WithName("AgentPrismDeleteApprovalRule")
            .WithSummary("Bir kalici onay kuralini geri alir.")
            .WithDescription("Kural silindikten sonra o tool icin onay yeniden sorulur.");
    }

    private static ProblemHttpResult? Validate(string name, McpServerRequest request)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return TypedResults.Problem(
                title: "Sunucu adi bos",
                detail: "Yoldaki ad zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return TypedResults.Problem(
                title: "Adres gecersiz",
                detail: "'endpoint' mutlak bir adres olmalidir.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Yerel surec (stdio) aktarimi bilerek desteklenmez: sunucuda surec
        // baslatmak, arayuze erisen birinin sunucuda program calistirmasi demektir.
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                title: "Adres semasi desteklenmiyor",
                detail: "Yalnizca http ve https kabul edilir. Yerel surec (stdio) aktarimi " +
                        "bilerek desteklenmez; sunucuda surec baslatmak tool'larin yalnizca " +
                        "kodda tanimlanmasi kuralini bozar.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }
}
