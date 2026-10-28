using System.ComponentModel;
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
/// Per-tenant tool policy endpoints: which tools a tenant may run.
/// </summary>
/// <remarks>
/// The tenant is taken from the ROUTE, like <see cref="TenantProviderEndpoints"/>: an operator of the installation
/// manages any tenant's policy through them, and a route tenant other than the caller's own requires platform
/// authority (<see cref="CrossTenantAuthority"/>). The policy is read at call time by the tool chain; saving one
/// applies to the next tool call.
/// </remarks>
internal static class TenantToolPolicyEndpoints
{
    /// <summary>
    /// The longest a tool name or a permission may be, in characters. A tool name cannot be longer than 64
    /// (<c>ToolNameRules</c>); no permission limit exists, so the longest name the code accepts anywhere is used.
    /// </summary>
    internal const int MaxNameLength = 64;

    /// <summary>
    /// The most entries one list may carry. No limit exists for a list of names, and the 50 of
    /// <c>ToolArgumentConditionLimits.MaxListLength</c> bounds argument values, too small for a tenant that allows
    /// its MCP tools by name. 200 is the ceiling the tool list of the observability endpoint already uses.
    /// </summary>
    internal const int MaxEntries = 200;

    /// <summary>Maps the tenant tool policy endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/tenants/{tenantId}/tools", GetAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .RequirePlatformAuthorityForOtherTenant("tenantId")
            .WithName("TraconGetTenantToolPolicy")
            .WithTags("Tracon", "TenantProviders")
            .WithSummary("Returns a tenant's tool policy.")
            .WithDescription(
                "'allowedTools: null' and 'grantedPermissions: null' mean no policy is saved: the tenant is " +
                "UNRESTRICTED by the policy, unless Tracon:Tools:RequireTenantPolicy is on ('requirePolicy: true'), " +
                "which makes it run NO tool; two arrays, empty or populated, mean the tenant may run only the tools " +
                "they name. A call passes when its tool name is in 'allowedTools' or its tool declares a permission " +
                "that is in 'grantedPermissions'. The policy is a gate in front of IToolAuthorizationHandler, not a " +
                "replacement: a call needs both to allow it. A tenant other than the caller's own requires platform " +
                "authority (403 otherwise).");

        builder.MapPut("/api/tenants/{tenantId}/tools", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .RequirePlatformAuthorityForOtherTenant("tenantId")
            .WithName("TraconSaveTenantToolPolicy")
            .WithTags("Tracon", "TenantProviders")
            .WithSummary("Creates or replaces a tenant's tool policy.")
            .Accepts<TenantToolPolicyRequest>("application/json")
            .WithDescription(
                "Saving a policy is a RESTRICTION: a tenant with no policy is unrestricted (or, with " +
                "Tracon:Tools:RequireTenantPolicy on, may run no tool), and this call is the way that changes. A " +
                "missing or empty list is an empty list: two empty lists allow NO tool. That is not the same as having " +
                "no policy; use DELETE to remove the policy. Each entry is trimmed, must not be empty, must not hold a " +
                "control character and must be at most 64 characters; a list holds at most 200 entries and a repeated " +
                "entry is saved once. Names and permissions are compared exactly, with letter case. The policy covers " +
                "the tools IToolAuthorizationHandler is asked about: a tool registered in code, an MCP tool, the " +
                "file_access_* tools of a session workspace, search_knowledge and search_conversation_memory. A hosted " +
                "tool, an agent called as a tool, a skill tool, the file memory, todo and harness tools are not covered. " +
                "The 'tenantId' route value is folded to lower case before the policy is saved. A tenant other than the " +
                "caller's own requires platform authority (403 otherwise).");

        builder.MapDelete("/api/tenants/{tenantId}/tools", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .RequirePlatformAuthorityForOtherTenant("tenantId")
            .WithName("TraconDeleteTenantToolPolicy")
            .WithTags("Tracon", "TenantProviders")
            .WithSummary("Deletes a tenant's tool policy.")
            .WithDescription(
                "After deletion the tenant has no policy again: unrestricted by the policy, or, with " +
                "Tracon:Tools:RequireTenantPolicy on, able to run no tool. A tenant other than the caller's own " +
                "requires platform authority (403 otherwise).");
    }

    private static async Task<Ok<TenantToolPolicyResponse>> GetAsync(
        string tenantId,
        [FromServices] ITenantToolPolicyStore store,
        [FromServices] IOptions<TraconOptions> options,
        CancellationToken cancellationToken)
    {
        tenantId = HttpTenantContext.NormalizeRouteTenantId(tenantId);

        var policy = await store.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new TenantToolPolicyResponse
        {
            TenantId = tenantId,
            AllowedTools = policy?.AllowedTools,
            GrantedPermissions = policy?.GrantedPermissions,
            UpdatedAt = policy?.UpdatedAt,
            RequirePolicy = options.Value.Tools.RequireTenantPolicy,
        });
    }

    private static async Task<Results<Ok<TenantToolPolicyResponse>, ProblemHttpResult>> SaveAsync(
        string tenantId,
        HttpContext httpContext,
        [FromServices] ITenantToolPolicyStore store,
        [FromServices] IOptions<TraconOptions> options,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        tenantId = HttpTenantContext.NormalizeRouteTenantId(tenantId);

        if (!HttpTenantContext.IsValidTenantId(tenantId))
        {
            return Invalid("Invalid tenant key.");
        }

        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<TenantToolPolicyRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        if (!TryNormalize(bound!.AllowedTools, "allowedTools", out var allowedTools, out var allowedError))
        {
            return allowedError!;
        }

        if (!TryNormalize(bound.GrantedPermissions, "grantedPermissions", out var grantedPermissions, out var grantedError))
        {
            return grantedError!;
        }

        var policy = await store
            .UpsertAsync(tenantId, allowedTools, grantedPermissions, cancellationToken)
            .ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.TenantToolPolicyEndpoints"),
            metrics,
            tenantId,
            action: "tenant_tool_policy.save",
            entity: $"tenant_tool_policy:{tenantId}",
            before: null,
            after: DescribeForAudit(policy),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new TenantToolPolicyResponse
        {
            TenantId = policy.TenantId,
            AllowedTools = policy.AllowedTools,
            GrantedPermissions = policy.GrantedPermissions,
            UpdatedAt = policy.UpdatedAt,
            RequirePolicy = options.Value.Tools.RequireTenantPolicy,
        });
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string tenantId,
        [FromServices] ITenantToolPolicyStore store,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TraconMetrics metrics,
        CancellationToken cancellationToken)
    {
        tenantId = HttpTenantContext.NormalizeRouteTenantId(tenantId);

        var deleted = await store.DeleteAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (!deleted)
        {
            return TypedResults.Problem(
                title: "Policy not found",
                detail: $"Tenant '{tenantId}' has no saved tool policy.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.TenantToolPolicyEndpoints"),
            metrics,
            tenantId,
            action: "tenant_tool_policy.delete",
            entity: $"tenant_tool_policy:{tenantId}",
            before: null,
            after: null,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Trims every entry, refuses an empty one, a control character, a name that is too long or a list that is
    /// too long, and saves a repeated entry once.
    /// </summary>
    private static bool TryNormalize(
        IReadOnlyList<string>? values,
        string field,
        out List<string> normalized,
        out ProblemHttpResult? problem)
    {
        normalized = [];
        problem = null;

        if (values is null)
        {
            return true;
        }

        if (values.Count > MaxEntries)
        {
            problem = Invalid($"'{field}' can carry at most {MaxEntries} entries.");
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            var entry = value?.Trim();

            if (string.IsNullOrEmpty(entry))
            {
                problem = Invalid($"'{field}' cannot hold an empty entry.");
                return false;
            }

            if (entry.Length > MaxNameLength)
            {
                problem = Invalid($"An entry of '{field}' can be at most {MaxNameLength} characters.");
                return false;
            }

            if (entry.Any(char.IsControl))
            {
                problem = Invalid($"An entry of '{field}' cannot hold a control character.");
                return false;
            }

            if (seen.Add(entry))
            {
                normalized.Add(entry);
            }
        }

        return true;
    }

    /// <summary>Summarizes a policy for the audit trail: the two lists, which are names and never values.</summary>
    private static string DescribeForAudit(TenantToolPolicy policy)
        => AuditPayload.Write(writer =>
        {
            WriteList(writer, "allowedTools", policy.AllowedTools);
            WriteList(writer, "grantedPermissions", policy.GrantedPermissions);
        });

    private static void WriteList(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);

        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static ProblemHttpResult Invalid(string detail)
        => TypedResults.Problem(
            title: "Invalid request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

[Description("The response describing a tenant's tool policy.")]
internal sealed record TenantToolPolicyResponse
{
    [Description("Gets the tenant this policy belongs to.")]
    public required string TenantId { get; init; }

    [Description(
        "Gets the closed set of tool names the tenant may run, or `null`\n" +
        "when no policy is saved (unrestricted, unless `requirePolicy` is `true`).")]
    public IReadOnlyList<string>? AllowedTools { get; init; }

    [Description(
        "Gets the closed set of permissions granted to the tenant: a tool whose required permission is in\n" +
        "this set may run. `null` when no policy is saved.")]
    public IReadOnlyList<string>? GrantedPermissions { get; init; }

    [Description("Gets the time the policy was last written; `null` when no policy is saved.")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [Description(
        "Gets whether `Tracon:Tools:RequireTenantPolicy` is on, so a tenant with no policy runs no tool.\n" +
        "The flag is a setting of the host, not of the tenant.")]
    public required bool RequirePolicy { get; init; }
}

[Description("The request body for creating or replacing a tenant's tool policy.")]
internal sealed record TenantToolPolicyRequest
{
    [Description(
        "Gets the closed set of tool names the tenant may run. A missing or empty list names no tool.")]
    public IReadOnlyList<string>? AllowedTools { get; init; }

    [Description(
        "Gets the closed set of permissions granted to the tenant: a tool whose required permission is in\n" +
        "this set may run. A missing or empty list grants none.")]
    public IReadOnlyList<string>? GrantedPermissions { get; init; }
}
