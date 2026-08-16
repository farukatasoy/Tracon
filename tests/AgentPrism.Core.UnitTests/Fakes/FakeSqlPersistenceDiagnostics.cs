namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Fakes <see cref="ISqlPersistenceDiagnostics"/> in tests without opening a real database connection.</summary>
internal sealed class FakeSqlPersistenceDiagnostics(
    string providerName,
    bool canConnect,
    IReadOnlyList<string> pendingMigrations) : ISqlPersistenceDiagnostics
{
    public string ProviderName { get; } = providerName;

    public ValueTask<SqlPersistenceDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new SqlPersistenceDiagnosticsSnapshot
        {
            CanConnect = canConnect,
            PendingMigrations = canConnect ? pendingMigrations : [],
        });
}
