using System.Data.Common;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// <see cref="SqlStoreContext"/>'s ownership-conditional dispose (Phase 110):
/// Tracon must dispose a data source it built itself, and must never
/// dispose one the consumer supplied. Isolated from a real Npgsql connection
/// on purpose, with a spy data source, so this is a direct proof of
/// <see cref="SqlStoreContext"/>'s own logic rather than of Npgsql's
/// disposal behavior (that end-to-end path is covered separately by
/// <see cref="ExternalDataSourceTests"/>). No Docker dependency.
/// </summary>
public sealed class SqlStoreContextDisposalTests
{
    [Fact]
    public void Dispose_disposes_the_data_source_when_it_owns_it()
    {
        var dataSource = new SpyDataSource();
        var context = Build(dataSource, ownsDataSource: true);

        context.Dispose();

        dataSource.DisposeCalled.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_does_not_dispose_the_data_source_when_it_does_not_own_it()
    {
        var dataSource = new SpyDataSource();
        var context = Build(dataSource, ownsDataSource: false);

        context.Dispose();

        dataSource.DisposeCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task DisposeAsync_disposes_the_data_source_when_it_owns_it()
    {
        var dataSource = new SpyDataSource();
        var context = Build(dataSource, ownsDataSource: true);

        await context.DisposeAsync();

        dataSource.DisposeAsyncCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_does_not_dispose_the_data_source_when_it_does_not_own_it()
    {
        var dataSource = new SpyDataSource();
        var context = Build(dataSource, ownsDataSource: false);

        await context.DisposeAsync();

        dataSource.DisposeAsyncCalled.ShouldBeFalse();
    }

    [Fact]
    public void OwnsDataSource_defaults_to_true()
    {
        // Direct construction (test fixtures across the three provider
        // projects, e.g. PostgresTestContext) must keep today's behavior
        // without setting the field explicitly.
        new SqlStoreContext
        {
            DataSource = new SpyDataSource(),
            Dialect = new PostgresDialect("tracon"),
            CommandTimeoutSeconds = 30,
            ProviderName = "PostgreSQL",
        }.OwnsDataSource.ShouldBeTrue();
    }

    private static SqlStoreContext Build(DbDataSource dataSource, bool ownsDataSource) => new()
    {
        DataSource = dataSource,
        Dialect = new PostgresDialect("tracon"),
        CommandTimeoutSeconds = 30,
        ProviderName = "PostgreSQL",
        OwnsDataSource = ownsDataSource,
    };

    private sealed class SpyDataSource : DbDataSource
    {
        public bool DisposeCalled { get; private set; }

        public bool DisposeAsyncCalled { get; private set; }

        public override string ConnectionString => string.Empty;

        protected override DbConnection CreateDbConnection() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            DisposeCalled = true;
            base.Dispose(disposing);
        }

        protected override ValueTask DisposeAsyncCore()
        {
            DisposeAsyncCalled = true;
            return base.DisposeAsyncCore();
        }
    }
}
