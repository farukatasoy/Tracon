namespace Tracon;

/// <summary>
/// The workflow engine's run timeout settings, as the HTTP layer reads them when a
/// definition is saved.
/// </summary>
/// <remarks>
/// <para>
/// The settings themselves are <c>TraconWorkflowOptions.RunTimeout</c> and
/// <c>TraconWorkflowOptions.MaxRunTimeout</c>. They live in <c>Tracon.Workflows</c>,
/// which this package does not reference, so <c>UseWorkflows()</c> registers the one
/// implementation of this seam - the same shape as <see cref="IWorkflowRecoveryPolicy"/>.
/// </para>
/// <para>
/// Without <c>UseWorkflows()</c> nothing is registered and a definition's run timeout
/// is checked for its range only: the engine that would apply it is not there, and it
/// reads the ceiling again when it runs.
/// </para>
/// </remarks>
internal interface IWorkflowRunTimeoutPolicy
{
    /// <summary>Gets the installation's run timeout.</summary>
    TimeSpan RunTimeout { get; }

    /// <summary>Gets the longest run timeout a definition may set; <see langword="null"/> when not set.</summary>
    TimeSpan? MaxRunTimeout { get; }
}
