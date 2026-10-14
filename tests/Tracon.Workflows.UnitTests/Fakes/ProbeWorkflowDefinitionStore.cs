namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// A definition store that counts what the engine reads and can be told to fail or to forget a
/// version - the three things a test of the run's definition read needs and the in-memory
/// store cannot be made to do.
/// </summary>
/// <remarks>
/// Writes go straight to the wrapped store, so a test saves through it exactly as it would
/// through the real one.
/// </remarks>
internal sealed class ProbeWorkflowDefinitionStore(IWorkflowDefinitionStore inner) : IWorkflowDefinitionStore
{
    private int _gets;
    private int _versionGets;

    /// <summary>Gets how many times the current definition was read.</summary>
    public int Gets => Volatile.Read(ref _gets);

    /// <summary>Gets how many times one stored version was read.</summary>
    public int VersionGets => Volatile.Read(ref _versionGets);

    /// <summary>Gets or sets the exception the current-definition read throws; <see langword="null"/> for none.</summary>
    public Exception? GetFailure { get; set; }

    /// <summary>
    /// Gets the versions <see cref="GetVersionAsync"/> answers <see langword="null"/> for, whatever
    /// the wrapped store holds: the history of a database that lost them.
    /// </summary>
    public HashSet<int> ForgottenVersions { get; } = [];

    /// <inheritdoc />
    public ValueTask<WorkflowDefinition?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _gets);

        return GetFailure is { } failure
            ? throw failure
            : inner.GetAsync(tenantId, name, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        => inner.ListAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<WorkflowDefinition> SaveAsync(string tenantId, WorkflowDefinition definition, CancellationToken cancellationToken = default)
        => inner.SaveAsync(tenantId, definition, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        => inner.DeleteAsync(tenantId, name, cancellationToken);

    /// <inheritdoc />
    public ValueTask<WorkflowDefinition?> GetVersionAsync(string tenantId, string name, int version, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _versionGets);

        return ForgottenVersions.Contains(version)
            ? new ValueTask<WorkflowDefinition?>((WorkflowDefinition?)null)
            : inner.GetVersionAsync(tenantId, name, version, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorkflowDefinition>> ListVersionsAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        => inner.ListVersionsAsync(tenantId, name, cancellationToken);

    /// <inheritdoc />
    public ValueTask<WorkflowDefinition> RollbackAsync(string tenantId, string name, int version, CancellationToken cancellationToken = default)
        => inner.RollbackAsync(tenantId, name, version, cancellationToken);
}
