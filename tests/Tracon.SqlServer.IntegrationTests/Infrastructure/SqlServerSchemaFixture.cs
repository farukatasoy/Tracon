using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests.Infrastructure;

/// <summary>
/// The single schema shared by one test class. The schema is created
/// once per class; each test resets its own data with
/// <see cref="SqlServerTestContext.ResetDataAsync"/>.
/// </summary>
/// <remarks>
/// A reset deletes rows; it does not undo a change to the shape of the schema,
/// the position of a sequence or an identity seed. A test that changes or
/// measures one of those builds a schema of its own.
/// </remarks>
public class SqlServerSchemaFixture : IAsyncLifetime
{
    private readonly SqlServerFixture _container;
    private readonly bool _enableReadViews;

    /// <summary>Sets up the fixture of a schema that has the core migration set only.</summary>
    /// <param name="container">The running SQL Server container.</param>
    public SqlServerSchemaFixture(SqlServerFixture container)
        : this(container, enableReadViews: false)
    {
    }

    /// <summary>Sets up the fixture of a schema, with or without the "views" optional migration set.</summary>
    /// <param name="container">The running SQL Server container.</param>
    /// <param name="enableReadViews">Whether the "views" migration set applies.</param>
    protected SqlServerSchemaFixture(SqlServerFixture container, bool enableReadViews)
    {
        _container = container;
        _enableReadViews = enableReadViews;
    }

    /// <summary>Gets the mutable tenant context shared by all tests in the class.</summary>
    public MutableTenantContext Tenant { get; } = new("tenant-a");

    /// <summary>Gets the class's schema context.</summary>
    /// <remarks>A test never disposes it: disposing a context drops its schema, and the next test of the class needs it.</remarks>
    internal SqlServerTestContext Context { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
       => Context = await SqlServerTestContext.CreateAsync(_container, Tenant, enableReadViews: _enableReadViews);

    /// <summary>Resets all data in the schema; the schema and the migration ledger remain.</summary>
    /// <returns>The completion task.</returns>
    public ValueTask ResetAsync() => Context.ResetDataAsync();

    /// <summary>
    /// Resets all data in the schema and points the shared tenant context at
    /// <paramref name="tenantId"/>, in the canonical form a fixed tenant context
    /// would hold.
    /// </summary>
    /// <param name="tenantId">The tenant the stores of the context read.</param>
    /// <returns>The class's schema context, empty.</returns>
    /// <remarks>
    /// The tenant context is shared by every test of the class, so a test that
    /// ran before may have moved it; a test that reads the tenant from the
    /// context sets it here, on its first line.
    /// </remarks>
    internal async ValueTask<SqlServerTestContext> ResetAsync(string tenantId)
    {
        Tenant.TenantId = AmbientTenantScope.Normalize(tenantId);
        await Context.ResetDataAsync();

        return Context;
    }

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

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// The schema shared by one test class that reads the published views: the
/// core migration set plus the "views" optional set, which production turns on
/// only on request.
/// </summary>
/// <param name="container">The running SQL Server container.</param>
public sealed class SqlServerReadViewSchemaFixture(SqlServerFixture container)
    : SqlServerSchemaFixture(container, enableReadViews: true);
