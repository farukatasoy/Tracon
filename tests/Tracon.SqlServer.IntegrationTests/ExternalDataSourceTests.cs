using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// <c>UseSqlServer(o =&gt; o.DataSource = ...)</c>: a data source Tracon did
/// not build and must never dispose (Phase 110).
/// </summary>
/// <remarks>
/// <c>Microsoft.Data.SqlClient</c> does not offer a <see cref="DbDataSource"/>
/// of its own (measured against 7.0.2); these tests use Tracon's own
/// <see cref="SqlServerDataSource"/> adapter as the "consumer-supplied"
/// instance, the same adapter shape a consumer would have to write.
/// </remarks>
public sealed class ExternalDataSourceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task DataSource_and_ConnectionString_together_is_rejected()
    {
        await using var external = new SqlServerDataSource(fixture.ConnectionString);

        var services = new ServiceCollection();
        services.AddTracon().UseSqlServer(options =>
        {
            options.DataSource = external;
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = SqlServerTestContext.NewSchemaName();
        });

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TraconSqlServerOptions>>().Value);

        exception.Message.ShouldContain(nameof(TraconSqlServerOptions.DataSource));
        exception.Message.ShouldContain(nameof(TraconSqlServerOptions.ConnectionString));
    }

    [Fact]
    public async Task DataSource_alone_does_not_require_a_connection_string()
    {
        await using var external = new SqlServerDataSource(fixture.ConnectionString);

        var services = new ServiceCollection();
        services.AddTracon().UseSqlServer(options =>
        {
            options.DataSource = external;
            options.SchemaName = SqlServerTestContext.NewSchemaName();
            options.AutoApplyMigrations = false;
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<TraconSqlServerOptions>>().Value.ShouldNotBeNull();

        var storeContext = provider.GetRequiredService<SqlStoreContext>();

        storeContext.DataSource.ShouldBeSameAs(external);
        storeContext.OwnsDataSource.ShouldBeFalse();
    }

    /// <summary>Manual case 4: the host must not kill the consumer's own pool at shutdown.</summary>
    [Fact]
    public async Task External_data_source_is_not_disposed_when_the_host_stops()
    {
        await using var external = new SqlServerDataSource(fixture.ConnectionString);
        var schemaName = SqlServerTestContext.NewSchemaName();

        var services = new ServiceCollection();
        services.AddTracon().UseSqlServer(options =>
        {
            options.DataSource = external;
            options.SchemaName = schemaName;
            options.AutoApplyMigrations = true;
        });

        await using (var provider = services.BuildServiceProvider())
        {
            foreach (var hosted in provider.GetServices<IHostedService>())
            {
                await hosted.StartAsync(CancellationToken.None);
            }

            // Migrations really ran against the external data source: proof the
            // wiring is real, not just that validation passed.
            await using var checkCommand = external.CreateCommand(
                $"SELECT count(*) FROM {schemaName}.__migrations;");
            var migrationCount = await checkCommand.ExecuteScalarAsync();
            Convert.ToInt64(migrationCount, CultureInfo.InvariantCulture).ShouldBeGreaterThan(0);
        }
        // The ServiceProvider (and every singleton it created, including
        // SqlStoreContext) is now disposed.

        // If Tracon had disposed `external`, this would throw
        // ObjectDisposedException instead of running.
        await using var command = external.CreateCommand("SELECT 1;");
        var result = await command.ExecuteScalarAsync();
        Convert.ToInt32(result, CultureInfo.InvariantCulture).ShouldBe(1);
    }

    /// <summary>
    /// Mirror of the above: Tracon's OWN data source is marked as OWNED, so
    /// <c>SqlStoreContext.Dispose()</c>/<c>DisposeAsync()</c> disposes it at
    /// shutdown (proven directly, with a spy data source, by
    /// <c>SqlStoreContextDisposalTests</c> in the PostgreSQL project — that
    /// logic is shared source, K-176). This test stops short of asserting
    /// that a disposed <see cref="SqlServerDataSource"/> actually rejects a
    /// later command: unlike <c>NpgsqlDataSource</c>, it owns no unmanaged
    /// pool of its own to release (<c>Microsoft.Data.SqlClient</c>'s pool is
    /// keyed by connection string, not by this thin adapter), so its
    /// <see cref="DbDataSource.Dispose()"/> is a no-op and a post-dispose
    /// command legitimately keeps working.
    /// </summary>
    /// <summary>
    /// Phase 110, K-625: the data source Tracon builds is no longer a
    /// public DI service — see the matching test and remarks in
    /// <c>Tracon.PostgreSql.IntegrationTests.ServiceRegistrationTests</c>.
    /// </summary>
    [Fact]
    public void The_data_source_is_not_registered_as_a_public_DI_service()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseSqlServer(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = SqlServerTestContext.NewSchemaName();
            options.AutoApplyMigrations = false;
        });

        using var provider = services.BuildServiceProvider();

        provider.GetService<SqlServerDataSource>().ShouldBeNull();
    }

    [Fact]
    public void Own_data_source_is_marked_as_owned()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseSqlServer(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = SqlServerTestContext.NewSchemaName();
            options.AutoApplyMigrations = false;
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SqlStoreContext>().OwnsDataSource.ShouldBeTrue();
    }
}
