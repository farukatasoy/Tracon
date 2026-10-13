namespace Tracon.Core.UnitTests.Fakes;

/// <summary>Builds the <see cref="WorkflowRecovery"/> a workflow job handler needs in a unit test.</summary>
internal static class TestWorkflowRecovery
{
    /// <summary>Recovery as a host without <c>UseWorkflows()</c> has it: off, over empty stores.</summary>
    /// <returns>A recovery that is off.</returns>
    public static WorkflowRecovery Off() => new(new InMemoryRunStore(), new InMemoryWorkflowCheckpointStore(), policy: null);
}
