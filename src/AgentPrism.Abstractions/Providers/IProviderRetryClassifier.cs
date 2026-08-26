namespace AgentPrism;

/// <summary>Decides whether a model-provider failure is retryable on the next fallback link.</summary>
/// <remarks>
/// <para>
/// Called by <c>FallbackChatClient</c>, once per failed attempt, before it
/// falls back to AgentPrism's own closed-set rules. A cancellation
/// (<see cref="OperationCanceledException"/>, anywhere in the exception
/// graph) never reaches this classifier — that check runs before the seam
/// and cannot be overridden.
/// </para>
/// <para>
/// AgentPrism's taxonomy is its own opinion; a consumer may want to trust an
/// additional SDK-specific exception. Registered with <c>TryAddSingleton</c>,
/// so the consumer's registration wins.
/// </para>
/// </remarks>
public interface IProviderRetryClassifier
{
    /// <summary>
    /// Classifies the failure. Return <see cref="ProviderRetryDecision.Unknown"/>
    /// to defer to AgentPrism's built-in rules.
    /// </summary>
    /// <param name="exception">The exception a model call threw.</param>
    /// <returns>The retry decision.</returns>
    ProviderRetryDecision Classify(Exception exception);
}
