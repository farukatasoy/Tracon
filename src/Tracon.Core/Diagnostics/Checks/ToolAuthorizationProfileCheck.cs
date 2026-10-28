using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Answers whether anything decides which tool call may run, by asking which
/// <see cref="IToolAuthorizationHandler"/> the container actually bound.
/// </summary>
/// <remarks>
/// Asked in every composition, a single-tenant one included: tool authorization
/// does not depend on tenants. Whoever can start a run can have the agent call any
/// tool it has, and the built-in handler is the one that allows all of them.
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

        return bound == builtIn
            ? ProductionProfileResult.Permissive(
                nameof(IToolAuthorizationHandler),
                $"resolves to Tracon's built-in {bound.Name}, so every tool call is allowed for every caller",
                "register your own IToolAuthorizationHandler before the AddTracon() call, so it decides " +
                "which caller may run which tool")
            : ProductionProfileResult.Satisfied(nameof(IToolAuthorizationHandler));
    }
}
