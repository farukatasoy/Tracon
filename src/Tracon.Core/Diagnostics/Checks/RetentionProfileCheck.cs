using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether anything deletes recorded data as it ages.
/// </summary>
/// <remarks>
/// The flag governs the CONFIGURATION defaults only; a policy row in the store
/// still applies without it. The remedy therefore names both routes rather than
/// claiming the flag is the only way to answer the decision.
/// </remarks>
internal sealed class RetentionProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnboundedRetention;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var setting = $"{TraconRetentionOptions.SectionName}:{nameof(TraconRetentionOptions.Enabled)}";

        return services.GetRequiredService<IOptions<TraconRetentionOptions>>().Value.Enabled
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                "off, so no configured retention default deletes anything and recorded data grows without a bound",
                $"set {setting} to true and give each target a maximum age, or write an explicit policy " +
                "per target through IRetentionPolicyStore");
    }
}
