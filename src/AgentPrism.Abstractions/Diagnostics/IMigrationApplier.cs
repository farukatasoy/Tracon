namespace AgentPrism;

/// <summary>
/// Applies the pending migrations of the active SQL persistence provider.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>Use*()</c> extension (<c>UsePostgreSql</c>, <c>UseSqlServer</c>, and
/// <c>UseSqlite</c>) registers this interface with <c>Replace</c>, resolving to
/// the same instance as <c>MigrationRunner</c>.
/// </para>
/// <para>
/// This exists as its own interface, separate from
/// <see cref="ISqlPersistenceDiagnostics"/>, because <c>MigrationRunner</c> is
/// compiled from source SHARED across the three SQL provider packages
/// (<c>AgentPrism.Sql.Shared</c>): a consumer that references more than one
/// provider - the <c>agentprism</c> global tool does, for its <c>migrate</c>
/// command - sees three DIFFERENT <c>MigrationRunner</c> types with the same
/// name, and an unqualified reference to it does not compile (<c>CS0433</c>).
/// This interface lives in <c>AgentPrism.Abstractions</c>, referenced
/// identically by all three, so <c>IServiceProvider.GetRequiredService&lt;IMigrationApplier&gt;()</c>
/// resolves without needing to know which provider is active.
/// </para>
/// </remarks>
public interface IMigrationApplier
{
    /// <summary>Applies every pending migration, in order.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of migrations applied.</returns>
    ValueTask<int> ApplyAsync(CancellationToken cancellationToken = default);
}
