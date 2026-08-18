namespace AgentPrism;

/// <summary>
/// The result of a pre-flight context-window check, computed without calling
/// the model provider.
/// </summary>
public sealed record ContextWindowEstimate
{
    /// <summary>Gets the estimated token count of the prompt. The estimate is approximate.</summary>
    public required int PromptTokens { get; init; }

    /// <summary>
    /// Gets the model's context window size, taken from
    /// <see cref="ModelDescriptor.ContextWindowTokens"/>.
    /// <see langword="null"/> when the model is not found in the catalog.
    /// </summary>
    public required int? ContextWindowTokens { get; init; }

    /// <summary>
    /// Gets the token budget kept for the prompt after
    /// <see cref="AgentPrismPreflightOptions.ReserveRatio"/> is set aside for
    /// the answer. <see langword="null"/> when <see cref="ContextWindowTokens"/> is unknown.
    /// </summary>
    public required int? AllowedPromptTokens { get; init; }

    /// <summary>
    /// Gets whether a real run with this prompt would be rejected by the
    /// pre-flight check. Always <see langword="false"/> when
    /// <see cref="ContextWindowTokens"/> is unknown — an unknown window can never
    /// be exceeded.
    /// </summary>
    public required bool WouldBeRejected { get; init; }
}
