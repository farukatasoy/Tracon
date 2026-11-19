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
/// </remarks>
public sealed class ServiceRegistrationTests(PostgresFixture fixture)
{
    [Fact]
    public void UsePostgreSql_replaces_the_in_memory_stores()
    {
        using var provider = BuildProvider();

        // Write-capable stores are wrapped by the audit-trail decorators;
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
    public void UsePostgreSql_says_its_state_is_shared_between_processes_and_outlives_one()
    {
        // The production profile reads this to refuse a declared multi-instance host whose store serves one
        // process (a database server is the answer it accepts), and to ask whether a run that a restart cut off is
        // still there to find (it is: the row stays in the database).
        using var provider = BuildProvider();

        var sharing = provider.GetRequiredService<StateSharing>();

        sharing.AcrossProcesses.ShouldBeTrue();
        sharing.OutlivesProcess.ShouldBeTrue();
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
    /// The provider fills the slot Tracon reads vector search availability from:
    /// empty while <c>EnableKnowledge</c> is off, which is what makes a definition
    /// with <c>EnableVectorSearch</c> fail compilation (<c>TraconServiceCollectionExtensions</c>).
    /// It registers no <c>IVectorSearchStore</c> in either state: a factory that
    /// returns null for "off" fails under Autofac (see <c>RegistrationFactoryNullTests</c>).
    /// </summary>
    [Fact]
    public void The_vector_search_slot_is_empty_when_knowledge_is_disabled()
    {
        using var provider = BuildProvider(static options => options.EnableKnowledge = false);

        provider.GetRequiredService<VectorSearchStoreSlot>().Store.ShouldBeNull();
        provider.GetService<IVectorSearchStore>().ShouldBeNull();
    }

    [Fact]
    public void The_vector_search_slot_holds_the_real_store_when_knowledge_is_enabled()
    {
        using var provider = BuildProvider(static options => options.EnableKnowledge = true);

        provider.GetRequiredService<VectorSearchStoreSlot>().Store.ShouldBeOfType<PgVectorSearchStore>();
        provider.GetService<IVectorSearchStore>().ShouldBeNull();
    }

    /// <summary>
    /// A consumer's own <c>IVectorSearchStore</c> wins over the PostgreSQL store, whether it is
    /// registered before or after <c>UsePostgreSql()</c>.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_consumers_own_vector_store_wins_over_the_provider(bool registeredFirst)
    {
        var own = new ConsumerVectorSearchStore();
        var services = new ServiceCollection();

        if (registeredFirst)
        {
            services.AddSingleton<IVectorSearchStore>(own);
        }

        services.AddTracon().UsePostgreSql(static options =>
        {
            options.ConnectionString = "Host=localhost;Port=1;Database=unused;Username=unused;Timeout=1";
            options.AutoApplyMigrations = false;
            options.EnableKnowledge = true;
        });

        if (!registeredFirst)
        {
            services.AddSingleton<IVectorSearchStore>(own);
        }

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<VectorSearchStoreSlot>().Store.ShouldBeSameAs(own);
    }

    private sealed class ConsumerVectorSearchStore : IVectorSearchStore
    {
        public int Dimensions => 3;

        public ValueTask UpsertAsync(
            string tenantId,
            string collection,
            string sourceId,
            IReadOnlyList<VectorChunk> chunks,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(
            VectorSearchRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<int> DeleteSourceAsync(
            string tenantId,
            string collection,
            string sourceId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> ListSourcesAsync(
            string tenantId,
            string collection,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The data source Tracon builds is no longer a
    /// public DI service — resolving it under its concrete Npgsql type must
    /// fail the same way it would for a type nobody ever registered. Before
    /// this change, <c>TryAddSingleton&lt;NpgsqlDataSource&gt;</c> meant a
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
    /// 🚨 An earlier defect regression test. <c>Tracon.Sql.Shared</c> is compiled
    /// SEPARATELY into each SQL provider (link-based sharing): so
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
