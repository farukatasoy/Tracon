using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether the request header can name the tenant with nothing to
/// verify it against.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>UseTenancy(...)</c> next to <see cref="TenancyResolutionProfileCheck"/>
/// and for the same reason: the HTTP package is the one that sees the setting. It is
/// permissive in exactly one composition: tenancy is on, the header path is open and no
/// claim type takes precedence. With a claim type set the header is never read, so the
/// header path being open is harmless.
/// </para>
/// <para>
/// An API key already fixes the tenant of its own request (the header cannot override it),
/// so the risk is about every other caller. A gateway that writes the header in front of
/// Tracon is a reason to accept the risk by name, not to leave it unasked.
/// </para>
/// </remarks>
internal sealed class TenantHeaderProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnverifiedTenantHeader;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Named as the call, not as a configuration key, for the reason TenancyResolutionProfileCheck
        // gives: nothing binds TraconTenancyOptions from configuration.
        const string Setting = "UseTenancy(options => options.AllowHeaderResolution)";

        var options = services.GetRequiredService<IOptions<TraconTenancyOptions>>().Value;

        if (!options.Enabled)
        {
            return ProductionProfileResult.NotApplicable(
                Setting,
                "tenant resolution is off, so the request's tenant is never read");
        }

        return options.ClaimType is { Length: > 0 } || !options.AllowHeaderResolution
            ? ProductionProfileResult.Satisfied(Setting)
            : ProductionProfileResult.Permissive(
                Setting,
                "on with no claim type, so any client that reaches the endpoint names the tenant it acts as " +
                "in a request header that nothing verifies",
                "set options.ClaimType so the tenant comes from the authenticated identity, or turn the " +
                "header path off. Accept the risk only when a gateway in front of Tracon writes the header; " +
                "a request authenticated with an API key keeps that key's tenant either way");
    }
}
