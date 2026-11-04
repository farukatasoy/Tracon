using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Hands <see cref="TraconWorkflowOptions.ResumeInterruptedRuns"/> and
/// <see cref="TraconWorkflowOptions.MaxResumeAttempts"/> to the job handlers and
/// the HTTP layer, which cannot see <see cref="TraconWorkflowOptions"/>.
/// </summary>
/// <remarks>
/// Read through <see cref="IOptionsMonitor{TOptions}"/> on every access, the same way the runner reads its other
/// settings. Tracon follows no configuration reload (the options are the ones the host started with), so the read is
/// a lookup and not a promise that a changed file reaches the next job: restart to apply a change.
/// </remarks>
internal sealed class TraconWorkflowRecoveryPolicy(IOptionsMonitor<TraconWorkflowOptions> options) : IWorkflowRecoveryPolicy
{
    /// <inheritdoc />
    public bool ResumeInterruptedRuns => options.CurrentValue.ResumeInterruptedRuns;

    /// <inheritdoc />
    public int MaxResumeAttempts => options.CurrentValue.MaxResumeAttempts;
}
