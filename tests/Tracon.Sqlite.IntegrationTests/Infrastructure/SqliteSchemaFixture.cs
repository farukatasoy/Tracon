using Tracon.Testing.Contracts.Storage;

namespace Tracon.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// The single table prefix shared by one contract test class. The prefix is
/// created once per class; each test resets its own data with
/// <see cref="SqliteTestContext.ResetDataAsync"/>.
/// </summary>
/// <param name="database">The directory that holds the per-prefix SQLite files.</param>
/// <remarks>
/// The prefix is one per class and so is its database file
/// (<see cref="SqliteFixture.ConnectionStringFor"/>): classes that xunit runs
/// in parallel never wait for each other's writer lock.
/// </remarks>
public sealed class SqliteSchemaFixture(SqliteFixture database) : IAsyncLifetime
{
    /// <summary>Gets the mutable tenant context shared by all tests in the class.</summary>
    public MutableTenantContext Tenant { get; } = new("tenant-a");

    /// <summary>Gets the class's table prefix context.</summary>
    internal SqliteTestContext Context { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
        => Context = await SqliteTestContext.CreateAsync(database, Tenant);

    /// <summary>Resets all data in the prefix; the tables and the migration ledger remain.</summary>
    /// <returns>The completion task.</returns>
    public ValueTask ResetAsync() => Context.ResetDataAsync();

    /// <inheritdoc />
    /// <remarks>
    /// If <see cref="InitializeAsync"/> fails, <see cref="Context"/> is never
    /// assigned; disposal is skipped silently in that case, so the real error
    /// is not masked by a <see cref="NullReferenceException"/>.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Context is not null)
        {
            await Context.DisposeAsync();
        }
    }
}
