using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether a session records the user it belongs to.
/// </summary>
/// <remarks>
/// The remedy is worth the words it takes: rows written while ownership is off
/// keep no owner forever, so turning it on later narrows future listings but
/// cannot repair the ones already written.
/// </remarks>
internal sealed class SessionOwnershipProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnownedSessions;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var setting = $"{TraconSessionOwnershipOptions.SectionName}:{nameof(TraconSessionOwnershipOptions.Enabled)}";

        return services.GetRequiredService<IOptions<TraconSessionOwnershipOptions>>().Value.Enabled
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                "off, so no session is stamped with an owner and no listing is narrowed to one",
                $"set {setting} to true. Sessions written while it is off keep no owner, so turn it on " +
                "before the data you want to narrow is written");
    }
}
