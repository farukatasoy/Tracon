using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Hands <see cref="TraconWorkflowOptions.RunTimeout"/> and
/// <see cref="TraconWorkflowOptions.MaxRunTimeout"/> to the HTTP layer, which cannot see
/// <see cref="TraconWorkflowOptions"/>.
/// </summary>
/// <remarks>
/// Read through <see cref="IOptionsMonitor{TOptions}"/> on every access, so the save
/// checks the same values the runner applies.
/// </remarks>
internal sealed class TraconWorkflowRunTimeoutPolicy(IOptionsMonitor<TraconWorkflowOptions> options) : IWorkflowRunTimeoutPolicy
{
    /// <inheritdoc />
    public TimeSpan RunTimeout => options.CurrentValue.RunTimeout;

    /// <inheritdoc />
    public TimeSpan? MaxRunTimeout => options.CurrentValue.MaxRunTimeout;
}
