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
    /// <para>
    /// The limit is <em>shared across all tenants</em>. A single workflow makes
    /// dozens of model calls; leaving it unbounded would let one user consume
    /// an entire provider quota.
    /// </para>
    /// <para>
    /// A run over the limit waits for a slot. Its record already exists while
    /// it waits and shows <c>Running</c>; the wait counts toward
    /// <see cref="RunTimeout"/>, and a run whose deadline passes before it gets
    /// a slot closes like any other timed-out run.
    /// </para>
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
    /// this setting. The deadline starts when the run starts, so the time a
    /// run waits for a <see cref="MaxConcurrentRuns"/> slot counts too. To
    /// bound an individual step instead, give the node its own timeout inside
    /// the handler.
    /// </para>
    /// <para>
    /// Must be from one millisecond to about 49.7 days while <see cref="Enabled"/>
    /// is on; any other value fails at startup.
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
    /// Gets or sets the largest settings a single function node may carry, as bytes
    /// of the settings' JSON text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A workflow definition is written whole on every save and read on every run,
    /// and the workflow list returns every node. A node's settings are a handful of
    /// values for the function's settings type; the cap keeps the definition from
    /// becoming a data store. The default of 16 KiB is far above any settings object
    /// a function declares by hand and far below a body worth keeping in a
    /// definition.
    /// </para>
    /// <para>
    /// The cap is checked when a definition is saved. A definition that was saved
    /// before the cap was lowered still runs. Must be at least 1.
    /// </para>
    /// </remarks>
    public int MaxNodeSettingsLength { get; set; } = 16 * 1024;

    /// <summary>
    /// Gets or sets whether the checkpoints of a completed run are kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is <see langword="true"/>: being resumable even after a
    /// workflow has finished is the whole point of this feature. Setups that
    /// want to avoid the storage cost can disable it; checkpoints then live
    /// only while the run is in progress.
    /// </para>
    /// <para>
    /// Checkpoints are kept per session. While another run of the same
    /// session still needs them — one waiting for a human answer, or one still
    /// running — a finished run leaves them in place, so a pending request
    /// stays answerable after an earlier answer, even a rejected one. A run that
    /// asked a human stays waiting even after an answer (a second answer opens a
    /// new branch), so once a session has paused for input, this setting no
    /// longer removes that session's checkpoints; retention does, for the runs
    /// that are not waiting.
    /// </para>
    /// </remarks>
    public bool KeepCheckpointsAfterCompletion { get; set; } = true;
}
