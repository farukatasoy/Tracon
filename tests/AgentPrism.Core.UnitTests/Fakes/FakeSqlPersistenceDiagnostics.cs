namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Testlerde gercek bir veritabani baglantisi kurmadan <see cref="ISqlPersistenceDiagnostics"/> taklit eder.</summary>
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
