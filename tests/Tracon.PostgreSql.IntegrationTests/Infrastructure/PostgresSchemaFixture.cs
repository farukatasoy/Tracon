using Tracon.Testing.Contracts.Storage;

namespace Tracon.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// The single schema shared by one contract test class. The schema is created
/// once per class; each test resets its own data with
/// <see cref="PostgresTestContext.ResetDataAsync"/>.
/// </summary>
/// <param name="container">The running PostgreSQL container.</param>
public sealed class PostgresSchemaFixture(PostgresFixture container) : IAsyncLifetime
{
    /// <summary>Gets the mutable tenant context shared by all tests in the class.</summary>
    public MutableTenantContext Tenant { get; } = new("tenant-a");

    /// <summary>Gets the class's schema context.</summary>
    internal PostgresTestContext Context { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
        => Context = await PostgresTestContext.CreateAsync(container, Tenant);

    /// <summary>Resets all data in the schema; the schema and the migration ledger remain.</summary>
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
