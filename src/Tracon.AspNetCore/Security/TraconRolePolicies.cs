using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

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
/// </remarks>
internal sealed class TraconRolePolicies
{
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

        var reader = IsRegistered(provider, TraconPolicies.Reader) ? TraconPolicies.Reader : null;
        var operatorPolicy = IsRegistered(provider, TraconPolicies.Operator) ? TraconPolicies.Operator : null;
        var admin = IsRegistered(provider, TraconPolicies.Admin) ? TraconPolicies.Admin : null;

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
                throw new InvalidOperationException(
                    "TraconEndpointOptions.RequireRolePolicies is on but these policies are " +
                    $"not registered: {string.Join(", ", missing)}. Define the " +
                    "TraconPolicies.Reader/Operator/Admin names inside " +
                    "builder.Services.AddAuthorization(...), or turn RequireRolePolicies off.");
            }
        }

        return new TraconRolePolicies(reader, operatorPolicy, admin);
    }

    private static bool IsRegistered(IAuthorizationPolicyProvider? provider, string policyName)
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
            return false;
        }
    }
}
