using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether anything actually inspects prompts and responses.
/// </summary>
/// <remarks>
/// <para>
/// Content inspection is a REGISTRATION before it is a setting:
/// <c>AddTracon()</c> registers no guard and never adds the inspecting wrapper
/// to the model pipeline. Asking an option whether inspection is on would
/// therefore answer a question nobody asked - the collection is what installs
/// the wrapper.
/// </para>
/// <para>
/// The registration is necessary and NOT sufficient. Both inspection switches
/// default to on, but a deployment can turn both off and keep the guard
/// registered: the wrapper is then installed and inspects nothing on every
/// call, which is the risk this item names. One direction turned off is a
/// different matter - that is a setting someone had to write, so the decision
/// was taken; the question here is whether content is inspected at all.
/// </para>
/// </remarks>
internal sealed class ContentGuardProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UninspectedContent;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.GetServices<IContentGuard>().Any())
        {
            return ProductionProfileResult.Permissive(
                nameof(IContentGuard),
                "no content guard is registered, so no prompt or response is inspected and the " +
                "inspecting wrapper is never added to the model pipeline",
                "call AddPatternContentGuard(...) or AddContentGuard<TGuard>() on the Tracon chain, " +
                $"or populate the {TraconContentGuardOptions.SectionName}:Pattern configuration section");
        }

        var settings = services.GetRequiredService<IOptions<TraconContentGuardOptions>>().Value;

        if (!settings.InspectInput && !settings.InspectOutput)
        {
            var input = $"{TraconContentGuardOptions.SectionName}:{nameof(TraconContentGuardOptions.InspectInput)}";
            var output = $"{TraconContentGuardOptions.SectionName}:{nameof(TraconContentGuardOptions.InspectOutput)}";

            return ProductionProfileResult.Permissive(
                $"{input} and {output}",
                "a content guard is registered, but neither input nor output is inspected, so the " +
                "wrapper runs on every call and looks at nothing",
                $"set {input} or {output} back to true, or remove the guard registration if this " +
                "deployment inspects nothing on purpose");
        }

        return ProductionProfileResult.Satisfied(nameof(IContentGuard));
    }
}
