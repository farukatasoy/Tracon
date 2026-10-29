using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether the request-rate limit is counted for the whole deployment.
/// </summary>
/// <remarks>
/// Not applicable while request rate is not limited: <see cref="TraconProductionRisk.UnlimitedRequestRate"/>
/// asks that first, and a limit that is off has no scope to get wrong. Whether the counter store is one that
/// instances share is <see cref="TraconProductionRisk.InstanceLocalState"/>'s question.
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

        if (!options.Enabled)
        {
            return ProductionProfileResult.NotApplicable(
                setting,
                $"{TraconRateLimitOptions.SectionName}:{nameof(TraconRateLimitOptions.Enabled)} is off, so no limit is counted");
        }

        return options.Scope == RateLimitScope.Deployment
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                $"{nameof(RateLimitScope.Instance)} with {instances} instances declared, so each instance counts in its " +
                $"own memory and the deployment admits the limit {instances} times over",
                $"set {setting} to {nameof(RateLimitScope.Deployment)}; the counter then lives in the SQL store and each " +
                "request costs one database write");
    }
}
