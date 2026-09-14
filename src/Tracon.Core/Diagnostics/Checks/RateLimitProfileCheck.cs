using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Answers whether request rate is limited.</summary>
internal sealed class RateLimitProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnlimitedRequestRate;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var setting = $"{TraconRateLimitOptions.SectionName}:{nameof(TraconRateLimitOptions.Enabled)}";

        return services.GetRequiredService<IOptions<TraconRateLimitOptions>>().Value.Enabled
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                "off, so one caller can take as much of the deployment's model budget as it asks for",
                $"set {setting} to true and size the window. The counter is per process, so a " +
                "multi-instance deployment admits the limit once per instance");
    }
}
