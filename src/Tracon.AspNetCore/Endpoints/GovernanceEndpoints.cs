using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Governance endpoints: tenants, MCP servers, and persistent tool approval rules.
/// </summary>
/// <remarks>
/// All endpoints are in the protected group. In particular, adding an MCP server
/// means accepting tool definitions from an external source; this endpoint is a security boundary.
/// </remarks>
internal static class GovernanceEndpoints
{
    /// <summary>Maps the governance endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        MapTenants(builder, roles);
        MapMcpServers(builder, roles);
        MapMcpPrompts(builder, roles);
        MapMcpResources(builder, roles);
        MapMcpOAuthStart(builder, roles);
        MapApprovalRules(builder, roles);
    }

    /// <summary>
    /// Maps the OAuth callback endpoint. It must be mapped to a group SEPARATE
    /// from the other governance endpoints: the browser request redirected by the
    /// provider cannot carry our bearer token — the <c>state</c> parameter is the
    /// only valid proof of identity.
    /// </summary>
    /// <param name="builder">The endpoint group EXEMPT from bearer token checks, but subject to loopback+policy.</param>
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
                    return OAuthCallbackPage("MCP OAuth is not registered.", success: false);
                }

                if (string.IsNullOrEmpty(state))
                {
                    return OAuthCallbackPage("Invalid request: the state value is missing.", success: false);
                }

                var result = await coordinator
                    .CompleteAsync(state, error is null ? code : null, iss, cancellationToken)
                    .ConfigureAwait(false);

                return result.Status == McpOAuthOperationStatus.Ok
                    ? OAuthCallbackPage(
                        $"Authorization for server '{result.ServerName}' is complete. You can close this tab.",
                        success: true)
                    : OAuthCallbackPage(DescribeOAuthFailure(result), success: false);
            })
            .WithName("TraconMcpOAuthCallback")
            .WithTags("Tracon", "Governance")
            .WithSummary("Processes the OAuth provider's callback request.")
            .WithDescription(
                "This endpoint is outside the access layers: the browser redirected by the " +
                "provider cannot carry our bearer token. Security relies on the single-use 'state' value.");
    }

    private static string DescribeOAuthFailure(McpOAuthCompleteResult result)
        => result.Status switch
        {
            McpOAuthOperationStatus.InvalidState => "The authorization session was not found or has expired. Try again.",
            McpOAuthOperationStatus.AuthorizationFailed => result.Error ?? "Authorization was rejected by the provider.",
            _ => "An unexpected error occurred.",
        };

    private static ContentHttpResult OAuthCallbackPage(string message, bool success)
        => TypedResults.Text(
            $$"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>MCP OAuth</title></head>
            <body style="font-family: system-ui, sans-serif; padding: 2rem; max-width: 40rem; margin: 0 auto;">
            <h1>{{(success ? "Authorization complete" : "Authorization failed")}}</h1>
            <p>{{System.Net.WebUtility.HtmlEncode(message)}}</p>
            <script>if (window.opener) { window.close(); }</script>
            </body>
            </html>
            """,
            "text/html; charset=utf-8");

    private static void MapTenants(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/tenants/current", Ok<CurrentTenantResponse> (ITenantContext tenants)
                => TypedResults.Ok(new CurrentTenantResponse { TenantId = tenants.TenantId }))
            .RequireRole(roles.Reader)
            .WithName("TraconCurrentTenant")
            .WithTags("Tracon", "Governance")
            .WithSummary("Returns the current request's tenant.")
            .WithDescription(
                "The tenant is resolved from the request. In a single-tenant setup, it always " +
                "returns the default tenant. This endpoint is in the protected group; /api/meta does not carry tenant information.");

        builder.MapGet("/api/tenants", async Task<Ok<IReadOnlyList<TenantDescriptor>>> (
                ITenantStore tenants,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await tenants.ListAsync(cancellationToken).ConfigureAwait(false)))
            // 🚨 Admin, not Reader. This endpoint is [TenantAgnostic]: it returns
            // EVERY tenant in the installation, and its own exemption text says it
            // "exists for the management surface (Admin policy)". It asked for
            // Reader, so the lowest role could read the customer list of a
            // multi-tenant installation. The sibling PUT and DELETE were already
            // Admin; only the listing had slipped.
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .RequirePlatformAuthority()
            .WithName("TraconListTenants")
            .WithTags("Tracon", "Governance")
            .WithSummary("Lists registered tenants.")
            .WithDescription(
                "A tenant record is NOT REQUIRED. The tenant_id in other tables is the same " +
                "text as this record's slug value, but it is not connected by a foreign key; " +
                "a tenant with no record does not produce an error at runtime. The list names " +
                "every tenant of the installation, so it requires platform authority (403 " +
                "otherwise).");

        builder.MapPut("/api/tenants/{slug}", async Task<Results<Ok<TenantDescriptor>, ProblemHttpResult>> (
                string slug,
                HttpContext httpContext,
                ITenantStore tenants,
                CancellationToken cancellationToken) =>
            {
                if (!HttpTenantContext.IsValidTenantId(slug))
                {
                    return TypedResults.Problem(
                        title: "Tenant key invalid",
                        detail: "The key must be at most 64 characters and contain only letters, " +
                                "digits, dots, underscores, and hyphens.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var (request, bindError) = await RequestBodyBinding
                    .ReadAsync<TenantRequest>(httpContext, cancellationToken)
                    .ConfigureAwait(false);

                if (bindError is not null)
                {
                    return bindError;
                }

                var displayName = request!.DisplayName;

                var saved = await tenants.SaveAsync(
                    new TenantDescriptor
                    {
                        Id = Guid.Empty,
                        Slug = slug,
                        DisplayName = string.IsNullOrWhiteSpace(displayName) ? slug : displayName,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(saved);
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .RequirePlatformAuthorityForOtherTenant("slug")
            .WithName("TraconSaveTenant")
            .WithTags("Tracon", "Governance")
            .WithSummary("Adds or updates a tenant record.")
            .WithDescription(
                "The record is a display name for a tenant key that already works without it; " +
                "creating one does not create the tenant and deleting one does not remove its " +
                "data. The slug comes from the path and must be at most 64 characters of " +
                "letters, digits, dots, underscores, and hyphens (400 otherwise) — it is the " +
                "same text stored as 'tenant_id' on every other row, and it is folded to " +
                "lower case for the same reason, so 'Acme' and 'acme' name one record. " +
                "An empty display name falls back to the slug. A tenant other than the " +
                "caller's own requires platform authority (403 otherwise).")
            .Accepts<TenantRequest>("application/json");

        builder.MapDelete("/api/tenants/{slug}", async Task<Results<NoContent, ProblemHttpResult>> (
                string slug,
                ITenantStore tenants,
                CancellationToken cancellationToken)
                => await tenants.DeleteAsync(slug, cancellationToken).ConfigureAwait(false)
                    ? TypedResults.NoContent()
                    : TypedResults.Problem(
                        title: "Tenant not found",
                        detail: $"There is no tenant record with key '{slug}'.",
                        statusCode: StatusCodes.Status404NotFound))
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .RequirePlatformAuthorityForOtherTenant("slug")
            .WithName("TraconDeleteTenant")
            .WithTags("Tracon", "Governance")
            .WithSummary("Deletes a tenant record.")
            .WithDescription(
                "Only the record is deleted; the tenant's agents, sessions, and runs remain. " +
                "A tenant other than the caller's own requires platform authority (403 otherwise).");
    }

    private static void MapMcpServers(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/mcp-servers", async Task<Ok<IReadOnlyList<McpServerDefinition>>> (
                IMcpServerStore servers,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => TypedResults.Ok<IReadOnlyList<McpServerDefinition>>(
                    [.. (await servers.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false))
                        .Select(MaskHeaders)]))
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconListMcpServers")
            .WithTags("Tracon", "Governance")
            .WithSummary("Lists registered remote MCP servers.")
            .WithDescription(
                "No credential value is stored: 'headerConfigurationKeys' (and the deprecated " +
                "'authorizationConfigurationKey') carry only the NAME of the configuration key " +
                "each value is read from, and are returned as stored. Plain 'headers' are stored " +
                "as sent and are returned with their NAMES only: every value is replaced with '***'.");

        builder.MapPut("/api/mcp-servers/{name}", async Task<Results<Ok<McpServerDefinition>, ProblemHttpResult>> (
                string name,
                HttpContext httpContext,
                IMcpServerStore servers,
                ITenantContext tenants,
                IOptionsMonitor<TraconEgressOptions> egressOptions,
                IOptionsMonitor<TraconMcpSecurityOptions> mcpSecurityOptions,
                IOptions<TraconOptions> coreOptions,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var (bound, bindError) = await RequestBodyBinding
                    .ReadAsync<McpServerRequest>(httpContext, cancellationToken)
                    .ConfigureAwait(false);

                if (bindError is not null)
                {
                    return bindError;
                }

                var request = bound!;

                if (Validate(
                        name,
                        request,
                        tenants.TenantId,
                        coreOptions.Value.DefaultTenantId,
                        egressOptions.CurrentValue,
                        mcpSecurityOptions.CurrentValue) is { } invalid)
                {
                    return invalid;
                }

                // A save that leaves a header map out keeps the stored one: the
                // admin form sends neither map, and a read masks the plain
                // values, so it could not send them back (phase 190).
                var previous = await servers.GetAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false);
                var headers = CredentialHeaderSaveRules.Effective(request.Headers, previous?.Headers, StringComparer.Ordinal);
                var headerKeys = CredentialHeaderSaveRules.Effective(
                    request.HeaderConfigurationKeys,
                    previous?.HeaderConfigurationKeys,
                    StringComparer.OrdinalIgnoreCase);

                if (ValidateHeaders(
                        request,
                        headers,
                        headerKeys,
                        tenants.TenantId,
                        coreOptions.Value.DefaultTenantId,
                        mcpSecurityOptions.CurrentValue) is { } invalidHeaders)
                {
                    return invalidHeaders;
                }

                var endpoint = new Uri(request.Endpoint, UriKind.Absolute);

                WarnWhenPreservedHeadersMove(
                    loggerFactory.CreateLogger("Tracon.GovernanceEndpoints"),
                    name,
                    previous,
                    endpoint,
                    request);

                var saved = await servers.SaveAsync(
                    new McpServerDefinition
                    {
                        Id = Guid.Empty,
                        TenantId = tenants.TenantId,
                        Name = name,
                        Description = request.Description,
                        Endpoint = endpoint,
                        Transport = request.Transport,
#pragma warning disable CS0618 // The deprecated field is still accepted and stored until 1.0.0 (phase 190).
                        AuthorizationConfigurationKey = request.AuthorizationConfigurationKey,
#pragma warning restore CS0618
                        Headers = headers,
                        HeaderConfigurationKeys = CredentialHeaderSaveRules.Copy(headerKeys, StringComparer.OrdinalIgnoreCase),
                        Enabled = request.Enabled,
                        RequiresApproval = request.RequiresApproval,
                        OAuthEnabled = request.OAuthEnabled,
                        OAuthClientId = request.OAuthClientId,
                        OAuthClientSecretConfigurationKey = request.OAuthClientSecretConfigurationKey,
                        OAuthScopes = request.OAuthScopes,
                        OAuthAuthorizationMode = request.OAuthAuthorizationMode,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(MaskHeaders(saved));
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("TraconSaveMcpServer")
            .WithTags("Tracon", "Governance")
            .WithSummary("Adds or updates a remote MCP server.")
            .Accepts<McpServerRequest>("application/json")
            .WithDescription(
                "SECURITY BOUNDARY. Adding an MCP server means accepting tool definitions " +
                "from an external source. Only http/https addresses are accepted; local process " +
                "(stdio) transport is not supported. Tools require approval by default. A " +
                "configuration key name must be under the configured allowed prefix and inside " +
                "the tenant's own key space — '{prefix}{tenantId}:...'; a flat name directly " +
                "under the prefix belongs to the default tenant (400 otherwise). A credential " +
                "header (Authorization, X-Api-Key, Cookie, any name ending in '-key') is declared " +
                "in 'headerConfigurationKeys' as the NAME of the configuration key its value is " +
                "read from; the same header in plain 'headers' is rejected with 400, because " +
                "plain header values are stored as given. No response returns a plain header " +
                "value: the saved record comes back with every one replaced by '***', and a " +
                "header whose value is '***' is rejected with 400. A header name may appear only " +
                "once across both maps (case-insensitive, 400 otherwise), and 'Authorization' in " +
                "'headerConfigurationKeys' cannot be combined with 'authorizationConfigurationKey' " +
                "or OAuth. 'headers' or 'headerConfigurationKeys' left out or null keeps the stored " +
                "map; '{}' removes it. Every other field is replaced. Changing 'endpoint' without " +
                "sending the maps keeps them, so the stored headers and the resolved credential " +
                "values go to the NEW address.");

        builder.MapDelete("/api/mcp-servers/{name}", async Task<Results<NoContent, ProblemHttpResult>> (
                string name,
                IMcpServerStore servers,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => await servers.DeleteAsync(tenants.TenantId, name, cancellationToken).ConfigureAwait(false)
                    ? TypedResults.NoContent()
                    : TypedResults.Problem(
                        title: "MCP server not found",
                        detail: $"There is no server named '{name}'.",
                        statusCode: StatusCodes.Status404NotFound))
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("TraconDeleteMcpServer")
            .WithTags("Tracon", "Governance")
            .WithSummary("Deletes a remote MCP server.")
            .WithDescription(
                "The registration is removed, so the tools it contributed stop being offered to " +
                "agents. Agent definitions that name those tools are not rewritten and will fail " +
                "validation on their next save — check which agents use the server before " +
                "removing it. No request is made to the remote server itself. An unknown name " +
                "returns 404.");

        builder.MapPost("/api/mcp-servers/refresh", async Task<Results<Ok<McpRefreshResponse>, ProblemHttpResult>> (
                IMcpToolRefresher? refresher,
                IAuditLog auditLog,
                IAuditActorResolver actorResolver,
                ITenantContext tenants,
                ILoggerFactory loggerFactory,
                [FromServices] TraconMetrics metrics,
                CancellationToken cancellationToken) =>
            {
                if (refresher is null)
                {
                    return TypedResults.Problem(
                        title: "MCP not registered",
                        detail: "Add the Tracon.Mcp package and call UseMcp() for tool discovery.",
                        statusCode: StatusCodes.Status501NotImplemented);
                }

                var outcome = await refresher.RefreshAsync(cancellationToken).ConfigureAwait(false);

                // A manual refresh is not a write to a registered server; that is why
                // the audit trail is written here, in the endpoint layer.
                await AuditRecorder.WriteAsync(
                    auditLog,
                    actorResolver,
                    loggerFactory.CreateLogger("Tracon.GovernanceEndpoints"),
                    metrics,
                    tenants.TenantId,
                    action: "mcp.refresh",
                    entity: "mcp:*",
                    before: null,
                    after: AuditPayload.Write(writer => writer.WriteNumber("toolCount", outcome.ToolCount)),
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(new McpRefreshResponse { ToolCount = outcome.ToolCount });
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("TraconRefreshMcpTools")
            .WithTags("Tracon", "Governance")
            .WithSummary("Refreshes the tool list of remote MCP servers now.")
            .WithDescription(
                "The refresh normally happens in the background at fixed intervals. This endpoint " +
                "lets the tools of a newly added server appear without waiting for the next " +
                "scheduled refresh.");
    }

    private static void MapMcpPrompts(IEndpointRouteBuilder builder, TraconRolePolicies roles)
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
            .WithName("TraconListMcpPrompts")
            .WithTags("Tracon", "Governance")
            .WithSummary("Gets an MCP server's prompt list.")
            .WithDescription("If the server does not advertise the 'prompts' capability, the request is never sent.");

        builder.MapPost("/api/mcp-servers/{name}/prompts/{prompt}", async Task<Results<Ok<McpPromptContent>, ProblemHttpResult>> (
                string name,
                string prompt,
                HttpContext httpContext,
                [FromServices] IMcpPromptClient? prompts,
                ITenantContext tenants,
                CancellationToken cancellationToken) =>
            {
                if (prompts is null)
                {
                    return McpNotRegisteredProblem();
                }

                var (request, bindError) = await RequestBodyBinding
                    .ReadOptionalAsync<McpPromptArgumentsRequest>(httpContext, cancellationToken)
                    .ConfigureAwait(false);

                if (bindError is not null)
                {
                    return bindError;
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
            .WithName("TraconGetMcpPrompt")
            .WithTags("Tracon", "Governance")
            .WithSummary("Resolves an MCP prompt's content with arguments.")
            .Accepts<McpPromptArgumentsRequest>(true, "application/json")
            .WithDescription(
                "The returned content is a SNAPSHOT: it must be copied into the agent's instructions; " +
                "it is not re-fetched at runtime. The 'hash' field is for tracking changes on the server.");
    }

    private static void MapMcpResources(IEndpointRouteBuilder builder, TraconRolePolicies roles)
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
            .WithName("TraconListMcpResources")
            .WithTags("Tracon", "Governance")
            .WithSummary("Gets an MCP server's resource list.")
            .WithDescription("If the server does not advertise the 'resources' capability, the request is never sent.");

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
                    McpOperationStatus.ItemNotFound => McpItemNotFoundProblem("Resource", uri),
                    _ => McpConnectionFailedProblem(name),
                };
            })
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconReadMcpResource")
            .WithTags("Tracon", "Governance")
            .WithSummary("Reads an MCP resource.")
            .WithDescription(
                "Only URIs advertised by the server's ListResourcesAsync are accepted; " +
                "an arbitrary URI is rejected because it carries an SSRF risk.");
    }

    private static void MapMcpOAuthStart(IEndpointRouteBuilder builder, TraconRolePolicies roles)
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
                        title: "OAuth not configured",
                        detail: "OAuth is not enabled on the server, the flow is not Authorization Code, " +
                                "or Tracon:Mcp:OAuthCallbackBaseUri is not set.",
                        statusCode: StatusCodes.Status409Conflict),
                    _ => McpConnectionFailedProblem(name),
                };
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconStartMcpOAuth")
            .WithTags("Tracon", "Governance")
            .WithSummary("Starts the OAuth authorization flow for an MCP server.")
            .WithDescription(
                "The administrator is redirected to the returned 'authorizationUri'. After approval, " +
                "the provider redirects back to the '/oauth/callback' endpoint; that endpoint uses the 'state' value for CSRF protection.");
    }

    private static ProblemHttpResult McpNotRegisteredProblem()
        => TypedResults.Problem(
            title: "MCP not registered",
            detail: "Add the Tracon.Mcp package and call UseMcp().",
            statusCode: StatusCodes.Status501NotImplemented);

    private static ProblemHttpResult McpServerNotFoundProblem(string name)
        => TypedResults.Problem(
            title: "MCP server not found",
            detail: $"There is no server named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult McpCapabilityUnsupportedProblem(string name, string capability)
        => TypedResults.Problem(
            title: "Server does not support this",
            detail: $"Server '{name}' does not advertise the '{capability}' capability.",
            statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult McpConnectionFailedProblem(string name)
        => TypedResults.Problem(
            title: "Could not connect to server",
            detail: $"Server '{name}' could not be reached, or the request failed.",
            statusCode: StatusCodes.Status502BadGateway);

    private static ProblemHttpResult McpUriNotDeclaredProblem(string uri)
        => TypedResults.Problem(
            title: "Resource not declared",
            detail: $"'{uri}' is not in this server's resource list.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult McpItemNotFoundProblem(string kind, string name)
        => TypedResults.Problem(
            title: $"{kind} not found",
            detail: $"'{name}' does not exist on the server.",
            statusCode: StatusCodes.Status404NotFound);

    private static void MapApprovalRules(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/approvals/rules", async Task<Ok<IReadOnlyList<ToolApprovalRule>>> (
                IToolApprovalRuleStore rules,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => TypedResults.Ok(await rules.ListAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false)))
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListApprovalRules")
            .WithTags("Tracon", "Governance")
            .WithSummary("Lists persistent 'don't ask again' approval rules.")
            .WithDescription(
                "Each rule pre-approves a tool call so it never reaches the approval mailbox " +
                "again, which makes this list a standing grant worth reviewing. A rule with no " +
                "agent name applies to every agent in the tenant. When it carries an arguments " +
                "hash the rule matches only that exact call; without one it matches every call " +
                "to that tool. Rules do not expire — remove one to start asking again.");

        builder.MapPost("/api/approvals/rules", async Task<Results<Created<ToolApprovalRule>, ProblemHttpResult>> (
                HttpContext httpContext,
                IToolApprovalRuleStore rules,
                ITenantContext tenants,
                IAuditActorResolver actorResolver,
                CancellationToken cancellationToken) =>
            {
                var (bound, bindError) = await RequestBodyBinding
                    .ReadAsync<ToolApprovalRuleRequest>(httpContext, cancellationToken)
                    .ConfigureAwait(false);

                if (bindError is not null)
                {
                    return bindError;
                }

                var request = bound!;

                if (ValidateRule(request) is { } invalid)
                {
                    return invalid;
                }

                var id = TraconId.NewId();

                var saved = await rules.AddAsync(
                    new ToolApprovalRule
                    {
                        Id = id,
                        TenantId = tenants.TenantId,
                        AgentName = request.AgentName,
                        ToolName = request.ToolName,
                        ArgumentConditions = request.ArgumentConditions,
                        CreatedBy = actorResolver.Resolve(),
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                    cancellationToken).ConfigureAwait(false);

                // AddAsync is an idempotent upsert (see IToolApprovalRuleStore.AddAsync):
                // a matching rule for the same scope already existed and its EXISTING id
                // (not the one just generated above) came back. The "remember this
                // decision" flow (ToolApprovalResolver) relies on that idempotence and
                // must stay silent; this admin-authored path surfaces it as a conflict
                // instead, so a second identical rule is never mistaken for a fresh one.
                if (saved.Id != id)
                {
                    return TypedResults.Problem(
                        title: "Rule already exists",
                        detail: "A rule for the same tool, agent, and conditions is already registered.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                return TypedResults.Created($"/api/approvals/rules/{saved.Id}", saved);
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconCreateApprovalRule")
            .WithTags("Tracon", "Governance")
            .WithSummary("Creates a persistent, argument-conditioned approval rule.")
            .Accepts<ToolApprovalRuleRequest>("application/json")
            .WithDescription(
                "Writes a standing 'don't ask again' rule with an admin-authored comparison " +
                "(for example \"amount <= 100\"), evaluated on every call. There is no free-text " +
                "expression field: the operator is a closed set and conditions combine with AND " +
                "only. A code-defined policy (ITraconBuilder.AddToolApprovalPolicy) always " +
                "runs first and can override this rule in both directions.");

        builder.MapDelete("/api/approvals/rules/{ruleId:guid}", async Task<Results<NoContent, ProblemHttpResult>> (
                Guid ruleId,
                IToolApprovalRuleStore rules,
                ITenantContext tenants,
                CancellationToken cancellationToken)
                => await rules.DeleteAsync(tenants.TenantId, ruleId, cancellationToken).ConfigureAwait(false)
                    ? TypedResults.NoContent()
                    : TypedResults.Problem(
                        title: "Rule not found",
                        detail: $"There is no approval rule with id '{ruleId}'.",
                        statusCode: StatusCodes.Status404NotFound))
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconDeleteApprovalRule")
            .WithTags("Tracon", "Governance")
            .WithSummary("Revokes a persistent approval rule.")
            .WithDescription("After the rule is deleted, approval is asked again for that tool.");
    }

    private static ProblemHttpResult? ValidateRule(ToolApprovalRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return TypedResults.Problem(
                title: "Tool name empty",
                detail: "'toolName' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ArgumentConditions.Count > ToolArgumentConditionLimits.MaxConditions)
        {
            return TypedResults.Problem(
                title: "Too many conditions",
                detail: $"A rule can carry at most {ToolArgumentConditionLimits.MaxConditions} conditions.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        foreach (var condition in request.ArgumentConditions)
        {
            if (ValidateCondition(condition) is { } invalid)
            {
                return invalid;
            }
        }

        return null;
    }

    private static ProblemHttpResult? ValidateCondition(ToolArgumentCondition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.Path) || condition.Path.Length > ToolArgumentConditionLimits.MaxPathLength)
        {
            return TypedResults.Problem(
                title: "Invalid condition path",
                detail: $"'path' must be non-empty and at most {ToolArgumentConditionLimits.MaxPathLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (condition.Path.Split('.', StringSplitOptions.RemoveEmptyEntries).Length > ToolArgumentConditionLimits.MaxPathSegments)
        {
            return TypedResults.Problem(
                title: "Invalid condition path",
                detail: $"'path' can carry at most {ToolArgumentConditionLimits.MaxPathSegments} dotted segments.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        switch (condition.Operator)
        {
            case ToolArgumentOperator.Equals:
            case ToolArgumentOperator.NotEquals:
                if (condition.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
                {
                    return InvalidValueProblem(condition.Operator, "text, number, or boolean");
                }

                break;

            case ToolArgumentOperator.GreaterThan:
            case ToolArgumentOperator.GreaterThanOrEqual:
            case ToolArgumentOperator.LessThan:
            case ToolArgumentOperator.LessThanOrEqual:
                if (condition.Value.ValueKind != JsonValueKind.Number)
                {
                    return InvalidValueProblem(condition.Operator, "a number");
                }

                break;

            case ToolArgumentOperator.In:
            case ToolArgumentOperator.NotIn:
                if (condition.Value.ValueKind != JsonValueKind.Array)
                {
                    return InvalidValueProblem(condition.Operator, "an array of text or numbers");
                }

                var count = 0;

                foreach (var element in condition.Value.EnumerateArray())
                {
                    if (++count > ToolArgumentConditionLimits.MaxListLength)
                    {
                        return TypedResults.Problem(
                            title: "List too long",
                            detail: $"An 'in'/'notIn' value can carry at most {ToolArgumentConditionLimits.MaxListLength} entries.",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    if (element.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                    {
                        return InvalidValueProblem(condition.Operator, "an array of text or numbers");
                    }
                }

                break;

            default:
                return TypedResults.Problem(
                    title: "Unknown operator",
                    detail: $"'{condition.Operator}' is not a recognized operator.",
                    statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static ProblemHttpResult InvalidValueProblem(ToolArgumentOperator op, string expected)
        => TypedResults.Problem(
            title: "Invalid condition value",
            detail: $"Operator '{op}' expects {expected}.",
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Returns a definition the way every response carries it: header names only.</summary>
    /// <param name="server">The stored definition.</param>
    /// <returns>A copy whose header values are all masked; the stored record is not changed.</returns>
    private static McpServerDefinition MaskHeaders(McpServerDefinition server)
        => server with { Headers = HeaderValueMask.Apply(server.Headers) };

    /// <summary>Applies the shared header rules and the Authorization conflict rule to an MCP save.</summary>
    /// <remarks>
    /// The conflict is judged on the EFFECTIVE map: a form that writes
    /// <c>authorizationConfigurationKey</c> while the stored record names
    /// <c>Authorization</c> in <c>headerConfigurationKeys</c> would otherwise
    /// store both.
    /// </remarks>
    private static ProblemHttpResult? ValidateHeaders(
        McpServerRequest request,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> headerKeys,
        string tenantId,
        string defaultTenantId,
        TraconMcpSecurityOptions mcpSecurity)
    {
        if (CredentialHeaderSaveRules.Validate(
                request.Headers,
                request.HeaderConfigurationKeys,
                headers,
                headerKeys,
                mcpSecurity.AllowedConfigurationPrefix,
                tenantId,
                defaultTenantId,
                isReserved: null) is { } rejection)
        {
            return TypedResults.Problem(
                title: rejection.Title,
                detail: rejection.Detail,
                statusCode: StatusCodes.Status400BadRequest);
        }

        // A stored row may still carry a plain Authorization header (it was
        // saveable before the credential rule, and a save that leaves 'headers'
        // out keeps it). With OAuth on, the MCP client writes its bearer token
        // only when the request has no Authorization header yet, so the kept
        // header would silently replace OAuth on every request.
        if (request.OAuthEnabled &&
            headers.Keys.Any(static header => string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase)))
        {
            return TypedResults.Problem(
                title: "Conflicting authentication",
                detail: "The stored 'headers' carry 'Authorization' while OAuth is enabled; OAuth manages that " +
                        "header and would never send its token. Send 'headers' in the same save without " +
                        "'Authorization' (or '{}').",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!headerKeys.Keys.Any(static header => string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

#pragma warning disable CS0618 // The deprecated field is judged against its replacement (phase 190).
        var legacyKey = request.AuthorizationConfigurationKey;
#pragma warning restore CS0618

        if (!string.IsNullOrEmpty(legacyKey))
        {
            return TypedResults.Problem(
                title: "Conflicting authentication",
                detail: "'headerConfigurationKeys' names 'Authorization' and 'authorizationConfigurationKey' " +
                        "is set; both would manage the same header. Keep one — " +
                        "'authorizationConfigurationKey' is deprecated.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.OAuthEnabled)
        {
            return TypedResults.Problem(
                title: "Conflicting authentication",
                detail: "'headerConfigurationKeys' names 'Authorization' while OAuth is enabled; OAuth " +
                        "manages the Authorization header itself.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    /// <summary>
    /// Logs when a save moves a server to another host while keeping its stored
    /// header maps without the request sending them.
    /// </summary>
    /// <remarks>
    /// The admin form edits the address but never shows the maps, so the
    /// operator cannot see that the headers and the credential values they
    /// resolve now go to the new host. The tenant key space rule keeps the key names inside the
    /// tenant, so this is not an escalation; the log makes it visible. Names
    /// only — a value is never logged.
    /// </remarks>
    private static void WarnWhenPreservedHeadersMove(
        ILogger logger,
        string name,
        McpServerDefinition? previous,
        Uri endpoint,
        McpServerRequest request)
    {
        if (previous is null
            || string.Equals(previous.Endpoint.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var kept = new List<string>();

        if (request.Headers is null)
        {
            kept.AddRange(previous.Headers.Keys);
        }

        if (request.HeaderConfigurationKeys is null)
        {
            kept.AddRange(previous.HeaderConfigurationKeys.Keys);
        }

        if (kept.Count > 0 && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "MCP server '{ServerName}' moved from host '{PreviousHost}' to '{Host}' and kept its stored headers " +
                "({HeaderNames}); they are now sent to the new host.",
                name,
                previous.Endpoint.Host,
                endpoint.Host,
                string.Join(", ", kept));
        }
    }

    private static ProblemHttpResult? Validate(
        string name,
        McpServerRequest request,
        string tenantId,
        string defaultTenantId,
        TraconEgressOptions egress,
        TraconMcpSecurityOptions mcpSecurity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return TypedResults.Problem(
                title: "Server name empty",
                detail: "The path name is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return TypedResults.Problem(
                title: "Address invalid",
                detail: "'endpoint' must be an absolute address.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Local process (stdio) transport is deliberately unsupported: starting a
        // process on the server would mean anyone with UI access could run programs on the server.
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                title: "Address scheme not supported",
                detail: "Only http and https are accepted. Local process (stdio) transport is " +
                        "deliberately unsupported; starting a process on the server would break " +
                        "the rule that tools are defined in code only.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // 🚨 A read masks every header value, so a client doing read-modify-
        // write would otherwise store the mask over the real value and break
        // the server's authentication without a single error.
        if (HeaderValueMask.FindMaskedHeader(request.Headers) is { } maskedHeader)
        {
            return TypedResults.Problem(
                title: "Header value masked",
                detail: HeaderValueMask.DescribeRejection(maskedHeader),
                statusCode: StatusCodes.Status400BadRequest);
        }

        // 🚨 SSRF: an IP literal is judged here, at save time. A host NAME is
        // not resolved (it would slow the save down and a name that does not
        // resolve yet is not an error); that check happens on every
        // connection, inside EgressSocketGuard.
        if (EgressAddressValidator.ValidateLiteral(
                endpoint,
                new EgressAddressPolicy(egress.AllowPrivateNetworkTargets, AllowLoopback: false)) is { } addressReason)
        {
            return TypedResults.Problem(
                title: "Address not allowed",
                detail: addressReason,
                statusCode: StatusCodes.Status400BadRequest);
        }

        // 🚨 The definition carries no secret, only the NAME of the key its
        // value is read from (K-059). Without a prefix restriction that name
        // could point at any configuration key in the application; without
        // the tenant segment it could point at another tenant's key.
#pragma warning disable CS0618 // The deprecated field keeps its key space rule until 1.0.0 (phase 190).
        var legacyAuthorizationKey = request.AuthorizationConfigurationKey;
#pragma warning restore CS0618

        if (RequireTenantKey(
                legacyAuthorizationKey,
                mcpSecurity.AllowedConfigurationPrefix,
                tenantId,
                defaultTenantId,
                "authorizationConfigurationKey") is { } authKeyProblem)
        {
            return authKeyProblem;
        }

        if (RequireTenantKey(
                request.OAuthClientSecretConfigurationKey,
                mcpSecurity.AllowedConfigurationPrefix,
                tenantId,
                defaultTenantId,
                "oauthClientSecretConfigurationKey") is { } secretKeyProblem)
        {
            return secretKeyProblem;
        }

        if (request.OAuthEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.OAuthClientId))
            {
                return TypedResults.Problem(
                    title: "OAuth client id missing",
                    detail: "'oauthClientId' is required when OAuth is enabled.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!string.IsNullOrEmpty(legacyAuthorizationKey))
            {
                return TypedResults.Problem(
                    title: "Conflicting authentication",
                    detail: "'authorizationConfigurationKey' must be empty when OAuth is enabled; " +
                            "both would try to manage the same Authorization header.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return null;
    }

    /// <summary>
    /// Rejects a configuration key name that sits outside the allowed prefix
    /// or outside the caller's tenant. An empty name is allowed: the field
    /// itself is optional.
    /// </summary>
    private static ProblemHttpResult? RequireTenantKey(
        string? configurationKeyName,
        string allowedPrefix,
        string tenantId,
        string defaultTenantId,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(configurationKeyName))
        {
            return null;
        }

        try
        {
            ConfigurationKeyGuard.RequireTenantKey(
                configurationKeyName,
                allowedPrefix,
                tenantId,
                defaultTenantId,
                fieldName);

            return null;
        }
        catch (TraconException exception)
        {
            return TypedResults.Problem(
                title: "Configuration key not allowed",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
