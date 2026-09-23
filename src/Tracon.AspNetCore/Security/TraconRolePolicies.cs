using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

/// <summary>
/// Resolves once, during the <c>MapTracon()</c> call, which role policies
/// (<see cref="TraconPolicies"/>) are registered in the authorization configuration of
/// the consumer.
/// </summary>
/// <remarks>
/// <para>
/// When a policy is not registered, the matching property returns <see langword="null"/> and
/// <see cref="RoleEndpointConventionBuilderExtensions.RequireRole"/> adds no authorization —
/// the endpoint passes only the existing three-layer protection (the earlier behavior).
/// </para>
/// <para>
/// The resolution uses <see cref="IAuthorizationPolicyProvider.GetPolicyAsync(string)"/>.
/// The default provider answers this query synchronously from a dictionary inside
/// <c>AuthorizationOptions</c> (<c>Task.FromResult</c>); because <c>MapTracon()</c> is
/// itself called after <c>app.Build()</c> and outside request processing,
/// <c>GetAwaiter().GetResult()</c> is safe here.
/// </para>
/// <para>
/// A provider that THROWS for a role name is logged as a warning (category
/// <c>Tracon.RolePolicies</c>) and the name is treated as not registered.
/// With <see cref="TraconEndpointOptions.RequireRolePolicies"/> on, the
/// startup failure carries the provider's exception as its inner exception.
/// </para>
/// </remarks>
internal sealed class TraconRolePolicies
{
    /// <summary>The log category of a role policy that could not be resolved.</summary>
    private const string LoggerCategory = "Tracon.RolePolicies";

    private TraconRolePolicies(string? reader, string? operatorPolicy, string? admin)
    {
        Reader = reader;
        Operator = operatorPolicy;
        Admin = admin;
    }

    /// <summary>Gets <see cref="TraconPolicies.Reader"/> when it is registered, otherwise <see langword="null"/>.</summary>
    public string? Reader { get; }

    /// <summary>Gets <see cref="TraconPolicies.Operator"/> when it is registered, otherwise <see langword="null"/>.</summary>
    public string? Operator { get; }

    /// <summary>Gets <see cref="TraconPolicies.Admin"/> when it is registered, otherwise <see langword="null"/>.</summary>
    public string? Admin { get; }

    /// <summary>
    /// Resolves the registration state of the role policies. When
    /// <see cref="TraconEndpointOptions.RequireRolePolicies"/> is on and a policy is
    /// missing, it fails at startup.
    /// </summary>
    /// <param name="services">The built service provider.</param>
    /// <param name="options">The endpoint settings.</param>
    /// <exception cref="InvalidOperationException">
    /// When <see cref="TraconEndpointOptions.RequireRolePolicies"/> is on but one or more
    /// role policies are not registered.
    /// </exception>
    public static TraconRolePolicies Resolve(IServiceProvider services, TraconEndpointOptions options)
    {
        var provider = services.GetService<IAuthorizationPolicyProvider>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;
        var failures = new List<Exception>(3);

        var reader = IsRegistered(provider, TraconPolicies.Reader, logger, failures) ? TraconPolicies.Reader : null;
        var operatorPolicy = IsRegistered(provider, TraconPolicies.Operator, logger, failures) ? TraconPolicies.Operator : null;
        var admin = IsRegistered(provider, TraconPolicies.Admin, logger, failures) ? TraconPolicies.Admin : null;

        if (options.RequireRolePolicies)
        {
            var missing = new List<string>(3);

            if (reader is null)
            {
                missing.Add(TraconPolicies.Reader);
            }

            if (operatorPolicy is null)
            {
                missing.Add(TraconPolicies.Operator);
            }

            if (admin is null)
            {
                missing.Add(TraconPolicies.Admin);
            }

            if (missing.Count > 0)
            {
                // The first provider failure travels as the inner exception: a
                // startup that says "not registered" while the real cause was a
                // provider that threw would send the operator to the wrong fix.
                throw new InvalidOperationException(
                    "TraconEndpointOptions.RequireRolePolicies is on but these policies are " +
                    $"not registered: {string.Join(", ", missing)}. Define the " +
                    "TraconPolicies.Reader/Operator/Admin names inside " +
                    "builder.Services.AddAuthorization(...), or turn RequireRolePolicies off." +
                    (failures.Count > 0
                        ? " The IAuthorizationPolicyProvider threw while resolving at least one of " +
                          "them; see the inner exception."
                        : string.Empty),
                    failures.Count > 0 ? failures[0] : null);
            }
        }

        return new TraconRolePolicies(reader, operatorPolicy, admin);
    }

    private static bool IsRegistered(
        IAuthorizationPolicyProvider? provider,
        string policyName,
        ILogger logger,
        List<Exception> failures)
    {
        if (provider is null)
        {
            return false;
        }

        try
        {
            return provider.GetPolicyAsync(policyName).GetAwaiter().GetResult() is not null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Kept on the "not registered" side (the documented fallback), so a
            // provider that throws for an unknown name does not stop the host.
            // It is not the same answer as "not registered", though, and the
            // role check this endpoint loses must not disappear silently.
            logger.LogWarning(
                ex,
                "The IAuthorizationPolicyProvider threw while resolving the '{PolicyName}' role policy. " +
                "The policy is treated as not registered, so the endpoints it guards get no role check.",
                policyName);

            failures.Add(ex);

            return false;
        }
    }
}
