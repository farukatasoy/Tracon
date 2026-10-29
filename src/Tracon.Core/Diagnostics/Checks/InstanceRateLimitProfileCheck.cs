using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether the request-rate limits are counted for the whole deployment.
/// </summary>
/// <remarks>
/// <para>
/// Two limiters follow <see cref="TraconRateLimitOptions.Scope"/>: the one on the endpoint groups, which
/// <see cref="TraconRateLimitOptions.Enabled"/> switches, and the one on the inbound trigger accept endpoint, which is always
/// on. The decision is therefore asked whether or not the endpoint limit is on: with the scope at
/// <see cref="RateLimitScope.Instance"/> the trigger limit is counted per instance either way. A deployment that turned the
/// endpoint limit off and uses no inbound trigger accepts the risk by name.
/// </para>
/// <para>
/// Whether the counter store is one that instances share is <see cref="TraconProductionRisk.InstanceLocalState"/>'s question.
/// </para>
/// </remarks>
internal sealed class InstanceRateLimitProfileCheck : InstanceProfileCheck
{
    /// <inheritdoc />
    public override TraconProductionRisk Risk => TraconProductionRisk.InstanceLocalRateLimit;

    /// <inheritdoc />
    protected override ProductionProfileResult EvaluateShared(IServiceProvider services, int instances)
    {
        var options = services.GetRequiredService<IOptions<TraconRateLimitOptions>>().Value;
        var setting = $"{TraconRateLimitOptions.SectionName}:{nameof(TraconRateLimitOptions.Scope)}";

        if (options.Scope == RateLimitScope.Deployment)
        {
            return ProductionProfileResult.Satisfied(setting);
        }

        var limits = options.Enabled
            ? "the endpoint limit and the inbound trigger limit"
            : "the inbound trigger limit, which is always on (the endpoint limit is off)";

        return ProductionProfileResult.Permissive(
            setting,
            $"{nameof(RateLimitScope.Instance)} with {instances} instances declared, so each instance counts {limits} in its own " +
            $"memory and the deployment admits them {instances} times over",
            $"set {setting} to {nameof(RateLimitScope.Deployment)}; the counter then lives in the SQL store and each counted " +
            "request costs one database write. If the endpoint limit is off and no inbound trigger is used, accept the risk by name");
    }
}
