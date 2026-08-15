namespace AgentPrism;

/// <summary>Options for the canary evaluation background service — Phase 56.</summary>
/// <remarks>
/// Reads values from the <c>AgentPrism:Canary</c> configuration section.
/// </remarks>
public sealed class CanaryOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AgentPrism:Canary";

    /// <summary>
    /// Gets or sets a value that enables automatic rollback. The default is
    /// <see langword="false"/> (K1). No experiment stops itself or changes weight
    /// until this is enabled, even when a <see cref="CanaryPolicy"/> exists.
    /// </summary>
    public bool AutoRollbackEnabled { get; set; }

    /// <summary>The evaluation interval for running experiments that define a canary policy.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(5);
}
