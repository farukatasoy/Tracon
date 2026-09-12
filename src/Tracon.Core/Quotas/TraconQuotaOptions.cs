namespace Tracon;

/// <summary>Defines options for the quota subsystem.</summary>
/// <remarks>
/// Read from the <c>Tracon:Quotas</c> configuration section.
/// </remarks>
public sealed class TraconQuotaOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Tracon:Quotas";

    /// <summary>
    /// Gets or sets whether quota enforcement is enabled. When disabled, rules
    /// can be stored but no run is rejected and no counter increments.
    /// </summary>
    /// <remarks>
    /// Defaulting to <see langword="true"/> is safe: nothing is rejected while
    /// no rule exists. The "no default quota" rule comes from an <em>empty rule
    /// set</em>, not from disabling the subsystem.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the time zone used to calculate period boundaries. Windows
    /// and IANA names are accepted.
    /// </summary>
    /// <remarks>
    /// An administrator who says "daily quota" means their business day. The
    /// consumer is therefore expected to change the default UTC time zone.
    /// </remarks>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// Gets percentages that publish a <c>quota.threshold</c> event when the
    /// quota counter reaches them.
    /// </summary>
    /// <remarks>
    /// Each threshold is published <strong>once</strong> per period. Otherwise,
    /// every run above the threshold would produce a new event as the counter increments.
    /// </remarks>
    public IList<int> ThresholdPercents { get; } = [80, 100];

    /// <summary>
    /// Gets or sets whether to allow a run when quota enforcement fails.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/> so a temporary database outage does
    /// not stop the service. A strict deployment can set this to
    /// <see langword="false"/> and choose "do not run when quota cannot be verified".
    /// </remarks>
    public bool AllowOnStoreFailure { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a crossed quota threshold is also written as a
    /// <see cref="RunEventType.Custom"/> notice (<see
    /// cref="RunEventCustomTypes.QuotaThreshold"/>) into the triggering root
    /// run's own event stream, before its terminal event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see langword="false"/>: with it off, behavior is
    /// byte-for-byte identical to before this option existed — the same
    /// <c>quota.threshold</c> webhook, no new frame in the run's event
    /// stream. The webhook itself is unaffected by this flag; it always
    /// fires when a threshold is crossed, exactly as before.
    /// </para>
    /// <para>
    /// A consumer that wants a threshold notice correlated to the exact run
    /// and delivered on the same connection the run streamed from turns this
    /// on. See <see cref="RunEventCustomTypes.QuotaThreshold"/> for the
    /// event's shape and the guarantee that only Tracon can write it.
    /// </para>
    /// </remarks>
    public bool PublishThresholdToRunStream { get; set; }

    /// <summary>Returns the resolved time zone, falling back to UTC when its name is unknown.</summary>
    /// <returns>The time zone.</returns>
    /// <remarks>
    /// An unrecognized name does not throw because quota enforcement must not
    /// stop all traffic for a bad name. <c>TraconQuotaOptionsValidator</c>
    /// validates it during startup and reports the error there.
    /// </remarks>
    public TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
