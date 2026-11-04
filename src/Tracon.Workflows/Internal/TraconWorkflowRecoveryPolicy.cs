using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Hands <see cref="TraconWorkflowOptions.ResumeInterruptedRuns"/> and
/// <see cref="TraconWorkflowOptions.MaxResumeAttempts"/> to the job handlers and
/// the HTTP layer, which cannot see <see cref="TraconWorkflowOptions"/>.
/// </summary>
/// <remarks>
/// Read through <see cref="IOptionsMonitor{TOptions}"/> on every access. <c>UseWorkflows</c> binds the options through
/// the framework's configuration binder, which registers a change token source, so a configuration reload reaches the
/// next job these handlers start. The runner reads <see cref="IOptions{TOptions}"/> and keeps the value it built at
/// start, so after a reload the two disagree until the host restarts: restart to apply a change to all readers.
/// </remarks>
internal sealed class TraconWorkflowRecoveryPolicy(IOptionsMonitor<TraconWorkflowOptions> options) : IWorkflowRecoveryPolicy
{
    /// <inheritdoc />
    public bool ResumeInterruptedRuns => options.CurrentValue.ResumeInterruptedRuns;

    /// <inheritdoc />
    public int MaxResumeAttempts => options.CurrentValue.MaxResumeAttempts;
}
