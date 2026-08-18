using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AgentPrism.Api;

/// <summary>
/// Role names the demonstration scheme understands.
/// </summary>
internal static class DemoRoles
{
    /// <summary>Read-only access.</summary>
    public const string Reader = "agentprism-reader";

    /// <summary>Reader plus starting a run and giving an approval.</summary>
    public const string Operator = "agentprism-operator";

    /// <summary>Full access.</summary>
    public const string Admin = "agentprism-admin";
}

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY. NEVER USE THIS IN PRODUCTION.</strong>
/// </summary>
/// <remarks>
/// <para>
/// This handler turns a plain request header into a role claim. It performs NO
/// identity verification: anybody who can reach the endpoint can name their own
/// role. A production deployment binds
/// <see cref="AgentPrismPolicies"/> to a real identity provider (OpenID Connect,
/// JWT bearer, Windows authentication) instead.
/// </para>
/// <para>
/// It exists for one reason: without an identity source no
/// <c>RequireRole(...)</c> call in AgentPrism can be OBSERVED in this reference
/// application, so the role model — which is enforced and tested inside the
/// library — looked like it did nothing here. It is off by default and turns on
/// only with <c>AgentPrism:Demo:Roles:Enabled</c>.
/// </para>
/// </remarks>
internal sealed class DemoRoleAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The scheme name.</summary>
    public const string SchemeName = "AgentPrismDemoRole";

    /// <summary>The header that carries the role.</summary>
    public const string RoleHeader = "X-AgentPrism-Demo-Role";

    public DemoRoleAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers[RoleHeader] is not [{ Length: > 0 } role, ..])
        {
            // No header means no identity. The endpoint then answers 401 when a
            // role policy protects it, and stays reachable when none does.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var normalized = role.Trim().ToLowerInvariant() switch
        {
            "reader" or DemoRoles.Reader => DemoRoles.Reader,
            "operator" or DemoRoles.Operator => DemoRoles.Operator,
            "admin" or DemoRoles.Admin => DemoRoles.Admin,
            _ => null,
        };

        if (normalized is null)
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"'{RoleHeader}' must be one of: reader, operator, admin."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, $"demo-{normalized}"), new Claim(ClaimTypes.Role, normalized)],
            SchemeName);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
