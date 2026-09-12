namespace Tracon;

/// <summary>Online evaluation settings.</summary>
/// <remarks>
/// <para>
/// Read from the <c>Tracon:OnlineEvaluation</c> configuration section.
/// </para>
/// <para>
/// <strong>Two-gate default.</strong> <see cref="Enabled"/> defaults to
/// <see langword="false"/> (the no-surprises default) AND
/// <see cref="SampleRate"/> defaults to <c>0.0</c>. Even when <see cref="Enabled"/>
/// is turned on, no run is sampled, the judge model is never called, and not
/// a single cent is spent unless the rate is also given. The third defense is
/// <see cref="MaxScoresPerHour"/>: even if the sample rate is miscalculated,
/// there is still an upper bound.
/// </para>
/// </remarks>
public sealed class OnlineEvaluationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Tracon:OnlineEvaluation";

    /// <summary>The greatest supported per-judge timeout.</summary>
    internal static readonly TimeSpan MaxJudgeTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Whether online evaluation is enabled. Default <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Fraction of completed runs to sample, 0.0-1.0.
    /// </summary>
    /// <remarks>
    /// Default <c>0.0</c>: even when <see cref="Enabled"/> is turned on,
    /// nothing is scored unless the rate is also given. Sampling itself is
    /// <strong>deterministic</strong> - it is derived from the hash of the run
    /// id; the same run is never evaluated twice, and a retry does not roll a
    /// new die.
    /// </remarks>
    public double SampleRate { get; set; }

    /// <summary>
    /// Maximum number of runs sampled per hour per tenant.
    /// </summary>
    /// <remarks>
    /// The second line of defense for sampling: even if
    /// <see cref="SampleRate"/> is miscalculated or traffic spikes, the
    /// absolute cost is bounded by this cap.
    /// </remarks>
    public int MaxScoresPerHour { get; set; } = 100;

    /// <summary>Only these agents are scored. Empty means all.</summary>
    public IList<string> AgentNames { get; } = [];

    /// <summary>Low-score threshold, on a 0-100 scale.</summary>
    public int LowScoreThreshold { get; set; } = 60;

    /// <summary>
    /// Minimum sample size required for an alert. A single low score does not produce an alert.
    /// </summary>
    /// <remarks>
    /// The model is noisy; raising an alert from a single sample trains the
    /// on-call engineer to start ignoring notifications.
    /// </remarks>
    public int MinSampleSize { get; set; } = 20;

    /// <summary>Maximum time allocated to one judge call.</summary>
    /// <remarks>
    /// The timeout applies to each registered judge independently. With N judges,
    /// one online evaluation can take up to N times this value before retries.
    /// </remarks>
    public TimeSpan JudgeTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Window for the average calculation.</summary>
    public TimeSpan EvaluationWindow { get; set; } = TimeSpan.FromHours(1);
}
