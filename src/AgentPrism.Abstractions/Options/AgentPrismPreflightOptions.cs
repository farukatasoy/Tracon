namespace AgentPrism;

/// <summary>
/// Defines options for the pre-flight context-window check.
/// </summary>
/// <remarks>
/// <para>
/// The check estimates prompt token count before a run starts and rejects the
/// call, with no model provider ever contacted, once the estimate exceeds the
/// model's context window. The estimate is approximate: counting a
/// prompt's tokens without the provider's own tokenizer can only approximate
/// the real count, and a wrong count either rejects a call that would have
/// succeeded or lets through one that would have failed.
/// </para>
/// <para>
/// <strong>Disabled by default</strong>: a false rejection stops a
/// working agent, and that risk must be opted into, not discovered in production.
/// </para>
/// </remarks>
public sealed class AgentPrismPreflightOptions
{
    /// <summary>
    /// Gets or sets whether the pre-flight context-window check runs.
    /// Disabled by default.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the share of the context window kept free for the answer,
    /// from 0 to 1. Defaults to 0.2 (20%).
    /// </summary>
    /// <remarks>
    /// A fixed token count would need updating for every new model; a ratio
    /// scales with the model's own window automatically.
    /// </remarks>
    public double ReserveRatio { get; set; } = 0.2;
}
