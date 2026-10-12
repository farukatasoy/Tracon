namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>A run input store whose write fails, to prove the run survives it.</summary>
internal sealed class ThrowingRunInputStore : IRunInputStore
{
    public ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("The run input store is down.");

    public ValueTask<RunInputRecord?> GetAsync(string tenantId, Guid runId, CancellationToken cancellationToken = default)
        => new((RunInputRecord?)null);
}
