using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Hands <see cref="TraconWorkflowOptions.ResumeInterruptedRuns"/> and
/// <see cref="TraconWorkflowOptions.MaxResumeAttempts"/> to the job handlers and
/// the HTTP layer, which cannot see <see cref="TraconWorkflowOptions"/>.
/// </summary>
/// <remarks>
/// Read through <see cref="IOptionsMonitor{TOptions}"/> on every access. <c>UseWorkflows</c> registers no change token
/// source, so the monitor holds the value it built at start, the same value the runner applies, and a change to the
/// configuration needs a restart.
/// </remarks>
internal sealed class TraconWorkflowRecoveryPolicy(IOptionsMonitor<TraconWorkflowOptions> options) : IWorkflowRecoveryPolicy
{
    /// <inheritdoc />
    public bool ResumeInterruptedRuns => options.CurrentValue.ResumeInterruptedRuns;

    /// <inheritdoc />
    public int MaxResumeAttempts => options.CurrentValue.MaxResumeAttempts;
}
