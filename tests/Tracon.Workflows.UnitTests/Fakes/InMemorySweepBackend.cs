using Tracon.Tests.Workflows;

namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// The in-memory stores a cancellation sweep runs against: new ones for every run, so no run sees another's rows.
/// </summary>
/// <remarks>
/// The harness has no process, connection or file to leak, and the sweep does not claim to look for any.
/// </remarks>
internal sealed class InMemorySweepBackend : IWorkflowSweepBackend
{
    /// <inheritdoc />
    public string Name => "in-memory";

    /// <inheritdoc />
    public SweepTenantContext Tenant { get; } = new();

    /// <inheritdoc />
    public SweepStores OpenStores()
        => new(
            new InMemoryRunStore(tenantContext: Tenant),
            new InMemoryWorkflowCheckpointStore(),
            new InMemoryWorkflowRequestStore(Tenant),
            new InMemoryWorkflowResponseStore(Tenant));

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> FindLeaksAsync() => new([]);
}
