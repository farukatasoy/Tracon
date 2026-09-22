namespace Tracon;

/// <summary>
/// Applies the pending migrations of the active SQL persistence provider.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>Use*()</c> extension (<c>UsePostgreSql</c>, <c>UseSqlServer</c>, and
/// <c>UseSqlite</c>) registers this interface with <c>Replace</c>, resolving to
/// the provider's migration runner - the same instance
/// <see cref="ISqlPersistenceDiagnostics"/> resolves to.
/// </para>
/// <para>
/// This is the public way to apply migrations from your own code, for example
/// from a deployment step when <c>AutoApplyMigrations</c> is off. The runner
/// itself is not public: each SQL provider package compiles its own copy, and
/// this interface, which lives in <c>Tracon.Abstractions</c>, is the one type
/// all three share. <c>IServiceProvider.GetRequiredService&lt;IMigrationApplier&gt;()</c>
/// therefore resolves without knowing which provider is active, even in an
/// application that references more than one of them.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>Replace</c> by whichever SQL provider is active.
/// </para>
/// </remarks>
public interface IMigrationApplier
{
    /// <summary>Applies every pending migration, in order.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of migrations applied.</returns>
    ValueTask<int> ApplyAsync(CancellationToken cancellationToken = default);
}
