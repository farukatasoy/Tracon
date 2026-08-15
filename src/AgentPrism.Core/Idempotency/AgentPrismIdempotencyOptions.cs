namespace AgentPrism;

/// <summary>Options for <c>Idempotency-Key</c> support — Phase 43.</summary>
/// <remarks>
/// <para>
/// Reads values from the <c>AgentPrism:Idempotency</c> configuration section.
/// </para>
/// <para>
/// The <strong>default is enabled</strong>. This is a deliberate K1 interpretation.
/// Unlike rate limiting and quotas, this feature activates only when a client sends
/// the <c>Idempotency-Key</c> header. A request without the header has no extra cost
/// or behavior change. If disabled by default, a client that sends the header could
/// assume protection when it has none. That would be the silent surprise. Decision:
/// <c>docs/KARARLAR.md</c>; add the decision number at phase close.
/// </para>
/// </remarks>
public sealed class AgentPrismIdempotencyOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AgentPrism:Idempotency";

    /// <summary>
    /// Gets or sets a value that enables idempotency support. Even when enabled, a
    /// request without an <c>Idempotency-Key</c> header has no extra cost. Default: <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The maximum key length. The endpoint returns <c>400</c> when it is exceeded.</summary>
    public int MaxKeyLength { get; set; } = 255;
}
