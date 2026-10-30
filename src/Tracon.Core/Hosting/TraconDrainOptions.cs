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
    /// window a new run can land in right as shutdown begins. Two
    /// backup mechanisms actually carry that guarantee: the HTTP host's own
    /// Kestrel request draining, and the job worker's own
    /// <c>WaitForRunningJobsAsync</c> drain loop. A setup that relies on this
    /// flag alone, without one of those two layers in its own request path,
    /// has no closed guarantee against a run starting in that narrow window.
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
    /// <para>Must be from one millisecond to about 49.7 days; any other value fails at startup.</para>
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
