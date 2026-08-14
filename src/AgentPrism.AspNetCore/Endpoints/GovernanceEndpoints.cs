using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

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
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        MapTenants(builder, roles);
        MapMcpServers(builder, roles);
        MapMcpPrompts(builder, roles);
        MapMcpResources(builder, roles);
        MapMcpOAuthStart(builder, roles);
        MapApprovalRules(builder, roles);
    }

    /// <summary>
    /// OAuth geri donus (callback) ucunu baglar. Diger yonetisim uclarindan
    /// AYRI bir gruba baglanmalidir: saglayicinin yonlendirdigi tarayici
    /// isteginde bizim bearer token'imiz olamaz — <c>state</c> parametresi
    /// tek gecerli kimlik kanitidir (bolum 22.3).
    /// </summary>
    /// <param name="builder">Bearer token denetiminden MUAF, loopback+policy'den GECEN uc grubu.</param>
    public static void MapMcpOAuthCallback(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/mcp-servers/{name}/oauth/callback", async Task<ContentHttpResult> (
                string name,
                string? code,
                string? state,
                string? iss,
                string? error,
                [FromServices] IMcpOAuthCoordinator? coordinator,
                CancellationToken cancellationToken) =>
            {
                if (coordinator is null)
                {
                    return OAuthCallbackPage("MCP OAuth kayitli degil.", success: false);
                }

                if (string.IsNullOrEmpty(state))
                {
                    return OAuthCallbackPage("Gecersiz istek: durum degeri eksik.", success: false);
                }

                var result = await coordinator
                    .CompleteAsync(state, error is null ? code : null, iss, cancellationToken)
                    .ConfigureAwait(false);

                return result.Status == McpOAuthOperationStatus.Ok
                    ? OAuthCallbackPage(
                        $"'{result.ServerName}' sunucusu icin yetkilendirme tamamlandi. Bu sekmeyi kapatabilirsiniz.",
                        success: true)
                    : OAuthCallbackPage(DescribeOAuthFailure(result), success: false);
            })
            .WithName("AgentPrismMcpOAuthCallback")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("OAuth saglayicisinin geri donus istegini isler.")
            .WithDescription(
                "Bu uc erisim katmanlarinin disindadir: saglayicinin yonlendirdigi tarayici bizim " +
                "bearer token'imizi tasiyamaz. Guvenlik, tek kullanimlik 'state' degerine dayanir.");
    }

    private static string DescribeOAuthFailure(McpOAuthCompleteResult result)
        => result.Status switch
        {
            McpOAuthOperationStatus.InvalidState => "Yetkilendirme oturumu bulunamadi veya suresi doldu. Yeniden deneyin.",
            McpOAuthOperationStatus.AuthorizationFailed => result.Error ?? "Yetkilendirme saglayici tarafindan reddedildi.",
            _ => "Beklenmeyen bir hata olustu.",
        };

    private static ContentHttpResult OAuthCallbackPage(string message, bool success)
        => TypedResults.Text(
            $$"""
            <!doctype html>
            <html lang="tr">
            <head><meta charset="utf-8"><title>MCP OAuth</title></head>
            <body style="font-family: system-ui, sans-serif; padding: 2rem; max-width: 40rem; margin: 0 auto;">
            <h1>{{(success ? "Yetkilendirme tamamlandi" : "Yetkilendirme basarisiz")}}</h1>
            <p>{{System.Net.WebUtility.HtmlEncode(message)}}</p>
            <script>if (window.opener) { window.close(); }</script>
            </body>
            </html>
            """,
            "text/html; charset=utf-8");

    private static void MapTenants(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/tenants/current", Ok<CurrentTenantResponse> (ITenantContext tenants)
                => TypedResults.Ok(new CurrentTenantResponse { TenantId = tenants.TenantId }))
            .RequireRole(roles.Reader)
            .WithName("AgentPrismCurrentTenant")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Gecerli istegin kiracisini dondurur.")
            .WithDescription(
                "Kiraci istekten cozulur. Tek kiracili kurulumda her zaman varsayilan " +
                "kiraci doner. Bu uc korumali gruptadir; /api/meta kiraci bilgisi tasimaz.");

        builder.MapGet("/api/tenants", async Task<Ok<IReadOnlyList<TenantDescriptor>>> (
                ITenantStore tenants,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await tenants.ListAsync(cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismListTenants")
            .WithTags("AgentPrism", "Governance")
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
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismSaveTenant")
            .WithTags("AgentPrism", "Governance")
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
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismDeleteTenant")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Bir kiraci kaydini siler.")
            .WithDescription("Yalnizca kayit silinir; kiracinin agent'lari, oturumlari ve calistirmalari kalir.");
    }

    private static void MapMcpServers(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/mcp-servers", async Task<Ok<IReadOnlyList<McpServerDefinition>>> (
                IMcpServerStore servers,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => TypedResults.Ok(
                    await servers.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismListMcpServers")
            .WithTags("AgentPrism", "Governance")
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
                        OAuthEnabled = request.OAuthEnabled,
                        OAuthClientId = request.OAuthClientId,
                        OAuthClientSecretConfigurationKey = request.OAuthClientSecretConfigurationKey,
                        OAuthScopes = request.OAuthScopes,
                        OAuthAuthorizationMode = request.OAuthAuthorizationMode,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(saved);
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismSaveMcpServer")
            .WithTags("AgentPrism", "Governance")
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
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismDeleteMcpServer")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Bir uzak MCP sunucusunu siler.");

        builder.MapPost("/api/mcp-servers/refresh", async Task<Results<Ok<McpRefreshResponse>, ProblemHttpResult>> (
                IMcpToolRefresher? refresher,
                IAuditLog auditLog,
                IAuditActorResolver actorResolver,
                ITenantContext tenants,
                ILoggerFactory loggerFactory,
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

                // Elle tazeleme, kayitli bir sunucuya yapilan yazma degildir; bu yuzden
                // denetim izi burada, uc katmaninda yazilir.
                await AuditRecorder.WriteAsync(
                    auditLog,
                    actorResolver,
                    loggerFactory.CreateLogger("AgentPrism.GovernanceEndpoints"),
                    tenants.TenantId,
                    action: "mcp.refresh",
                    entity: "mcp:*",
                    before: null,
                    after: $$"""{"toolCount":{{count}}}""",
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(new McpRefreshResponse { ToolCount = count });
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismRefreshMcpTools")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Uzak MCP sunucularinin tool listesini simdi tazeler.")
            .WithDescription(
                "Tazeleme normalde arka planda belirli araliklarla yapilir. Bu uc, yeni " +
                "eklenen bir sunucunun tool'larinin bir sonraki tazelemeyi beklemeden " +
                "gorunmesi icindir.");
    }

    private static void MapMcpPrompts(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/mcp-servers/{name}/prompts", async Task<Results<Ok<IReadOnlyList<McpPromptSummary>>, ProblemHttpResult>> (
                string name,
                [FromServices] IMcpPromptClient? prompts,
                ITenantContext tenants,
                CancellationToken cancellationToken) =>
            {
                if (prompts is null)
                {
                    return McpNotRegisteredProblem();
                }

                var result = await prompts.ListPromptsAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

                return result.Status switch
                {
                    McpOperationStatus.Ok => TypedResults.Ok(result.Prompts),
                    McpOperationStatus.ServerNotFound => McpServerNotFoundProblem(name),
                    McpOperationStatus.CapabilityUnsupported => McpCapabilityUnsupportedProblem(name, "prompts"),
                    _ => McpConnectionFailedProblem(name),
                };
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismListMcpPrompts")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Bir MCP sunucusunun prompt listesini getirir.")
            .WithDescription("Sunucu 'prompts' yetenegini bildirmiyorsa istek hic gonderilmez.");

        builder.MapPost("/api/mcp-servers/{name}/prompts/{prompt}", async Task<Results<Ok<McpPromptContent>, ProblemHttpResult>> (
                string name,
                string prompt,
                McpPromptArgumentsRequest? request,
                [FromServices] IMcpPromptClient? prompts,
                ITenantContext tenants,
                CancellationToken cancellationToken) =>
            {
                if (prompts is null)
                {
                    return McpNotRegisteredProblem();
                }

                var (status, content) = await prompts
                    .GetPromptAsync(tenants.TenantId, name, prompt, request?.Arguments, cancellationToken)
                    .ConfigureAwait(false);

                return status switch
                {
                    McpOperationStatus.Ok => TypedResults.Ok(content!),
                    McpOperationStatus.ServerNotFound => McpServerNotFoundProblem(name),
                    McpOperationStatus.CapabilityUnsupported => McpCapabilityUnsupportedProblem(name, "prompts"),
                    McpOperationStatus.ItemNotFound => McpItemNotFoundProblem("Prompt", prompt),
                    _ => McpConnectionFailedProblem(name),
                };
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismGetMcpPrompt")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Bir MCP prompt'unun icerigini argumanlarla cozer.")
            .WithDescription(
                "Donen icerik ANLIK GORUNTUDUR: agent talimatina kopyalanmasi gerekir, calisma " +
                "aninda yeniden cekilmez (bolum 22.1). 'hash' alani sunucudaki degisimi izlemek icindir.");
    }

    private static void MapMcpResources(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/mcp-servers/{name}/resources", async Task<Results<Ok<IReadOnlyList<McpResourceSummary>>, ProblemHttpResult>> (
                string name,
                [FromServices] IMcpResourceClient? resources,
                ITenantContext tenants,
                CancellationToken cancellationToken) =>
            {
                if (resources is null)
                {
                    return McpNotRegisteredProblem();
                }

                var result = await resources.ListResourcesAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

                return result.Status switch
                {
                    McpOperationStatus.Ok => TypedResults.Ok(result.Resources),
                    McpOperationStatus.ServerNotFound => McpServerNotFoundProblem(name),
                    McpOperationStatus.CapabilityUnsupported => McpCapabilityUnsupportedProblem(name, "resources"),
                    _ => McpConnectionFailedProblem(name),
                };
            })
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismListMcpResources")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Bir MCP sunucusunun kaynak listesini getirir.")
            .WithDescription("Sunucu 'resources' yetenegini bildirmiyorsa istek hic gonderilmez.");

        builder.MapGet("/api/mcp-servers/{name}/resources/read", async Task<Results<Ok<McpResourceContent>, ProblemHttpResult>> (
                string name,
                string uri,
                [FromServices] IMcpResourceClient? resources,
                ITenantContext tenants,
                CancellationToken cancellationToken) =>
            {
                if (resources is null)
                {
                    return McpNotRegisteredProblem();
                }

                var (status, content) = await resources
                    .ReadResourceAsync(tenants.TenantId, name, uri, cancellationToken)
                    .ConfigureAwait(false);

                return status switch
                {
                    McpOperationStatus.Ok => TypedResults.Ok(content!),
                    McpOperationStatus.ServerNotFound => McpServerNotFoundProblem(name),
                    McpOperationStatus.CapabilityUnsupported => McpCapabilityUnsupportedProblem(name, "resources"),
                    McpOperationStatus.UriNotDeclared => McpUriNotDeclaredProblem(uri),
                    McpOperationStatus.ItemNotFound => McpItemNotFoundProblem("Kaynak", uri),
                    _ => McpConnectionFailedProblem(name),
                };
            })
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismReadMcpResource")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Bir MCP kaynagini okur.")
            .WithDescription(
                "Yalniz sunucunun ListResourcesAsync ile bildirdigi URI'ler kabul edilir; " +
                "serbest URI SSRF riski tasidigi icin reddedilir (bolum 22.2).");
    }

    private static void MapMcpOAuthStart(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapPost("/api/mcp-servers/{name}/oauth/start", async Task<Results<Ok<McpOAuthStartResponse>, ProblemHttpResult>> (
                string name,
                [FromServices] IMcpOAuthCoordinator? coordinator,
                ITenantContext tenants,
                CancellationToken cancellationToken) =>
            {
                if (coordinator is null)
                {
                    return McpNotRegisteredProblem();
                }

                var result = await coordinator.StartAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);

                return result.Status switch
                {
                    McpOAuthOperationStatus.Ok => TypedResults.Ok(new McpOAuthStartResponse
                    {
                        AuthorizationUri = result.AuthorizationUri!.ToString(),
                        State = result.State!,
                    }),
                    McpOAuthOperationStatus.ServerNotFound => McpServerNotFoundProblem(name),
                    McpOAuthOperationStatus.NotConfigured => TypedResults.Problem(
                        title: "OAuth yapilandirilmamis",
                        detail: "Sunucuda OAuth acik degil, akis Authorization Code degil veya " +
                                "AgentPrism:Mcp:OAuthCallbackBaseUri ayarlanmamis.",
                        statusCode: StatusCodes.Status409Conflict),
                    _ => McpConnectionFailedProblem(name),
                };
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismStartMcpOAuth")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Bir MCP sunucusu icin OAuth yetkilendirme akisini baslatir.")
            .WithDescription(
                "Donen 'authorizationUri' adresine yonetici yonlendirilir. Saglayici onaydan sonra " +
                "'/oauth/callback' ucuna doner; bu uc CSRF korumasi icin 'state' degerini kullanir.");
    }

    private static ProblemHttpResult McpNotRegisteredProblem()
        => TypedResults.Problem(
            title: "MCP kayitli degil",
            detail: "AgentPrism.Mcp paketini ekleyin ve UseMcp() cagirin.",
            statusCode: StatusCodes.Status501NotImplemented);

    private static ProblemHttpResult McpServerNotFoundProblem(string name)
        => TypedResults.Problem(
            title: "MCP sunucusu bulunamadi",
            detail: $"'{name}' adinda bir sunucu yok.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult McpCapabilityUnsupportedProblem(string name, string capability)
        => TypedResults.Problem(
            title: "Sunucu desteklemiyor",
            detail: $"'{name}' sunucusu '{capability}' yetenegini bildirmiyor.",
            statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult McpConnectionFailedProblem(string name)
        => TypedResults.Problem(
            title: "Sunucuya baglanilamadi",
            detail: $"'{name}' sunucusuna ulasilamadi veya istek basarisiz oldu.",
            statusCode: StatusCodes.Status502BadGateway);

    private static ProblemHttpResult McpUriNotDeclaredProblem(string uri)
        => TypedResults.Problem(
            title: "Kaynak bildirilmemis",
            detail: $"'{uri}' bu sunucunun kaynak listesinde degil.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult McpItemNotFoundProblem(string kind, string name)
        => TypedResults.Problem(
            title: $"{kind} bulunamadi",
            detail: $"'{name}' sunucuda yok.",
            statusCode: StatusCodes.Status404NotFound);

    private static void MapApprovalRules(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/approvals/rules", async Task<Ok<IReadOnlyList<ToolApprovalRule>>> (
                IToolApprovalRuleStore rules,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await rules.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismListApprovalRules")
            .WithTags("AgentPrism", "Governance")
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
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismDeleteApprovalRule")
            .WithTags("AgentPrism", "Governance")
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

        if (request.OAuthEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.OAuthClientId))
            {
                return TypedResults.Problem(
                    title: "OAuth istemci kimligi eksik",
                    detail: "'oauthClientId' OAuth acikken zorunludur.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!string.IsNullOrEmpty(request.AuthorizationConfigurationKey))
            {
                return TypedResults.Problem(
                    title: "Cakisan kimlik dogrulama",
                    detail: "OAuth acikken 'authorizationConfigurationKey' bos olmalidir; ikisi ayni " +
                            "Authorization basligini yonetmeye calisirdi.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return null;
    }
}
