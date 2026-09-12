namespace Tracon;

/// <summary>The settings of single-executor election.</summary>
/// <remarks>
/// They are read from the <c>Tracon:SingletonExecution</c> configuration section. See
/// <c>TraconServiceCollectionExtensions.AddTracon</c>.
/// </remarks>
public sealed class SingletonExecutionOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string SectionName = "Tracon:SingletonExecution";

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
    /// <remarks>
    /// While <see cref="Enabled"/> is <see langword="true"/> this must be at least
    /// THREE SECONDS, and startup fails otherwise. Renewal never runs faster than once
    /// a second, so below that the renewal would land on or after the expiry: the lease
    /// would look expired while its owner is alive, and a second instance would take
    /// over work that has to run on one.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the id of this instance. When it is <see langword="null"/> or empty it
    /// is generated automatically (machine name plus process id plus a unique suffix).
    /// </summary>
    public string? OwnerId { get; set; }
}
