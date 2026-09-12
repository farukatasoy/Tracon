namespace Tracon;

/// <summary>Graceful-shutdown draining settings.</summary>
/// <remarks>
/// Read from the <c>Tracon:Drain</c> configuration section. See
/// <c>TraconServiceCollectionExtensions.AddTracon</c>.
/// </remarks>
public sealed class TraconDrainOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:Drain";

    /// <summary>
    /// Whether draining is on. Defaults to <see langword="false"/>: today's
    /// behavior (the process stops immediately, in-flight runs are cut off)
    /// does not change unless a setup opts in.
    /// </summary>
    /// <remarks>
    /// Turning this on does not, by itself, close the register/accept race
    /// window a new run can land in right as shutdown begins (BL-026) — see
    /// <see cref="ITraconDrainState"/>'s remarks for the two backup
    /// mechanisms (Kestrel request draining, the job worker's own
    /// <c>WaitForRunningJobsAsync</c>) that actually carry that guarantee.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// The longest time to wait for in-flight runs to finish before the
    /// process stops anyway.
    /// </summary>
    /// <remarks>
    /// The host's own <c>HostOptions.ShutdownTimeout</c> (default 30 s)
    /// still applies on top of this value and can cut the wait short; a
    /// setup that wants the full wait to take effect raises both together.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
