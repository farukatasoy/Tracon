using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// <c>UsePostgreSql()</c> cagrisinin bellek ici depolarin yerini gercekten aldigini dogrular.
/// </summary>
/// <remarks>
/// <para>
/// Bu testler <strong>sessiz bir hataya</strong> karsi koruma saglar: <c>AddAgentPrism()</c>
/// bellek ici depolari <c>TryAddSingleton</c> ile kaydeder ve zincirde once calisir.
/// <c>UsePostgreSql()</c> icinde <c>TryAdd</c> kullanilirsa hicbir sey olmaz; uygulama
/// hata vermeden bellek ici depoyla calismaya devam eder ve tum veri surecle birlikte kaybolur.
/// </para>
/// <para>Gerekce: <c>docs/KARARLAR.md</c>, karar K-025.</para>
/// </remarks>
public sealed class ServiceRegistrationTests(PostgresFixture fixture)
{
    [Fact]
    public void UsePostgreSql_bellek_ici_depolarin_yerini_alir()
    {
        using var provider = BuildProvider();

        // Yazma yapan depolar Faz 9'un denetim izi dekoratorleriyle sarilir; asil
        // testin dogruladigi sey (Postgres kazandi, bellek ici degil) dekoratorun
        // sardigi gercek uygulamaya bakilarak korunur.
        provider.GetRequiredService<IAgentDefinitionStore>()
            .ShouldBeOfType<AuditingAgentDefinitionStore>().AuditedInner
            .ShouldBeOfType<SqlAgentDefinitionStore>();
        provider.GetRequiredService<IRunStore>().ShouldBeOfType<SqlRunStore>();
        provider.GetRequiredService<ISessionStore>()
            .ShouldBeOfType<AuditingSessionStore>().AuditedInner
            .ShouldBeOfType<SqlSessionStore>();
    }

    [Fact]
    public void UsePostgreSql_sohbet_gecmisi_saglayicisini_kaydeder()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<ChatHistoryProvider>().ShouldBeOfType<SqlChatHistoryProvider>();
    }

    [Fact]
    public void UsePostgreSql_migration_servisini_kaydeder()
    {
        using var provider = BuildProvider();

        provider.GetServices<IHostedService>().OfType<MigrationHostedService>().ShouldHaveSingleItem();
        provider.GetRequiredService<MigrationRunner>().ShouldNotBeNull();
    }

    [Fact]
    public void Ayarlar_yapilandirmadan_okunur()
    {
        using var provider = BuildProvider(options =>
        {
            options.SchemaName = "ozel_sema";
            options.CommandTimeoutSeconds = 90;
            options.AutoApplyMigrations = false;
        });

        var options = provider.GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value;

        options.SchemaName.ShouldBe("ozel_sema");
        options.CommandTimeoutSeconds.ShouldBe(90);
        options.AutoApplyMigrations.ShouldBeFalse();
    }

    [Fact]
    public void Baglanti_dizesi_bos_ise_dogrulama_hata_verir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UsePostgreSql(options => options.ConnectionString = "   ");

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value);

        exception.Message.ShouldContain(nameof(AgentPrismPostgreSqlOptions.ConnectionString));
    }

    [Fact]
    public void Public_sema_adi_reddedilir()
    {
        using var provider = BuildProvider(options => options.SchemaName = "public");

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value);

        exception.Message.ShouldContain("public");
    }

    [Fact]
    public void Gecersiz_sema_adi_reddedilir()
    {
        using var provider = BuildProvider(options => options.SchemaName = "Kotu Ad");

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value);
    }

    [Fact]
    public async Task Derlenen_agent_sohbet_gecmisini_veritabanina_yazar()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        var services = new ServiceCollection();
        services.AddSingleton(context.TenantContext);
        services.AddAgentPrism()
            .AddModelProvider(new EchoModelProvider())
            .UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.SchemaName = context.SchemaName;
                options.AutoApplyMigrations = false;
            });

        await using var provider = services.BuildServiceProvider();

        var compiler = provider.GetRequiredService<AgentDefinitionCompiler>();
        var agent = compiler.Compile(TestData.Definition("gecmisli") with { ToolNames = [] });

        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("merhaba", session);

        // Derleyici saglayiciyi gercekten bagladiysa mesajlar tabloya yazilmistir.
        var itemCount = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.SchemaName}.conversation_items;");

        itemCount.ShouldBeGreaterThan(0);
    }

    private ServiceProvider BuildProvider(Action<AgentPrismPostgreSqlOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddAgentPrism().UsePostgreSql(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = PostgresTestContext.NewSchemaName();
            options.AutoApplyMigrations = false;
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }
}
