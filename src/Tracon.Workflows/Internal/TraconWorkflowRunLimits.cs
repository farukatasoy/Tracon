using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Hands <see cref="TraconWorkflowOptions.RunTimeout"/>, <see cref="TraconWorkflowOptions.MaxRunTimeout"/> and
/// <see cref="TraconWorkflowOptions.MaxSuperSteps"/> to the HTTP layer, which cannot see
/// <see cref="TraconWorkflowOptions"/>.
/// </summary>
/// <remarks>
/// Read through <see cref="IOptionsMonitor{TOptions}"/> on every access. <c>UseWorkflows</c> binds the options through
/// the framework's configuration binder, which registers a change token source, so a configuration reload reaches the
/// check of a save at once. The runner reads <see cref="IOptions{TOptions}"/> and keeps the value it built at start, so
/// after a reload the save checks the new limits while the runner applies the old ones until the host restarts.
/// </remarks>
internal sealed class TraconWorkflowRunLimits(IOptionsMonitor<TraconWorkflowOptions> options) : IWorkflowRunLimits
{
    /// <inheritdoc />
    public TimeSpan RunTimeout => options.CurrentValue.RunTimeout;

    /// <inheritdoc />
    public TimeSpan? MaxRunTimeout => options.CurrentValue.MaxRunTimeout;

    /// <inheritdoc />
    public int MaxSuperSteps => options.CurrentValue.MaxSuperSteps;
}
