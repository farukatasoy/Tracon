using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// Tek bir test icin yalitilmis bir sema kurar ve depolari hazirlar.
/// </summary>
/// <remarks>
/// Her test kendi semasini kullanir. Bu iki isi ayni anda yapar: testler birbirinin
/// verisini gormez, ve <c>SchemaName</c> ayarinin gercekten calistigi her testte
/// dogrulanmis olur.
/// </remarks>
public sealed class PostgresTestContext : IAsyncDisposable
{
    private PostgresTestContext(
        NpgsqlDataSource dataSource,
        AgentPrismPostgreSqlOptions options,
        string tenantId)
    {
        DataSource = dataSource;
        Options = options;
        TenantContext = new FixedTenantContext(tenantId);

        var wrapped = Microsoft.Extensions.Options.Options.Create(options);

        AgentDefinitions = new PostgresAgentDefinitionStore(dataSource, wrapped, TenantContext);
        Runs = new PostgresRunStore(dataSource, wrapped, TenantContext);
        Sessions = new PostgresSessionStore(dataSource, wrapped, TenantContext);
        Traces = new PostgresTraceStore(dataSource, wrapped, TenantContext);
        ApprovalRules = new PostgresToolApprovalRuleStore(dataSource, wrapped);
        McpServers = new PostgresMcpServerStore(dataSource, wrapped);
        Tenants = new PostgresTenantStore(dataSource, wrapped);
        ChatHistory = new PostgresChatHistoryProvider(dataSource, wrapped, TenantContext);
        AuditLog = new PostgresAuditLog(dataSource, wrapped);
        SkillScriptGrants = new PostgresSkillScriptGrantStore(dataSource, wrapped);
        Attachments = new PostgresAttachmentStore(dataSource, wrapped);
        AgentFiles = new PostgresAgentFileStore(dataSource, wrapped, TenantContext);
        Workflows = new PostgresWorkflowDefinitionStore(dataSource, wrapped);
        WorkflowCheckpoints = new PostgresWorkflowCheckpointStore(dataSource, wrapped);
        Jobs = new PostgresJobStore(dataSource, wrapped);
        JobSchedules = new PostgresJobScheduleStore(dataSource, wrapped);
        Evals = new PostgresEvalStore(dataSource, wrapped);
        Experiments = new PostgresExperimentStore(dataSource, wrapped, TenantContext);
        Quotas = new PostgresQuotaStore(dataSource, wrapped);
        Webhooks = new PostgresWebhookStore(dataSource, wrapped);
        Migrations = new MigrationRunner(dataSource, wrapped, NullLogger<MigrationRunner>.Instance);
    }

    /// <summary>Bu baglamin veri kaynagi.</summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>Bu baglamin ayarlari.</summary>
    public AgentPrismPostgreSqlOptions Options { get; }

    /// <summary>Bu baglamin kiraci baglami.</summary>
    public ITenantContext TenantContext { get; }

    /// <summary>Agent tanim deposu.</summary>
    public PostgresAgentDefinitionStore AgentDefinitions { get; }

    /// <summary>Calistirma deposu.</summary>
    public PostgresRunStore Runs { get; }

    /// <summary>Oturum deposu.</summary>
    public PostgresSessionStore Sessions { get; }

    /// <summary>Span deposu (Faz 6).</summary>
    public PostgresTraceStore Traces { get; }

    /// <summary>Kalici onay kurali deposu (Faz 6).</summary>
    public PostgresToolApprovalRuleStore ApprovalRules { get; }

    /// <summary>MCP sunucu deposu (Faz 6).</summary>
    public PostgresMcpServerStore McpServers { get; }

    /// <summary>Kiraci kaydi deposu (Faz 6).</summary>
    public PostgresTenantStore Tenants { get; }

    /// <summary>Sohbet gecmisi saglayicisi.</summary>
    public PostgresChatHistoryProvider ChatHistory { get; }

    /// <summary>Denetim izi defteri (Faz 9).</summary>
    public PostgresAuditLog AuditLog { get; }

    /// <summary>Script calistirma izni deposu (Faz 11).</summary>
    public PostgresSkillScriptGrantStore SkillScriptGrants { get; }

    /// <summary>Ek deposu (Faz 14).</summary>
    public PostgresAttachmentStore Attachments { get; }

    /// <summary>Kalici agent dosya belleği (Faz 14).</summary>
    public PostgresAgentFileStore AgentFiles { get; }

    /// <summary>Workflow tanim deposu (Faz 15).</summary>
    public PostgresWorkflowDefinitionStore Workflows { get; }

    /// <summary>Workflow kontrol noktasi deposu (Faz 15).</summary>
    public PostgresWorkflowCheckpointStore WorkflowCheckpoints { get; }

    /// <summary>Is kuyrugu deposu (Faz 17).</summary>
    public PostgresJobStore Jobs { get; }

    /// <summary>Zamanlama deposu (Faz 17).</summary>
    public PostgresJobScheduleStore JobSchedules { get; }

    /// <summary>Eval takim/vaka/kosu deposu (Faz 18).</summary>
    public PostgresEvalStore Evals { get; }

    /// <summary>A/B deneyi deposu (Faz 19).</summary>
    public PostgresExperimentStore Experiments { get; }

    /// <summary>Kota deposu (Faz 21).</summary>
    public PostgresQuotaStore Quotas { get; }

    /// <summary>Webhook deposu (Faz 21).</summary>
    public PostgresWebhookStore Webhooks { get; }

    /// <summary>Migration calistiricisi.</summary>
    public MigrationRunner Migrations { get; }

    /// <summary>Kullanilan sema adi.</summary>
    public string SchemaName => Options.SchemaName;

    /// <summary>
    /// Yeni bir yalitilmis sema kurar, migration'lari uygular ve depolari hazirlar.
    /// </summary>
    /// <param name="fixture">Calisan PostgreSQL container'i.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="applyMigrations">Migration'lar hemen uygulansin mi.</param>
    /// <returns>Kullanima hazir baglam.</returns>
    public static async ValueTask<PostgresTestContext> CreateAsync(
        PostgresFixture fixture,
        string tenantId = "default",
        bool applyMigrations = true)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var context = Create(fixture, NewSchemaName(), tenantId);

        if (applyMigrations)
        {
            await context.Migrations.ApplyAsync();
        }

        return context;
    }

    /// <summary>
    /// Var olan bir semaya baglanan ikinci bir baglam kurar.
    /// Es zamanlilik ve kiraci yalitimi testleri icin kullanilir.
    /// </summary>
    /// <param name="fixture">Calisan PostgreSQL container'i.</param>
    /// <param name="schemaName">Kullanilacak sema adi.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <returns>Ayni semaya bakan yeni baglam.</returns>
    public static PostgresTestContext Create(PostgresFixture fixture, string schemaName, string tenantId = "default")
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var options = new AgentPrismPostgreSqlOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaName = schemaName,
            AutoApplyMigrations = false,
            CommandTimeoutSeconds = 30,
        };

        var dataSource = new NpgsqlDataSourceBuilder(options.ConnectionString).Build();

        return new PostgresTestContext(dataSource, options, tenantId);
    }

    /// <summary>Yeni ve benzersiz bir test sema adi uretir.</summary>
    /// <returns>Kucuk harflerden olusan gecerli bir tanimlayici.</returns>
    public static string NewSchemaName()
        => "t_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16];

    /// <summary>Baglamdaki ham SQL'i calistirir.</summary>
    /// <param name="sql">Calistirilacak SQL.</param>
    /// <returns>Etkilenen satir sayisi.</returns>
    public async ValueTask<int> ExecuteAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Tek deger donduren ham SQL calistirir.</summary>
    /// <typeparam name="T">Beklenen tip.</typeparam>
    /// <param name="sql">Calistirilacak SQL.</param>
    /// <returns>Ilk satirin ilk sutunu.</returns>
    public async ValueTask<T?> ScalarAsync<T>(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        var result = await command.ExecuteScalarAsync();

        return result is T value ? value : default;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await DataSource.DisposeAsync();

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }
}
