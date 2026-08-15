using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Combines the database policy with the configuration default for a target.</summary>
/// <remarks>
/// Order: first the explicit record in <see cref="IRetentionPolicyStore"/>
/// (tenant, then <c>"*"</c>) is looked up; if found, configuration is NOT
/// consulted at all. If there is no record, <see cref="AgentPrismRetentionOptions"/>
/// takes over, but only while <see cref="AgentPrismRetentionOptions.Enabled"/> is on.
/// </remarks>
public sealed class RetentionPolicyResolver(
    IRetentionPolicyStore policyStore,
    IOptionsMonitor<AgentPrismRetentionOptions> optionsMonitor)
{
    /// <summary>Resolves the effective retention rule for a target.</summary>
    /// <param name="tenantId">The tenant identity.</param>
    /// <param name="target">The target name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The effective rule; <see langword="null"/> if nothing is to be deleted.</returns>
    public async ValueTask<ResolvedRetentionPolicy?> ResolveAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var dbPolicy = await policyStore.GetPolicyAsync(tenantId, target, cancellationToken).ConfigureAwait(false);

        if (dbPolicy is not null)
        {
            // If an explicit record exists, configuration is NOT consulted at
            // all — even if the record is "disabled", that is the consumer's
            // deliberate choice.
            return dbPolicy.Enabled && (dbPolicy.MaxAgeDays is not null || dbPolicy.MaxRows is not null)
                ? new ResolvedRetentionPolicy(target, dbPolicy.MaxAgeDays, dbPolicy.MaxRows, dbPolicy.Archive)
                : null;
        }

        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return null;
        }

        var fallback = options.ForTarget(target);

        // Configuration-based defaults carry only MaxAgeDays (K1: MaxRows does
        // not grow into the configuration surface, it only comes through an
        // explicit policy).
        return fallback?.MaxAgeDays is { } fallbackDays
            ? new ResolvedRetentionPolicy(target, fallbackDays, null, fallback.Archive)
            : null;
    }
}

/// <summary>Represents a resolved, applicable retention rule for a target.</summary>
/// <param name="Target">The target name.</param>
/// <param name="MaxAgeDays">Rows older than this age are deleted. <see langword="null"/> if there is no age-based threshold.</param>
/// <param name="MaxRows">The maximum number of rows to keep. <see langword="null"/> if there is no volume-based threshold.</param>
/// <param name="Archive">Whether to archive rows before deleting them.</param>
public sealed record ResolvedRetentionPolicy(string Target, int? MaxAgeDays, long? MaxRows, bool Archive);
