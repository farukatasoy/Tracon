using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether anything decides which tool call may run: a handler of the deployment's own, or the
/// per-tenant tool policy made mandatory.
/// </summary>
/// <remarks>
/// <para>
/// Asked in every composition, a single-tenant one included: tool authorization
/// does not depend on tenants. Whoever can start a run can have the agent call any
/// tool it has, and the built-in handler is the one that allows all of them.
/// </para>
/// <para>
/// Two answers satisfy it (phase 261). A handler the container bound that is not Tracon's built-in one, which
/// is the deployment's own code rule. Or <see cref="TraconToolOptions.RequireTenantPolicy"/>, which makes a tenant
/// with no tool policy run no tool, so no tenant runs a tool nobody allowed. The first answer needs no
/// policy; the second needs a policy per tenant and no code.
/// </para>
/// </remarks>
internal sealed class ToolAuthorizationProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UncheckedToolCalls;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var bound = services.GetRequiredService<IToolAuthorizationHandler>().GetType();
        var builtIn = TraconExtensionPoints.BuiltInDefaultOf(typeof(IToolAuthorizationHandler));

        if (bound != builtIn)
        {
            return ProductionProfileResult.Satisfied(nameof(IToolAuthorizationHandler));
        }

        const string Setting = TenantToolPolicyGate.RequirePolicySetting;

        return services.GetRequiredService<IOptions<TraconOptions>>().Value.Tools.RequireTenantPolicy
            ? ProductionProfileResult.Satisfied(Setting)
            : ProductionProfileResult.Permissive(
                nameof(IToolAuthorizationHandler),
                $"resolves to Tracon's built-in {bound.Name}, so every tool call is allowed for every caller",
                "register your own IToolAuthorizationHandler before the AddTracon() call, so it decides " +
                $"which caller may run which tool, or set {Setting} to true and save a tool policy for each " +
                "tenant, the default tenant included. A tenant without a policy then runs no tool");
    }
}
