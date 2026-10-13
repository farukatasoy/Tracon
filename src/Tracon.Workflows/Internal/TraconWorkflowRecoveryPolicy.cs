using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Hands <see cref="TraconWorkflowOptions.ResumeInterruptedRuns"/> and
/// <see cref="TraconWorkflowOptions.MaxResumeAttempts"/> to the job handlers and
/// the HTTP layer, which cannot see <see cref="TraconWorkflowOptions"/>.
/// </summary>
/// <remarks>
/// Read through <see cref="IOptionsMonitor{TOptions}"/> on every access, so a
/// reloaded configuration reaches the next job without a restart, the same way
/// the runner reads its other settings.
/// </remarks>
internal sealed class TraconWorkflowRecoveryPolicy(IOptionsMonitor<TraconWorkflowOptions> options) : IWorkflowRecoveryPolicy
{
    /// <inheritdoc />
    public bool ResumeInterruptedRuns => options.CurrentValue.ResumeInterruptedRuns;

    /// <inheritdoc />
    public int MaxResumeAttempts => options.CurrentValue.MaxResumeAttempts;
}
