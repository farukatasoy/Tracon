using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether a run is refused when the quota store cannot be read.
/// </summary>
/// <remarks>
/// With enforcement on, the default lets a run through when the store fails, so a tenant with a ceiling
/// goes past it during an outage. Both answers are legitimate (an outage that stops every run is its own
/// failure), which is why this is a decision a host makes by name and not a default that changes.
/// </remarks>
internal sealed class QuotaProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnenforcedQuotaOnStoreFailure;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.GetRequiredService<IOptions<TraconQuotaOptions>>().Value;
        var setting = $"{TraconQuotaOptions.SectionName}:{nameof(TraconQuotaOptions.AllowOnStoreFailure)}";

        if (!options.Enabled)
        {
            return ProductionProfileResult.NotApplicable(
                setting,
                $"{TraconQuotaOptions.SectionName}:{nameof(TraconQuotaOptions.Enabled)} is off, so no run is checked against a quota and no store failure can let one past it");
        }

        return options.AllowOnStoreFailure
            ? ProductionProfileResult.Permissive(
                setting,
                "true, so a run goes ahead when the quota store fails and a tenant past its ceiling is not stopped while the database is down",
                $"set {setting} to false to refuse runs whose quota cannot be read, or accept the risk by name when " +
                "an outage of the quota store must not stop the service")
            : ProductionProfileResult.Satisfied(setting);
    }
}
