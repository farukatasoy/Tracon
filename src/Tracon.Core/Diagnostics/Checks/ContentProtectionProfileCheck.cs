using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether recorded content is protected at rest.
/// </summary>
/// <remarks>
/// <para>
/// The protector the stores use is asked, not the flag. <c>AddTracon()</c> registers a protector that
/// writes the text unchanged, and only <c>AddContentProtection(...)</c> replaces it, so a host that
/// sets <c>Tracon:ContentProtection:Enabled</c> and never makes the call stores clear text while the
/// flag reads <c>true</c>. The SQL stores ask the same member
/// (<see cref="IContentProtector.IsEnabled"/>) before they protect a column. A protector of the
/// consumer's own answers the decision too, whatever the flag says: it owns its keys.
/// </para>
/// <para>
/// The flag is read only to word the answer. The key material and the key ids are deliberately
/// never touched here: everything this type returns is written into a startup exception and into
/// the log.
/// </para>
/// </remarks>
internal sealed class ContentProtectionProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnencryptedContentAtRest;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetRequiredService<IContentProtector>().IsEnabled)
        {
            return ProductionProfileResult.Satisfied(nameof(IContentProtector));
        }

        var setting = $"{TraconContentProtectionOptions.SectionName}:{nameof(TraconContentProtectionOptions.Enabled)}";
        var flagOn = services.GetRequiredService<IOptions<TraconContentProtectionOptions>>().Value.Enabled;

        return ProductionProfileResult.Permissive(
            setting,
            flagOn
                ? "true, but the content protector in use is not on (the flag alone installs none; AddContentProtection() does), so prompts, responses and tool arguments are stored as clear text"
                : "off, so prompts, responses and tool arguments are stored as clear text",
            $"call AddContentProtection(options => ...) with an active key id, or call AddContentProtection() and set {setting} to true together with an active key id");
    }
}
