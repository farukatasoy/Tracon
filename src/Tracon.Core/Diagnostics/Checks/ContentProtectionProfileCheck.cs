using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether recorded content is protected at rest.
/// </summary>
/// <remarks>
/// Only the flag is read. The key material and the key ids are deliberately
/// never touched here: everything this type returns is written into a startup
/// exception and into the log.
/// </remarks>
internal sealed class ContentProtectionProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnencryptedContentAtRest;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var setting = $"{TraconContentProtectionOptions.SectionName}:{nameof(TraconContentProtectionOptions.Enabled)}";

        return services.GetRequiredService<IOptions<TraconContentProtectionOptions>>().Value.Enabled
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                "off, so prompts, responses and tool arguments are stored as clear text",
                $"call AddContentProtection(...) or set {setting} to true together with an active key id");
    }
}
