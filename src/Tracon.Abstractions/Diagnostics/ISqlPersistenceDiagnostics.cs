namespace Tracon;

/// <summary>
/// Defines optional diagnostics for the active SQL persistence provider.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>Use*()</c> extension (<c>UsePostgreSql</c>, <c>UseSqlServer</c>, and
/// <c>UseSqlite</c>) registers this interface with <c>Replace</c>. Diagnostics
/// reflect the winner. Persistence is in memory when no provider registers it.
/// </para>
/// <para>
/// The check is a <strong>lightweight connection probe</strong>, similar to
/// <c>SELECT 1</c>. It applies no migration and changes no data.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>Replace</c> by whichever SQL provider is active.
/// </para>
/// </remarks>
public interface ISqlPersistenceDiagnostics
{
    /// <summary>Gets the provider name, for example <c>PostgreSQL</c>.</summary>
    string ProviderName { get; }

    /// <summary>Reads the connection and migration status.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current status.</returns>
    ValueTask<SqlPersistenceDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>Represents the current connection and migration status of an SQL persistence provider.</summary>
public sealed record SqlPersistenceDiagnosticsSnapshot
{
    /// <summary>Gets whether the database can be reached.</summary>
    public required bool CanConnect { get; init; }

    /// <summary>Gets the pending migration names. The list is unknown and empty if the database cannot be reached.</summary>
    public required IReadOnlyList<string> PendingMigrations { get; init; }
}

/// <summary>
/// Marks a registered SQL persistence provider.
/// </summary>
/// <param name="ProviderName">The provider name, for example <c>PostgreSQL</c>.</param>
/// <remarks>
/// Each <c>Use*</c> extension adds a marker. Markers <em>accumulate</em> through
/// <c>AddSingleton</c>, not <c>TryAdd</c>. If more than one exists, startup logs
/// a warning and <see cref="TraconDiagnosticsReport.RegisteredPersistenceProviders"/>
/// returns more than one.
/// </remarks>
internal sealed record SqlPersistenceRegistrationMarker(string ProviderName);
