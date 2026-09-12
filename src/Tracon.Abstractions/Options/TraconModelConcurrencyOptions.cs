namespace Tracon;

/// <summary>
/// Defines the outgoing concurrency limit applied per model provider.
/// </summary>
/// <remarks>
/// <para>
/// The goal is to avoid producing a burst of <c>429</c> responses in the
/// first place; the circuit breaker only reacts once a provider is already
/// failing. A request over the limit waits (bounded by its own cancellation
/// token) instead of being rejected immediately — rejecting outright would
/// turn a short traffic spike into the very failures this option exists to prevent.
/// </para>
/// <para>
/// <strong>Unlimited by default</strong>: today's behavior is preserved
/// exactly when this is not configured, and the hot path allocates nothing extra.
/// </para>
/// </remarks>
public sealed class TraconModelConcurrencyOptions
{
    /// <summary>
    /// Gets or sets the maximum concurrent outgoing calls allowed per
    /// provider. <see langword="null"/> means unlimited.
    /// </summary>
    public int? MaxConcurrentCallsPerProvider { get; set; }
}
