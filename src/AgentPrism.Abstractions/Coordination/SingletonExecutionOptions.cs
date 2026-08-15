namespace AgentPrism;

/// <summary>The settings of single-executor election (phase 42).</summary>
/// <remarks>
/// They are read from the <c>AgentPrism:SingletonExecution</c> configuration section. See
/// <c>AgentPrismServiceCollectionExtensions.AddAgentPrism</c>.
/// </remarks>
public sealed class SingletonExecutionOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string SectionName = "AgentPrism:SingletonExecution";

    /// <summary>
    /// Gets or sets a value that turns on single-executor election. The default is
    /// <see langword="false"/>: behaviour does not change in a single-instance setup, and
    /// no query reaches the lease table.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the lease duration. Renewal happens at ONE THIRD of this duration; at
    /// half of it, a single missed renewal would drop the lease.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the id of this instance. When it is <see langword="null"/> or empty it
    /// is generated automatically (machine name plus process id plus a unique suffix).
    /// </summary>
    public string? OwnerId { get; set; }
}
