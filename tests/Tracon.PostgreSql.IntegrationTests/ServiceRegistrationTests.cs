using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Verifies that the <c>UsePostgreSql()</c> call really replaces the in-memory stores.
/// </summary>
/// <remarks>
/// <para>
/// These tests guard against a <strong>silent failure</strong>: <c>AddTracon()</c>
/// registers the in-memory stores with <c>TryAddSingleton</c> and runs first in the
/// chain. If <c>UsePostgreSql()</c> used <c>TryAdd</c>, nothing would happen; the
/// application would keep working with the in-memory store without erroring, and
/// all data would be lost with the process.
/// </para>
/// <para>Rationale: <c>docs/KARARLAR.md</c>, decision K-025.</para>
/// </remarks>
public sealed class ServiceRegistrationTests(PostgresFixture fixture)
{
    [Fact]
    public void UsePostgreSql_replaces_the_in_memory_stores()
    {
        using var provider = BuildProvider();

        // Write-capable stores are wrapped by Phase 9's audit-trail decorators;
        // the actual thing this test verifies (Postgres won, not in-memory) is
        // preserved by looking at the real implementation the decorator wraps.
        provider.GetRequiredService<IAgentDefinitionStore>()
            .ShouldBeOfType<AuditingAgentDefinitionStore>().AuditedInner
            .ShouldBeOfType<SqlAgentDefinitionStore>();
        provider.GetRequiredService<IRunStore>().ShouldBeOfType<SqlRunStore>();
        provider.GetRequiredService<ISessionStore>()
            .ShouldBeOfType<AuditingSessionStore>().AuditedInner
            .ShouldBeOfType<SqlSessionStore>();
    }

    [Fact]
    public void UsePostgreSql_registers_the_chat_history_provider()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<ChatHistoryProvider>().ShouldBeOfType<SqlChatHistoryProvider>();
    }

    [Fact]
    public void UsePostgreSql_registers_the_migration_service()
    {
        using var provider = BuildProvider();

        provider.GetServices<IHostedService>().OfType<MigrationHostedService>().ShouldHaveSingleItem();
        provider.GetRequiredService<MigrationRunner>().ShouldNotBeNull();
    }

    /// <summary>
    /// Phase 67, K-476: <c>IVectorSearchStore</c> stays REGISTERED either way
    /// (<c>TryAddSingleton</c> ran); the factory decides whether it resolves
    /// to a real store or <see langword="null"/> based on <c>EnableKnowledge</c>,
    /// reusing the "GetService null = not available" gate from phase 51
    /// (<c>TraconServiceCollectionExtensions</c>, <c>EnableVectorSearch</c>).
    /// </summary>
    [Fact]
    public void IVectorSearchStore_resolves_to_null_when_knowledge_is_disabled()
    {
        using var provider = BuildProvider(static options => options.EnableKnowledge = false);

        provider.GetService<IVectorSearchStore>().ShouldBeNull();
    }

    [Fact]
    public void IVectorSearchStore_resolves_to_the_real_store_when_knowledge_is_enabled()
    {
        using var provider = BuildProvider(static options => options.EnableKnowledge = true);

        provider.GetService<IVectorSearchStore>().ShouldBeOfType<PgVectorSearchStore>();
    }

    /// <summary>
    /// Phase 110, K-625: the data source Tracon builds is no longer a
    /// public DI service — resolving it under its concrete Npgsql type must
    /// fail the same way it would for a type nobody ever registered. Before
    /// this phase, <c>TryAddSingleton&lt;NpgsqlDataSource&gt;</c> meant a
    /// consumer's own registration could silently win (or lose) depending on
    /// call order; the only way in now is the explicit
    /// <see cref="TraconPostgreSqlOptions.DataSource"/> field.
    /// </summary>
    [Fact]
    public void The_data_source_is_not_registered_as_a_public_DI_service()
    {
        using var provider = BuildProvider();

        provider.GetService<Npgsql.NpgsqlDataSource>().ShouldBeNull();
    }

    [Fact]
    public void Settings_are_read_from_configuration()
    {
        using var provider = BuildProvider(options =>
        {
            options.SchemaName = "custom_schema";
            options.CommandTimeoutSeconds = 90;
            options.AutoApplyMigrations = false;
        });

        var options = provider.GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value;

        options.SchemaName.ShouldBe("custom_schema");
        options.CommandTimeoutSeconds.ShouldBe(90);
        options.AutoApplyMigrations.ShouldBeFalse();
    }

    [Fact]
    public void Validation_throws_when_connection_string_is_blank()
    {
        var services = new ServiceCollection();
        services.AddTracon().UsePostgreSql(options => options.ConnectionString = "   ");

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value);

        exception.Message.ShouldContain(nameof(TraconPostgreSqlOptions.ConnectionString));
    }

    [Fact]
    public void Public_schema_name_is_rejected()
    {
        using var provider = BuildProvider(options => options.SchemaName = "public");

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value);

        exception.Message.ShouldContain("public");
    }

    [Fact]
    public void Invalid_schema_name_is_rejected()
    {
        using var provider = BuildProvider(options => options.SchemaName = "Bad Name");

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value);
    }

    [Fact]
    public async Task Compiled_agent_writes_chat_history_to_the_database()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        var services = new ServiceCollection();
        services.AddSingleton(context.TenantContext);
        services.AddTracon()
            .AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage())
            .UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.SchemaName = context.SchemaName;
                options.AutoApplyMigrations = false;
            });

        await using var provider = services.BuildServiceProvider();

        var compiler = provider.GetRequiredService<AgentDefinitionCompiler>();
        var agent = compiler.Compile(TestData.Definition("with-history") with { ToolNames = [] });

        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("hello", session);

        // If the compiler really wired up the provider, messages have been written to the table.
        var itemCount = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.SchemaName}.conversation_items;");

        itemCount.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// 🚨 MT-PKG-082 regression test. <c>Tracon.Sql.Shared</c> is compiled
    /// SEPARATELY into each SQL provider (K-176, link-based sharing): so
    /// <c>SqlStoreContext</c>/<c>MigrationRunner</c>/<c>MigrationHostedService</c>
    /// are a DIFFERENT CLR type per provider, and <c>services.Replace(...)</c>
    /// replaces only ITS OWN type — it does NOT REMOVE the competing provider's
    /// registration. Before the fix, this caused BOTH providers to apply
    /// migrations and write to their own database when two providers were
    /// registered at once (the "last registration wins" claim held only for a
    /// repeat registration of the SAME provider, not for DIFFERENT providers).
    /// </summary>
    /// <remarks>
    /// A second real provider (e.g. Tracon.Sqlite) is DELIBERATELY NOT
    /// referenced: each SQL provider project links <c>Tracon.Sql.Shared</c>
    /// into its own assembly, and PUBLIC types like <c>MigrationRunner</c>
    /// collide with CS0433 when two providers are referenced in the same
    /// project (measured in this session). The competing provider's presence
    /// is therefore SIMULATED with the shared (<c>Tracon.Abstractions</c>)
    /// <see cref="SqlPersistenceRegistrationMarker"/> marker —
    /// <c>MigrationHostedService.IsWinningProvider()</c> looks at exactly this
    /// marker, not a real second connection.
    /// </remarks>
    [Fact]
    public async Task Losing_provider_does_not_apply_migrations()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        var services = new ServiceCollection();
        services.AddSingleton(context.TenantContext);

        services.AddTracon().UsePostgreSql(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = context.SchemaName;
            options.AutoApplyMigrations = true;
        });

        // Simulates "SQLite" registered AFTER: PostgreSQL is now the loser.
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQLite"));

        await using var provider = services.BuildServiceProvider();

        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        // PostgreSQL lost: it must not have created its own schema at all.
        var schemaCreated = await context.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = "
            + $"'{context.SchemaName}';");

        schemaCreated.ShouldBe(0);
    }

    /// <summary>Mirror test: when PostgreSQL is registered LAST (the winner), migration is really applied.</summary>
    [Fact]
    public async Task Winning_provider_applies_migrations()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        var services = new ServiceCollection();
        services.AddSingleton(context.TenantContext);

        // Simulates "SQLite" registered FIRST.
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQLite"));

        services.AddTracon().UsePostgreSql(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = context.SchemaName;
            options.AutoApplyMigrations = true;
        });

        await using var provider = services.BuildServiceProvider();

        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        var migrationCount = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.SchemaName}.__migrations;");

        migrationCount.ShouldBeGreaterThan(0);
    }

    private ServiceProvider BuildProvider(Action<TraconPostgreSqlOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddTracon().UsePostgreSql(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = PostgresTestContext.NewSchemaName();
            options.AutoApplyMigrations = false;
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }
}
