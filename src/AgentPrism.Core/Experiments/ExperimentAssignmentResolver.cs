using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>
/// Deterministically assigns a run request to a bucket, when a running
/// experiment exists for an agent.
/// </summary>
/// <remarks>
/// The assignment is <strong>deterministic</strong>: the same key always
/// produces the same bucket. A random assignment would change the
/// instructions mid-conversation.
/// </remarks>
internal sealed class ExperimentAssignmentResolver
{
    private readonly IExperimentStore _store;

    /// <summary>Creates a new assignment resolver.</summary>
    /// <param name="store">Experiment store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public ExperimentAssignmentResolver(IExperimentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Produces an assignment for the given key, when a running experiment
    /// exists for this agent.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="agentName">Agent name.</param>
    /// <param name="assignmentKey">Assignment key (session id or run id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assignment; <see langword="null"/> when there is no running experiment.</returns>
    public async ValueTask<ExperimentAssignment?> ResolveAsync(
        string tenantId,
        string agentName,
        string assignmentKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(agentName);
        ArgumentNullException.ThrowIfNull(assignmentKey);

        var experiment = await _store.GetRunningAsync(tenantId, agentName, cancellationToken).ConfigureAwait(false);

        if (experiment is null)
        {
            return null;
        }

        var variant = SelectVariant(experiment, assignmentKey);

        return new ExperimentAssignment
        {
            ExperimentId = experiment.Id,
            Variant = variant.Name,
            Version = variant.Version,
        };
    }

    /// <summary>
    /// Produces a bucket in the 0-99 range from the first 4 bytes of
    /// SHA-256(experimentId + ":" + key), and returns the variant that falls
    /// within that weight range.
    /// </summary>
    /// <remarks><c>internal</c> so unit tests can call it directly.</remarks>
    internal static ExperimentVariant SelectVariant(Experiment experiment, string assignmentKey)
    {
        var input = $"{experiment.Id:D}:{assignmentKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        // The first 4 bytes, interpreted as big-endian and reduced to the 0-99 range.
        var bucket = (uint)((hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3]) % 100;

        var cumulative = 0;

        foreach (var variant in OrderForAssignment(experiment))
        {
            cumulative += variant.Weight;

            if (bucket < cumulative)
            {
                return variant;
            }
        }

        // If the weights do not sum to 100 (normally impossible since this is
        // validated at registration time), the last variant is used as a fallback.
        return experiment.Variants[^1];
    }

    /// <summary>
    /// Variant order used for the bucket range computation.
    /// </summary>
    /// <remarks>
    /// When <see cref="Experiment.Canary"/> is defined, the canary
    /// bucket is ALWAYS processed FIRST and thus takes the
    /// <c>[0, canaryWeight)</c> range - this range is INDEPENDENT of the
    /// physical order in <see cref="Experiment.Variants"/>. Since ramping up
    /// only raises the canary weight, this range only GROWS; a key once
    /// assigned to the canary never SHIFTS to control.
    /// When there is NO canary policy (i.e. plain A/B experiments
    /// referenced above), the order is unchanged.
    /// </remarks>
    private static IReadOnlyList<ExperimentVariant> OrderForAssignment(Experiment experiment)
    {
        if (experiment.Canary is not { } policy)
        {
            return experiment.Variants;
        }

        var canary = experiment.Variants.FirstOrDefault(
            variant => string.Equals(variant.Name, policy.CanaryVariant, StringComparison.Ordinal));

        if (canary is null)
        {
            return experiment.Variants;
        }

        return
        [
            canary,
            .. experiment.Variants.Where(variant => !string.Equals(variant.Name, policy.CanaryVariant, StringComparison.Ordinal)),
        ];
    }
}
