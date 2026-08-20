using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Helper that checks the pre-flight context-window estimate before a run
/// starts and produces a <c>400</c> when the prompt would be rejected.
/// </summary>
/// <remarks>
/// <para>
/// Same call shape as <see cref="QuotaGate"/> — an explicit check at every
/// endpoint that starts a run, not an endpoint filter, for the same reason
/// (the OpenAI-compatible endpoints carry the agent name in the body, not the route).
/// </para>
/// <para>
/// <strong>Disabled by default</strong> (the no-surprises rule,
/// <see cref="AgentPrismPreflightOptions.Enabled"/>): when it is off, this
/// method returns immediately and neither resolves the agent's model binding
/// nor counts a single token — not even an <see langword="if"/> beyond the
/// disabled check runs on the hot path.
/// </para>
/// </remarks>
internal static class PreflightGate
{
    /// <summary>Checks the estimate; produces the response to return if the prompt would be rejected.</summary>
    /// <param name="optionsMonitor">The runtime settings.</param>
    /// <param name="estimator">The estimator.</param>
    /// <param name="catalog">The agent catalog, used to resolve the agent's model binding.</param>
    /// <param name="agentName">The name of the agent to run.</param>
    /// <param name="prompt">The prompt text to estimate. <see langword="null"/> counts as empty.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The <c>400</c> response to return if the prompt would be rejected;
    /// otherwise <see langword="null"/>.
    /// </returns>
    public static async ValueTask<IResult?> CheckAsync(
        IOptionsMonitor<AgentPrismOptions> optionsMonitor,
        ContextWindowEstimator estimator,
        IAgentCatalog catalog,
        string agentName,
        string? prompt,
        CancellationToken cancellationToken)
    {
        if (!optionsMonitor.CurrentValue.Preflight.Enabled)
        {
            return null;
        }

        // A code agent or one with no resolvable model binding cannot be
        // estimated; the check silently passes rather than rejecting a run it
        // has no basis to judge (the same "unresolvable window never
        // rejects" rule ContextWindowEstimator itself follows).
        if (await AgentEndpoints.FindDescriptorAsync(catalog, agentName, cancellationToken).ConfigureAwait(false)
                is not { Model: { } binding })
        {
            return null;
        }

        var estimate = estimator.Estimate(binding, prompt);

        if (!estimate.WouldBeRejected)
        {
            return null;
        }

        return Results.Problem(
            title: "Prompt too large for the model's context window",
            detail: $"The prompt is estimated at {estimate.PromptTokens} tokens; the '{agentName}' agent's " +
                     $"model allows at most {estimate.AllowedPromptTokens} tokens for the prompt " +
                     $"(context window {estimate.ContextWindowTokens}, reserved for the answer: " +
                     $"{optionsMonitor.CurrentValue.Preflight.ReserveRatio:P0}). No call was made to the provider.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["promptTokens"] = estimate.PromptTokens,
                ["contextWindowTokens"] = estimate.ContextWindowTokens,
                ["allowedPromptTokens"] = estimate.AllowedPromptTokens,
            });
    }
}
