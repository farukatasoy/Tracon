using System.Data.Common;
using System.Globalization;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing;
using Tracon.Testing.Contracts.Storage;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// <c>UsePostgreSql(o =&gt; o.DataSource = ...)</c>: a data source Tracon did
/// not build and must never dispose (Phase 110).
/// </summary>
/// <remarks>
/// This is the DI-level counterpart of <c>PostgresTestContext</c>, which has
/// always constructed <c>SqlStoreContext</c> directly with a hand-built data
/// source (never through <c>UsePostgreSql()</c>) — so the "stores work with
/// any externally-owned <c>DbDataSource</c>" claim was already exercised
/// structurally by every other test in this project. What these tests add is
/// the DI wiring itself: option validation, ownership tracking, and the
/// dispose-on-shutdown guarantee.
/// </remarks>
public sealed class ExternalDataSourceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task DataSource_and_ConnectionString_together_is_rejected()
    {
        await using var external = new NpgsqlDataSourceBuilder(fixture.ConnectionString).Build();

        var services = new ServiceCollection();
        services.AddTracon().UsePostgreSql(options =>
        {
            options.DataSource = external;
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = PostgresTestContext.NewSchemaName();
        });

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value);

        exception.Message.ShouldContain(nameof(TraconPostgreSqlOptions.DataSource));
        exception.Message.ShouldContain(nameof(TraconPostgreSqlOptions.ConnectionString));
    }

    [Fact]
    public async Task DataSource_alone_does_not_require_a_connection_string()
    {
        await using var external = new NpgsqlDataSourceBuilder(fixture.ConnectionString).Build();

        var services = new ServiceCollection();
        services.AddTracon().UsePostgreSql(options =>
        {
            options.DataSource = external;
            options.SchemaName = PostgresTestContext.NewSchemaName();
            options.AutoApplyMigrations = false;
        });

        using var provider = services.BuildServiceProvider();

        // Validation must succeed: resolving the options must NOT throw.
        provider.GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value.ShouldNotBeNull();

        var storeContext = provider.GetRequiredService<SqlStoreContext>();

        storeContext.DataSource.ShouldBeSameAs(external);
        storeContext.OwnsDataSource.ShouldBeFalse();
    }

    /// <summary>Manual case 4: the host must not kill the consumer's own pool at shutdown.</summary>
    [Fact]
    public async Task External_data_source_is_not_disposed_when_the_host_stops()
    {
        await using var external = new NpgsqlDataSourceBuilder(fixture.ConnectionString).Build();
        var schemaName = PostgresTestContext.NewSchemaName();

        var services = new ServiceCollection();
        services.AddTracon().UsePostgreSql(options =>
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

    /// <summary>Mirror of the above: Tracon's OWN data source IS disposed at shutdown.</summary>
    [Fact]
    public async Task Own_data_source_is_disposed_when_the_host_stops()
    {
        var services = new ServiceCollection();
        services.AddTracon().UsePostgreSql(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = PostgresTestContext.NewSchemaName();
            options.AutoApplyMigrations = false;
        });

        DbDataSource dataSource;

        await using (var provider = services.BuildServiceProvider())
        {
            dataSource = provider.GetRequiredService<SqlStoreContext>().DataSource;
        }

        await Should.ThrowAsync<ObjectDisposedException>(
            async () => await dataSource.CreateCommand("SELECT 1;").ExecuteScalarAsync());
    }

    [Fact]
    public async Task Compiled_agent_runs_against_an_external_data_source()
    {
        await using var external = new NpgsqlDataSourceBuilder(fixture.ConnectionString).Build();
        var schemaName = PostgresTestContext.NewSchemaName();

        var services = new ServiceCollection();
        services.AddTracon()
            .AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage())
            .UsePostgreSql(options =>
            {
                options.DataSource = external;
                options.SchemaName = schemaName;
                options.AutoApplyMigrations = true;
            });

        await using var provider = services.BuildServiceProvider();

        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        var compiler = provider.GetRequiredService<AgentDefinitionCompiler>();
        var agent = compiler.Compile(TestData.Definition("with-external-data-source") with { ToolNames = [] });

        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("hello", session);

        await using var command = external.CreateCommand(
            $"SELECT count(*) FROM {schemaName}.conversation_items;");
        var itemCount = await command.ExecuteScalarAsync();
        Convert.ToInt64(itemCount, CultureInfo.InvariantCulture).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Non_Npgsql_DataSource_is_rejected_clearly()
    {
        var services = new ServiceCollection();
        services.AddTracon().UsePostgreSql(options =>
        {
            options.DataSource = new FakeDbDataSource();
            options.SchemaName = PostgresTestContext.NewSchemaName();
        });

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<TraconException>(
            () => provider.GetRequiredService<SqlStoreContext>());

        exception.Message.ShouldContain(nameof(NpgsqlDataSource));
    }

    /// <summary>
    /// A minimal <see cref="DbDataSource"/> that is deliberately NOT an
    /// <see cref="NpgsqlDataSource"/>, to prove the type check fires.
    /// </summary>
    private sealed class FakeDbDataSource : DbDataSource
    {
        public override string ConnectionString => string.Empty;

        protected override DbConnection CreateDbConnection() => throw new NotSupportedException();
    }
}
