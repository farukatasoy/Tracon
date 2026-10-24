namespace Tracon;

/// <summary>
/// What a run that waited on a human request that expired is closed with.
/// </summary>
/// <remarks>
/// Kept in <c>Tracon.Core</c> because two projects need the one string: the expiry service, in
/// <c>Tracon.Workflows</c>, writes it, and <see cref="DefaultRunErrorClassifier"/> files it under
/// <see cref="RunErrorClass.Timeout"/> - a deadline, never a fault of the workflow.
/// </remarks>
internal static class WorkflowRequestExpiry
{
    /// <summary>The <see cref="RunError.Type"/> of a run closed because a request nobody answered expired.</summary>
    public const string ErrorType = TraconErrorTypes.WorkflowRequestExpired;
}
