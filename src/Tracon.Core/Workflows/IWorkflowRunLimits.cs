namespace Tracon;

/// <summary>
/// The workflow engine's run limits, as the HTTP layer reads them when a definition is saved.
/// </summary>
/// <remarks>
/// <para>
/// The settings themselves are <c>TraconWorkflowOptions.RunTimeout</c>, <c>TraconWorkflowOptions.MaxRunTimeout</c>
/// and <c>TraconWorkflowOptions.MaxSuperSteps</c>. They live in <c>Tracon.Workflows</c>,
/// which this package does not reference, so <c>UseWorkflows()</c> registers the one
/// implementation of this seam - the same shape as <see cref="IWorkflowRecoveryPolicy"/>.
/// </para>
/// <para>
/// Without <c>UseWorkflows()</c> nothing is registered and a definition's run timeout
/// is checked for its range only, and its step count not at all: the engine that would apply them is not there, and it
/// reads the limits again when it runs.
/// </para>
/// </remarks>
internal interface IWorkflowRunLimits
{
    /// <summary>Gets the installation's run timeout.</summary>
    TimeSpan RunTimeout { get; }

    /// <summary>Gets the longest run timeout a definition may set; <see langword="null"/> when not set.</summary>
    TimeSpan? MaxRunTimeout { get; }

    /// <summary>Gets the most super-steps one run may process.</summary>
    int MaxSuperSteps { get; }
}
