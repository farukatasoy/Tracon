using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The base of the production decisions that exist only when the deployment declares more than one instance.
/// </summary>
/// <remarks>
/// <para>
/// With <see cref="TraconDeploymentOptions.Instances"/> at <c>1</c> (the default) every such decision is
/// not applicable, so a host that never declared a second instance is asked exactly what it was asked before
/// the declaration existed. Tracon cannot count replicas, so a deployment that runs two and does not say so
/// is not checked: the declaration is what turns the decisions on.
/// </para>
/// <para>
/// A decision stays one <see cref="TraconProductionRisk"/> value with its own <c>Accept</c> line. A check
/// derives from this class and answers only what is wrong when more than one instance shares the store.
/// </para>
/// </remarks>
internal abstract class InstanceProfileCheck : IProductionProfileCheck
{
    /// <summary>Gets the configuration key that declares the instance count.</summary>
    public static string DeclarationSetting { get; } =
        $"{TraconOptions.SectionName}:{nameof(TraconOptions.Deployment)}:{nameof(TraconDeploymentOptions.Instances)}";

    /// <inheritdoc />
    public abstract TraconProductionRisk Risk { get; }

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var instances = services.GetRequiredService<IOptions<TraconOptions>>().Value.Deployment.Instances;

        return instances > 1
            ? EvaluateShared(services, instances)
            : ProductionProfileResult.NotApplicable(
                DeclarationSetting,
                "one instance is declared, so nothing here is shared with another instance");
    }

    /// <summary>Answers the decision for a deployment that declared more than one instance.</summary>
    /// <param name="services">A scoped service provider.</param>
    /// <param name="instances">The declared instance count, always above one.</param>
    /// <returns>What the check found.</returns>
    protected abstract ProductionProfileResult EvaluateShared(IServiceProvider services, int instances);
}
