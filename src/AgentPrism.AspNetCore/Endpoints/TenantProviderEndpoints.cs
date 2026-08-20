using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Tenant provider binding (BYOK) and egress policy endpoints.
/// </summary>
/// <remarks>
/// No response here ever carries a credential value — only the
/// configuration key's <strong>name</strong> and whether it currently
/// resolves. Unlike <see cref="ApiKeyEndpoints"/>, the tenant is taken from
/// the ROUTE, not the ambient <see cref="ITenantContext"/>: these are
/// platform-administrator endpoints that manage any tenant's bindings, the
/// same shape as <c>GovernanceEndpoints</c>'s <c>PUT /api/tenants/{slug}</c>.
/// </remarks>
internal static class TenantProviderEndpoints
{
    /// <summary>Maps the tenant provider binding and egress policy endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/tenants/{tenantId}/providers", ListAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismListTenantProviderBindings")
            .WithTags("AgentPrism", "TenantProviders")
            .WithSummary("Lists a tenant's model provider bindings.")
            .WithDescription(
                "The response carries neither the credential value nor its configuration " +
                "key's value — only the key's NAME and whether it currently resolves " +
                "('resolved'). This is the diagnosis path for 'I set the key but it does not work'.");

        builder.MapPut("/api/tenants/{tenantId}/providers/{provider}", SaveBindingAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismSaveTenantProviderBinding")
            .WithTags("AgentPrism", "TenantProviders")
            .WithSummary("Creates or replaces a tenant's binding for a provider.")
            .Accepts<TenantProviderBindingRequest>("application/json")
            .WithDescription(
                "The body carries only the configuration key's NAME the value is read from " +
                "at call time, never the value itself. The name must be under the configured " +
                "allowed prefix (400 otherwise), and the provider must be allowed by the " +
                "tenant's egress policy, if one is defined (400 otherwise).");

        builder.MapDelete("/api/tenants/{tenantId}/providers/{provider}", DeleteBindingAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismDeleteTenantProviderBinding")
            .WithTags("AgentPrism", "TenantProviders")
            .WithSummary("Deletes a tenant's binding for a provider.")
            .WithDescription("After deletion, calls for that provider use the setup-time global credential again.");

        builder.MapGet("/api/tenants/{tenantId}/egress", GetEgressPolicyAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismGetTenantEgressPolicy")
            .WithTags("AgentPrism", "TenantProviders")
            .WithSummary("Returns a tenant's model provider egress policy.")
            .WithDescription(
                "'allowedProviders: null' means the tenant is UNRESTRICTED (no policy saved); " +
                "an empty or populated array means the tenant may call only those providers.");

        builder.MapPut("/api/tenants/{tenantId}/egress", SaveEgressPolicyAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismSaveTenantEgressPolicy")
            .WithTags("AgentPrism", "TenantProviders")
            .WithSummary("Creates or replaces a tenant's egress policy.")
            .Accepts<TenantEgressPolicyRequest>("application/json")
            .WithDescription(
                "Saving a policy is an ADDITIVE restriction: a tenant with no policy is " +
                "unrestricted, and this call is the only way that changes. An agent definition " +
                "naming a provider outside the saved list is rejected at compile time, not only " +
                "at call time. An empty 'allowedProviders' array allows NO provider — it is not " +
                "the same as having no policy; use DELETE to return to unrestricted.");

        builder.MapDelete("/api/tenants/{tenantId}/egress", DeleteEgressPolicyAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismDeleteTenantEgressPolicy")
            .WithTags("AgentPrism", "TenantProviders")
            .WithSummary("Deletes a tenant's egress policy.")
            .WithDescription("After deletion the tenant is unrestricted again — the same state as before any policy was ever saved.");
    }

    private static async Task<Ok<IReadOnlyList<TenantProviderBindingResponse>>> ListAsync(
        string tenantId,
        [FromServices] ITenantProviderBindingStore store,
        [FromServices] TenantProviderCredentialResolver resolver,
        CancellationToken cancellationToken)
    {
        var bindings = await store.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<TenantProviderBindingResponse>>(
            [.. bindings.Select(binding => Describe(binding, resolver))]);
    }

    private static async Task<Results<Ok<TenantProviderBindingResponse>, ProblemHttpResult>> SaveBindingAsync(
        string tenantId,
        string provider,
        HttpContext httpContext,
        [FromServices] ITenantProviderBindingStore store,
        [FromServices] ITenantEgressPolicyStore egressPolicies,
        [FromServices] TenantProviderCredentialResolver resolver,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IOptionsMonitor<AgentPrismEgressOptions> egressOptions,
        CancellationToken cancellationToken)
    {
        if (!HttpTenantContext.IsValidTenantId(tenantId))
        {
            return Invalid("Invalid tenant key.");
        }

        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<TenantProviderBindingRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (string.IsNullOrWhiteSpace(request.ApiKeyConfigurationName))
        {
            return Invalid("'apiKeyConfigurationName' cannot be empty.");
        }

        try
        {
            // Defense in two layers (section 65.2): the resolver validates the
            // SAME prefix rule again while resolving at call time.
            resolver.ValidatePrefix(request.ApiKeyConfigurationName);
        }
        catch (AgentPrismException ex)
        {
            return Invalid(ex.Message);
        }

        // 🚨 SSRF: the endpoint override is tenant input, and the resolved API
        // key is sent to whatever it names. An IP literal is judged here; a
        // host NAME is judged on every connection, inside EgressSocketGuard.
        if (request.Endpoint is { Length: > 0 } endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            {
                return Invalid("'endpoint' must be an absolute address.");
            }

            var addressPolicy = new EgressAddressPolicy(
                egressOptions.CurrentValue.AllowPrivateNetworkTargets,
                AllowLoopback: false);

            if (EgressAddressValidator.ValidateLiteral(endpointUri, addressPolicy) is { } addressReason)
            {
                return Invalid(addressReason);
            }
        }

        var policy = await egressPolicies.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (policy is not null && !policy.AllowedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            return Invalid(
                $"Tenant '{tenantId}' is not allowed to call model provider '{provider}'. " +
                $"Allowed providers: {string.Join(", ", policy.AllowedProviders)}.");
        }

        var binding = new TenantProviderBinding
        {
            TenantId = tenantId,
            ProviderName = provider,
            ApiKeyConfigurationName = request.ApiKeyConfigurationName,
            Endpoint = request.Endpoint,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await store.UpsertAsync(binding, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.TenantProviderEndpoints"),
            tenantId,
            action: "tenant_provider.save",
            entity: $"tenant_provider:{tenantId}:{provider}",
            before: null,
            after: DescribeForAudit(binding),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(Describe(binding, resolver));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteBindingAsync(
        string tenantId,
        string provider,
        [FromServices] ITenantProviderBindingStore store,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(tenantId, provider, cancellationToken).ConfigureAwait(false);

        if (!deleted)
        {
            return NotFound(tenantId, provider);
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.TenantProviderEndpoints"),
            tenantId,
            action: "tenant_provider.delete",
            entity: $"tenant_provider:{tenantId}:{provider}",
            before: null,
            after: null,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<Ok<TenantEgressPolicyResponse>> GetEgressPolicyAsync(
        string tenantId,
        [FromServices] ITenantEgressPolicyStore store,
        CancellationToken cancellationToken)
    {
        var policy = await store.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new TenantEgressPolicyResponse
        {
            TenantId = tenantId,
            AllowedProviders = policy?.AllowedProviders,
            UpdatedAt = policy?.UpdatedAt,
        });
    }

    private static async Task<Results<Ok<TenantEgressPolicyResponse>, ProblemHttpResult>> SaveEgressPolicyAsync(
        string tenantId,
        HttpContext httpContext,
        [FromServices] ITenantEgressPolicyStore store,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!HttpTenantContext.IsValidTenantId(tenantId))
        {
            return Invalid("Invalid tenant key.");
        }

        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<TenantEgressPolicyRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var allowedProviders = bound!.AllowedProviders ?? [];

        var policy = await store.UpsertAsync(tenantId, allowedProviders, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.TenantProviderEndpoints"),
            tenantId,
            action: "tenant_egress.save",
            entity: $"tenant_egress:{tenantId}",
            before: null,
            after: AuditPayload.WriteArray("allowedProviders", allowedProviders),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new TenantEgressPolicyResponse
        {
            TenantId = policy.TenantId,
            AllowedProviders = policy.AllowedProviders,
            UpdatedAt = policy.UpdatedAt,
        });
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteEgressPolicyAsync(
        string tenantId,
        [FromServices] ITenantEgressPolicyStore store,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (!deleted)
        {
            return TypedResults.Problem(
                title: "Policy not found",
                detail: $"Tenant '{tenantId}' has no saved egress policy.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.TenantProviderEndpoints"),
            tenantId,
            action: "tenant_egress.delete",
            entity: $"tenant_egress:{tenantId}",
            before: null,
            after: null,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static TenantProviderBindingResponse Describe(TenantProviderBinding binding, TenantProviderCredentialResolver resolver)
        => new()
        {
            ProviderName = binding.ProviderName,
            ApiKeyConfigurationName = binding.ApiKeyConfigurationName,
            Endpoint = binding.Endpoint,
            Resolved = TryResolve(binding, resolver),
            UpdatedAt = binding.UpdatedAt,
        };

    private static bool TryResolve(TenantProviderBinding binding, TenantProviderCredentialResolver resolver)
    {
        try
        {
            return resolver.Resolve(binding) is not null;
        }
        catch (AgentPrismException)
        {
            // The prefix was valid when saved but the allowed prefix setting changed
            // since; the endpoint reports this the same way as a missing value.
            return false;
        }
    }

    /// <summary>Summarizes a binding for the audit trail.</summary>
    /// <remarks>
    /// NO credential value or resolved status — only the name of the
    /// configuration key. The JSON property is deliberately named
    /// <c>configKeyName</c>, not <c>apiKeyConfigurationName</c>:
    /// <see cref="AuditSecretFilter"/> blanket-redacts any property whose name
    /// contains "apikey" regardless of its value, and the configuration key's
    /// name is exactly the diagnostic detail the audit trail exists to keep.
    /// </remarks>
    private static string DescribeForAudit(TenantProviderBinding binding)
        => AuditPayload.Write(writer =>
        {
            writer.WriteString("providerName", binding.ProviderName);
            writer.WriteString("configKeyName", binding.ApiKeyConfigurationName);
        });

    private static ProblemHttpResult NotFound(string tenantId, string provider)
        => TypedResults.Problem(
            title: "Binding not found",
            detail: $"Tenant '{tenantId}' has no binding for provider '{provider}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult Invalid(string detail)
        => TypedResults.Problem(
            title: "Invalid request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>The response describing a tenant provider binding.</summary>
/// <remarks>Carries no credential value, only the configuration key's NAME.</remarks>
public sealed record TenantProviderBindingResponse
{
    /// <summary>Gets the provider name.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets the configuration key name the credential is read from.</summary>
    public required string ApiKeyConfigurationName { get; init; }

    /// <summary>Gets the optional endpoint override.</summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Gets whether <c>ApiKeyConfigurationName</c> currently resolves to a value.
    /// </summary>
    public required bool Resolved { get; init; }

    /// <summary>Gets the time this binding was last written.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>The request body for creating or replacing a tenant provider binding.</summary>
public sealed record TenantProviderBindingRequest
{
    /// <summary>Gets the configuration key name the credential is read from. Never a value.</summary>
    public string? ApiKeyConfigurationName { get; init; }

    /// <summary>Gets the optional endpoint override.</summary>
    public string? Endpoint { get; init; }
}

/// <summary>The response describing a tenant's egress policy.</summary>
public sealed record TenantEgressPolicyResponse
{
    /// <summary>Gets the tenant this policy belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the closed set of allowed provider names, or <see langword="null"/>
    /// when the tenant is unrestricted (no policy saved).
    /// </summary>
    public IReadOnlyList<string>? AllowedProviders { get; init; }

    /// <summary>Gets the time the policy was last written; <see langword="null"/> when unrestricted.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>The request body for creating or replacing a tenant's egress policy.</summary>
public sealed record TenantEgressPolicyRequest
{
    /// <summary>Gets the closed set of allowed provider names. An empty list allows no provider.</summary>
    public IReadOnlyList<string>? AllowedProviders { get; init; }
}
