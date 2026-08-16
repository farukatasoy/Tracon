using AgentPrism.StoreContracts;

namespace AgentPrism.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// The single table prefix shared by one contract test class. The prefix is
/// created once per class; each test resets its own data with
/// <see cref="SqliteTestContext.ResetDataAsync"/>.
/// </summary>
/// <param name="database">The running SQLite database file.</param>
/// <remarks>
/// 🚨 Because the prefix is one per class, migrations write to SQLite's SINGLE
/// file in parallel; <see cref="SqliteDialect"/>'s prefix-scoped lock file
/// (K-389) serializes these concurrent migrations — no separate locking
/// mechanism is needed here.
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
