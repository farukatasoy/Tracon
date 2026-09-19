namespace Tracon;

/// <summary>Settings that control workflow execution.</summary>
public sealed class TraconWorkflowOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string SectionName = "Tracon:Workflows";

    /// <summary>Gets or sets whether workflow execution is enabled.</summary>
    /// <remarks>
    /// When disabled, the catalog is still listed; only execution is refused.
    /// This lets execution be stopped during an incident without deleting the
    /// definitions.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether checkpoint writing is enabled.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="true"/>: resuming works out of the box.
    /// The cost is one write per super-step; <see cref="MaxSuperSteps"/> and
    /// the retention limit of the in-memory store keep this in check.
    /// </remarks>
    public bool EnableCheckpointing { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of workflows that may run at the same time.
    /// </summary>
    /// <remarks>
    /// The limit is <em>shared across all tenants</em>. A single workflow makes
    /// dozens of model calls; leaving it unbounded would let one user consume
    /// an entire provider quota.
    /// </remarks>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>Gets or sets the maximum duration of a single workflow run.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Cooperative, and observed between super-steps.</strong> The
    /// deadline cancels the token the run is given, and the runner checks it
    /// as each super-step ends; it does not abandon work already in flight.
    /// A node that ignores its cancellation token therefore runs to
    /// completion, and the run stops at the first boundary after it. Treat
    /// this as a bound on how long a run keeps taking NEW steps, not as a
    /// hard wall-clock limit.
    /// </para>
    /// <para>
    /// A run stopped this way reports a <c>TimeoutException</c> error naming
    /// this setting. To bound an individual step instead, give the node its
    /// own timeout inside the handler.
    /// </para>
    /// </remarks>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the maximum number of super-steps a single run may process.
    /// </summary>
    /// <remarks>
    /// This is the only structural guard against an infinite loop. In the
    /// Handoff and GroupChat patterns the model decides when to hand off; a
    /// poorly written instruction can make two agents hand off to each other
    /// forever.
    /// </remarks>
    public int MaxSuperSteps { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether the checkpoints of a completed run are kept.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="true"/>: being resumable even after a
    /// workflow has finished is the whole point of this feature. Setups that
    /// want to avoid the storage cost can disable it; checkpoints then live
    /// only while the run is in progress.
    /// </remarks>
    public bool KeepCheckpointsAfterCompletion { get; set; } = true;
}
