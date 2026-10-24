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
    /// The limit is <em>shared across all tenants</em>, and it is per process. A
    /// single workflow makes dozens of model calls; leaving it unbounded would
    /// let one user consume an entire provider quota. To keep one tenant from
    /// taking every slot, set <see cref="MaxConcurrentRunsPerTenant"/>.
    /// </para>
    /// <para>
    /// A run over the limit waits for a slot. Its record already exists while
    /// it waits and shows <c>Queued</c> (it shows <c>Running</c> once it has a
    /// slot); the wait counts toward <see cref="RunTimeout"/>, and a run whose
    /// deadline passes before it gets a slot closes like any other timed-out run.
    /// </para>
    /// </remarks>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>
    /// Gets or sets the most workflow runs one tenant may have going at the same time.
    /// <see langword="null"/> (the default) sets no per-tenant limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without it, one tenant that starts many runs holds every slot of
    /// <see cref="MaxConcurrentRuns"/> and the queue's lease order, and the other tenants
    /// wait. With it, the setting limits a tenant in two places:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <strong>In each process:</strong> a tenant's runs beyond the limit wait for a tenant
    /// slot before they take one of the shared slots, so they never hold a shared slot while
    /// they wait. The run shows <c>Queued</c> until it has both slots. The limit is exact
    /// within one process.
    /// </item>
    /// <item>
    /// <strong>In the job queue, across every process that shares the store:</strong> a
    /// worker does not lease a queued workflow job (<c>Prefer: respond-async</c>, a scheduled
    /// or resumed workflow) of a tenant that already has this many workflow jobs leased, and
    /// leases the oldest job of another tenant instead. This count is read, not locked, so
    /// workers that lease at the same moment can take a tenant a little past the limit (by
    /// up to the number of workers); the in-process limit above stays exact.
    /// </item>
    /// </list>
    /// <para>
    /// The wait for a tenant slot counts toward <see cref="RunTimeout"/>, like the wait for a
    /// shared slot.
    /// </para>
    /// <para>
    /// Must be from 1 to <see cref="MaxConcurrentRuns"/>; any other value fails at startup.
    /// </para>
    /// </remarks>
    public int? MaxConcurrentRunsPerTenant { get; set; }

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
    /// A run stopped this way ends <c>Canceled</c> and reports a
    /// <c>TimeoutException</c> error naming this setting. The deadline starts when
    /// the run starts, so the time a run waits for a <see cref="MaxConcurrentRuns"/>
    /// slot counts too.
    /// </para>
    /// <para>
    /// A workflow definition can set its own value
    /// (<see cref="WorkflowDefinition.RunTimeout"/>). It may only shorten this one
    /// unless <see cref="MaxRunTimeout"/> allows more. To bound one step instead,
    /// give the node a timeout (<see cref="WorkflowNodeReference.Timeout"/>, or
    /// <see cref="WorkflowFunctionOptions.Timeout"/> when the function is
    /// registered): a step that runs past it fails the run, and a function's token
    /// is cancelled.
    /// </para>
    /// <para>
    /// Must be from one millisecond to about 49.7 days while <see cref="Enabled"/>
    /// is on; any other value fails at startup.
    /// </para>
    /// </remarks>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the longest run timeout a workflow definition may set for itself
    /// (<see cref="WorkflowDefinition.RunTimeout"/>). <see langword="null"/> (the
    /// default) means a definition may only shorten <see cref="RunTimeout"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A running workflow holds one of the <see cref="MaxConcurrentRuns"/> slots that
    /// every tenant shares, so how long one run may hold it is the host's decision,
    /// not the decision of whoever edits a definition. Set this to let some workflows
    /// run longer than <see cref="RunTimeout"/> - a podcast chain whose writer step
    /// takes several minutes, say - while every other workflow keeps the shorter
    /// default.
    /// </para>
    /// <para>
    /// A definition whose value is above the ceiling is refused when it is saved
    /// (<c>400</c>). A stored definition whose value is above a ceiling that was
    /// lowered later still runs, cut to the ceiling, and a warning is logged.
    /// </para>
    /// <para>
    /// Must be from one millisecond to about 49.7 days, and not below
    /// <see cref="RunTimeout"/>; any other value fails at startup, whether or not
    /// <see cref="Enabled"/> is on, because the ceiling guards a definition save.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseWorkflows(options =>
    ///        {
    ///            options.RunTimeout = TimeSpan.FromMinutes(10);
    ///            options.MaxRunTimeout = TimeSpan.FromHours(1);
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public TimeSpan? MaxRunTimeout { get; set; }

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
    /// <para>
    /// A run that a stopping worker cancels while <see cref="ResumeInterruptedRuns"/>
    /// is on is not finished: its job continues it from the latest checkpoint. It
    /// keeps its checkpoints, and the continuation, which shares the session,
    /// removes them when it finishes.
    /// </para>
    /// </remarks>
    public bool KeepCheckpointsAfterCompletion { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a workflow run that a queued job was running when the
    /// process stopped is resumed from its latest checkpoint when the job is
    /// leased again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is <see langword="false"/>: an interrupted job item runs its
    /// workflow again from the beginning, as before this setting existed. Turned
    /// on, the job is the run's owner. It writes the run's id on the job item (for
    /// a batch, <c>tracon.workflow</c>) before the run starts; a queued single run
    /// (<c>Prefer: respond-async</c>, <c>tracon.workflow-run</c>) already has its
    /// id as the job's id. When the job is leased again after a crash, a deploy or
    /// a graceful shutdown, the handler finds that run and acts on its status:
    /// </para>
    /// <list type="bullet">
    /// <item><c>Completed</c> or <c>AwaitingInput</c>: the work is done; nothing runs again.</item>
    /// <item>
    /// Any other status: a run left <c>Running</c> is closed as <c>Failed</c>, and
    /// a NEW run continues it from the latest checkpoint in its chain. The new
    /// run's <c>ContinuedFromRunId</c> names the run it continues; it uses the
    /// same session, the parameter values the chain recorded, and the
    /// attribution of whoever queued the job. It is not asked for quota or
    /// authorization again: the job was, when it was queued.
    /// </item>
    /// <item>
    /// No checkpoint in the chain: the run starts again from the beginning, or
    /// the resume or the response is applied again.
    /// </item>
    /// </list>
    /// <para>
    /// <strong>The step that was running when the process stopped runs
    /// again.</strong> A checkpoint is written as each super-step ends, so the
    /// steps finished before it are not repeated and the step in progress is. A
    /// function step with a side effect must be idempotent, and an agent step
    /// repeats its model calls and tool calls. A <c>Loop</c> agent step repeats
    /// every turn it had finished: the loop's turns are not checkpointed.
    /// </para>
    /// <para>
    /// A run that is bound to an HTTP connection (a streamed run) is never
    /// resumed automatically: the caller who was reading it is gone, and the run
    /// closes as <c>Canceled</c>. Resume it with the resume endpoint, or queue the
    /// run with <c>Prefer: respond-async</c> in the first place.
    /// </para>
    /// <para>
    /// Requires <see cref="EnableCheckpointing"/>: turning this on while
    /// checkpointing is off fails at startup. A graceful shutdown closes the run
    /// it stops as <c>Canceled</c> and keeps its checkpoints, also with
    /// <see cref="KeepCheckpointsAfterCompletion"/> off; the continuation removes
    /// them when it finishes.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UsePostgreSql(connectionString)
    ///        .UseWorkflows(options =>
    ///        {
    ///            options.ResumeInterruptedRuns = true;
    ///            options.MaxResumeAttempts = 2;
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public bool ResumeInterruptedRuns { get; set; }

    /// <summary>
    /// Gets or sets how many continuation runs one interrupted run may get while
    /// <see cref="ResumeInterruptedRuns"/> is on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is one. The count is per chain: a continuation that is itself
    /// interrupted counts against the same limit. Past it, the job item (or the
    /// queued run's job) is failed instead of continued, and the last run of the
    /// chain is left <c>Failed</c>. Zero turns continuation off but keeps the
    /// detection: an interrupted run is closed as <c>Failed</c> and its item fails,
    /// instead of running the workflow again from the beginning.
    /// </para>
    /// <para>
    /// The worker fails a job's lease past its attempt limit before the job's
    /// handler runs, so the job's last attempt never starts a continuation: a
    /// continuation it started would have nobody left to close it if it were
    /// interrupted too. The last attempt closes the chain instead. A queued single
    /// run's job is therefore given <c>2 + MaxResumeAttempts</c> attempts while
    /// recovery is on: one for the run, one per continuation, and the last one
    /// that closes the chain. A batch job keeps its own attempt limit
    /// (<c>Tracon:Scheduling:MaxAttempts</c>, three by default, or the job's
    /// <c>maxAttempts</c>) and gets at most two fewer continuations per item than
    /// that limit: give it <c>2 + MaxResumeAttempts</c> attempts to get them all.
    /// Must be from 0 to 100.
    /// </para>
    /// </remarks>
    public int MaxResumeAttempts { get; set; } = 1;

    /// <summary>
    /// Gets or sets how often the background scan looks for human requests whose time has run
    /// out (<see cref="WorkflowDefinition.RequestTimeout"/>) and closes their runs.
    /// </summary>
    /// <remarks>
    /// The default is one minute. A request is closed at most one interval after its time, so
    /// a finer value only matters for a very short <c>RequestTimeout</c>. Only one instance of
    /// a cluster scans at a time. The scan reads a partial index of the requests that have a
    /// limit, so it costs nothing while no workflow sets one. Must be from one millisecond to
    /// about 49.7 days.
    /// </remarks>
    public TimeSpan RequestExpirationScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the most requests one scan closes (<see cref="RequestExpirationScanInterval"/>).
    /// </summary>
    /// <remarks>The default is 100. A backlog is closed over several scans. Must be at least 1.</remarks>
    public int MaxRequestExpirationsPerScan { get; set; } = 100;
}
