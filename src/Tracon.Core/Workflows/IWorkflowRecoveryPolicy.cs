namespace Tracon;

/// <summary>
/// The workflow engine's recovery settings, as the job handlers and the HTTP
/// layer read them.
/// </summary>
/// <remarks>
/// <para>
/// The settings themselves are <c>TraconWorkflowOptions.ResumeInterruptedRuns</c>
/// and <c>TraconWorkflowOptions.MaxResumeAttempts</c>. They live in
/// <c>Tracon.Workflows</c>, which this package does not reference, so
/// <c>UseWorkflows()</c> registers the one implementation of this seam. Without
/// <c>UseWorkflows()</c> nothing is registered and recovery is off: a host that
/// cannot run a workflow cannot resume one either.
/// </para>
/// <para>
/// A handler built by an explicit factory receives this through that
/// factory. A factory that does not pass it leaves recovery off with no error,
/// whatever the configuration says.
/// </para>
/// </remarks>
internal interface IWorkflowRecoveryPolicy
{
    /// <summary>Gets whether an interrupted queued workflow run is resumed by its job.</summary>
    bool ResumeInterruptedRuns { get; }

    /// <summary>Gets how many continuation runs one interrupted run may get.</summary>
    int MaxResumeAttempts { get; }
}

/// <summary>Reads <see cref="IWorkflowRecoveryPolicy"/> where it may be absent.</summary>
internal static class WorkflowRecoveryPolicy
{
    /// <summary>Gets whether recovery is on.</summary>
    /// <param name="policy">The registered policy; <see langword="null"/> when the engine is not registered.</param>
    /// <returns><see langword="true"/> when a policy is registered and turned on.</returns>
    public static bool IsOn(IWorkflowRecoveryPolicy? policy) => policy is { ResumeInterruptedRuns: true };

    /// <summary>
    /// Gets the attempt limit of a job that carries a single queued workflow run.
    /// </summary>
    /// <param name="policy">The registered policy.</param>
    /// <param name="asyncRunMaxAttempts">The queued-run attempt limit (<c>Tracon:AsyncRun:MaxAttempts</c>).</param>
    /// <returns>
    /// <c>2 + MaxResumeAttempts</c> while recovery is on: the first attempt, one per
    /// continuation, and a last one that finds the chain at its limit and closes it.
    /// While it is off, the same limit a queued agent run gets, so the flag is the
    /// only thing that changes the queue's behavior.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The worker fails a lease past the job's limit BEFORE any handler runs. With
    /// one attempt fewer, the lease that should find the chain at its limit never
    /// reaches the handler: the last continuation stays <see cref="RunStatus.Running"/>,
    /// and with no continuation allowed the interruption is never detected at all.
    /// </para>
    /// <para>
    /// The queued-run limit is not used while recovery is on: its default of one
    /// would fail the job on its second lease, before its handler could resume it.
    /// </para>
    /// </remarks>
    public static int JobAttempts(IWorkflowRecoveryPolicy? policy, int asyncRunMaxAttempts)
        => IsOn(policy) ? 2 + policy!.MaxResumeAttempts : asyncRunMaxAttempts;
}
