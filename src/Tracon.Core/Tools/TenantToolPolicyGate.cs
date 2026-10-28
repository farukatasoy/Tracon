using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The per-tenant tool policy check that runs in front of <see cref="IToolAuthorizationHandler"/> (phase 261).
/// </summary>
/// <remarks>
/// <para>
/// A separate gate and not a built-in handler: a consumer who registers their own handler replaces the
/// built-in one, and a policy that lived there would never be read for that host while the endpoints that write
/// it kept answering. Here both gates must allow a call. The policy is the administrator's data rule (per
/// tenant); the handler is the installation's code rule (user, role, time).
/// </para>
/// <para>
/// Read at call time, once per call, with no cache. A policy that is saved applies to the next call; a stale
/// policy is a security defect and not a performance concern (the compiled-agent cache is untouched: the gate
/// is not part of what an agent compiles to).
/// </para>
/// </remarks>
internal sealed class TenantToolPolicyGate
{
    /// <summary>The configuration key that makes a tenant with no tool policy run no tool.</summary>
    public const string RequirePolicySetting = "Tracon:Tools:RequireTenantPolicy";

    private readonly ITenantToolPolicyStore _store;
    private readonly IOptions<TraconOptions> _options;

    /// <summary>Creates the gate.</summary>
    /// <param name="store">The tenant tool policy store.</param>
    /// <param name="options">
    /// The Tracon options. Read through <see cref="IOptions{TOptions}"/>, so the value is fixed when the host starts,
    /// as the production profile check sees it.
    /// </param>
    public TenantToolPolicyGate(ITenantToolPolicyStore store, IOptions<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _options = options;
    }

    /// <summary>Decides whether a tenant's policy lets a call to a tool through.</summary>
    /// <param name="tenantId">The tenant making the call.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="requiredPermission">The permission the tool declares, or <see langword="null"/> when it declares none.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decision. A refusal carries the text the model reads as the tool's result.</returns>
    /// <remarks>
    /// A store that throws makes this method throw. The caller turns that into a refusal (fail-closed) and logs it.
    /// </remarks>
    public async ValueTask<ToolAuthorizationResult> CheckAsync(
        string tenantId,
        string toolName,
        string? requiredPermission,
        CancellationToken cancellationToken)
    {
        var policy = await _store
            .GetAsync(AmbientTenantScope.Normalize(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return Decide(policy, toolName, requiredPermission, _options.Value.Tools.RequireTenantPolicy);
    }

    /// <summary>The rule of the gate, apart from the store.</summary>
    /// <param name="policy">The tenant's policy; <see langword="null"/> when it has none.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="requiredPermission">The permission the tool declares, or <see langword="null"/>.</param>
    /// <param name="requirePolicy">Whether a tenant with no policy may run no tool.</param>
    /// <returns>The decision.</returns>
    internal static ToolAuthorizationResult Decide(
        TenantToolPolicy? policy,
        string toolName,
        string? requiredPermission,
        bool requirePolicy)
    {
        if (policy is null)
        {
            // The text names the setting and nothing else, so the operator who reads a run learns why a tenant
            // that was never restricted is refused, and what turns it back. It carries no tenant id and no value.
            return requirePolicy
                ? ToolAuthorizationResult.Deny(
                    $"This tool is not enabled for this tenant: the tenant has no tool policy and {RequirePolicySetting} is on.")
                : ToolAuthorizationResult.Allow();
        }

        if (Contains(policy.AllowedTools, toolName)
            || (!string.IsNullOrEmpty(requiredPermission) && Contains(policy.GrantedPermissions, requiredPermission)))
        {
            return ToolAuthorizationResult.Allow();
        }

        return ToolAuthorizationResult.Deny("This tool is not enabled for this tenant.");
    }

    private static bool Contains(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
