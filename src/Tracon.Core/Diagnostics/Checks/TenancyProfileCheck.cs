using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Answers whether the deployment separates tenants, by asking which
/// <see cref="ITenantContext"/> the container actually bound.
/// </summary>
/// <remarks>
/// The binding is the honest question at this layer. Tenant resolution is an
/// HTTP concern and its settings live in the HTTP package, which the core
/// cannot see; but the CONSEQUENCE — every call running as one default tenant —
/// is visible here, and it is visible in an embedded host too, where an HTTP
/// setting would not exist at all. The HTTP package adds a second check for the
/// case this one cannot see: its own context bound, with resolution switched
/// off.
/// </remarks>
internal sealed class TenancyProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.SingleTenant;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var bound = services.GetRequiredService<ITenantContext>().GetType();
        var builtIn = TraconExtensionPoints.BuiltInDefaultOf(typeof(ITenantContext));

        return bound == builtIn
            ? ProductionProfileResult.Permissive(
                nameof(ITenantContext),
                $"resolves to Tracon's built-in {bound.Name}, so every call runs as the default tenant",
                "register your own ITenantContext before the AddTracon() call, or on an ASP.NET Core host " +
                "call UseTenancy(options => options.Enabled = true)")
            : ProductionProfileResult.Satisfied(nameof(ITenantContext));
    }
}
