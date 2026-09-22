namespace Tracon;

/// <summary>
/// Tracon's built-in provider retry classifier — the same closed-set
/// rules <see cref="FallbackRetryClassifier"/> has always applied.
/// </summary>
/// <remarks>
/// Registered with <c>TryAddSingleton</c> as the default
/// <see cref="IProviderRetryClassifier"/>: a consumer that registers nothing
/// gets exactly this, so <c>FallbackChatClient</c>'s retry decisions stay
/// bit-for-bit identical to before the seam existed. Never returns
/// <see cref="ProviderRetryDecision.Unknown"/> — it <em>is</em> the rule the
/// seam falls back to, not a participant deferring to it.
/// </remarks>
internal sealed class DefaultProviderRetryClassifier : IProviderRetryClassifier
{
    /// <inheritdoc />
    public ProviderRetryDecision Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return FallbackRetryClassifier.IsRetryable(exception)
            ? ProviderRetryDecision.Retry
            : ProviderRetryDecision.DoNotRetry;
    }
}
