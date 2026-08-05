using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// Tek bir test icin yalitilmis bir tablo oneki kurar ve depolari hazirlar.
/// </summary>
/// <remarks>
/// Her test kendi tablo onekini kullanir. Bu iki isi ayni anda yapar: testler
/// birbirinin verisini gormez, ve <c>TablePrefix</c> ayarinin gercekten
/// calistigi her testte dogrulanmis olur.
/// </remarks>
internal sealed class SqliteTestContext : IAsyncDisposable
{
    private SqliteTestContext(
        SqliteDataSource dataSource,
        AgentPrismSqliteOptions options,
        string tenantId)
    {
        DataSource = dataSource;
        Options = options;
        TenantContext = new FixedTenantContext(tenantId);

        var wrapped = new SqlStoreContext
        {
            DataSource = dataSource,
            Dialect = new SqliteDialect(options.TablePrefix),
            CommandTimeoutSeconds = options.CommandTimeoutSeconds,
            ProviderName = "SQLite",
        };

        StoreContext = wrapped;

        AgentDefinitions = new SqlAgentDefinitionStore(wrapped, TenantContext);
        Runs = new SqlRunStore(wrapped, TenantContext);
        Sessions = new SqlSessionStore(wrapped, TenantContext);
        Traces = new SqlTraceStore(wrapped, TenantContext);
        ApprovalRules = new SqlToolApprovalRuleStore(wrapped);
        McpServers = new SqlMcpServerStore(wrapped);
        Tenants = new SqlTenantStore(wrapped);
        ChatHistory = new SqlChatHistoryProvider(wrapped, TenantContext);
        AuditLog = new SqlAuditLog(wrapped);
        SkillScriptGrants = new SqlSkillScriptGrantStore(wrapped);
        Attachments = new SqlAttachmentStore(wrapped);
        AgentFiles = new SqlAgentFileStore(wrapped, TenantContext);
        Workflows = new SqlWorkflowDefinitionStore(wrapped);
        WorkflowCheckpoints = new SqlWorkflowCheckpointStore(wrapped);
        Jobs = new SqlJobStore(wrapped);
        JobSchedules = new SqlJobScheduleStore(wrapped);
        Evals = new SqlEvalStore(wrapped);
        Experiments = new SqlExperimentStore(wrapped, TenantContext);
        Quotas = new SqlQuotaStore(wrapped);
        Webhooks = new SqlWebhookStore(wrapped);
        Migrations = new MigrationRunner(wrapped, NullLogger<MigrationRunner>.Instance);
    }

    /// <summary>Bu baglamin veri kaynagi.</summary>
    public SqliteDataSource DataSource { get; }

    /// <summary>Paylasilan depo katmaninin baglami.</summary>
    public SqlStoreContext StoreContext { get; }

    /// <summary>Bu baglamin ayarlari.</summary>
    public AgentPrismSqliteOptions Options { get; }

    /// <summary>Bu baglamin kiraci baglami.</summary>
    public ITenantContext TenantContext { get; }

    /// <summary>Agent tanim deposu.</summary>
    public SqlAgentDefinitionStore AgentDefinitions { get; }

    /// <summary>Calistirma deposu.</summary>
    public SqlRunStore Runs { get; }

    /// <summary>Oturum deposu.</summary>
    public SqlSessionStore Sessions { get; }

    /// <summary>Span deposu.</summary>
    public SqlTraceStore Traces { get; }

    /// <summary>Kalici onay kurali deposu.</summary>
    public SqlToolApprovalRuleStore ApprovalRules { get; }

    /// <summary>MCP sunucu deposu.</summary>
    public SqlMcpServerStore McpServers { get; }

    /// <summary>Kiraci kaydi deposu.</summary>
    public SqlTenantStore Tenants { get; }

    /// <summary>Sohbet gecmisi saglayicisi.</summary>
    public SqlChatHistoryProvider ChatHistory { get; }

    /// <summary>Denetim izi defteri.</summary>
    public SqlAuditLog AuditLog { get; }

    /// <summary>Script calistirma izni deposu.</summary>
    public SqlSkillScriptGrantStore SkillScriptGrants { get; }

    /// <summary>Ek deposu.</summary>
    public SqlAttachmentStore Attachments { get; }

    /// <summary>Kalici agent dosya belleği.</summary>
    public SqlAgentFileStore AgentFiles { get; }

    /// <summary>Workflow tanim deposu.</summary>
    public SqlWorkflowDefinitionStore Workflows { get; }

    /// <summary>Workflow kontrol noktasi deposu.</summary>
    public SqlWorkflowCheckpointStore WorkflowCheckpoints { get; }

    /// <summary>Is kuyrugu deposu.</summary>
    public SqlJobStore Jobs { get; }

    /// <summary>Zamanlama deposu.</summary>
    public SqlJobScheduleStore JobSchedules { get; }

    /// <summary>Eval takim/vaka/kosu deposu.</summary>
    public SqlEvalStore Evals { get; }

    /// <summary>A/B deneyi deposu.</summary>
    public SqlExperimentStore Experiments { get; }

    /// <summary>Kota deposu.</summary>
    public SqlQuotaStore Quotas { get; }

    /// <summary>Webhook deposu.</summary>
    public SqlWebhookStore Webhooks { get; }

    /// <summary>Migration calistiricisi.</summary>
    public MigrationRunner Migrations { get; }

    /// <summary>Kullanilan tablo oneki.</summary>
    public string TablePrefix => Options.TablePrefix;

    /// <summary>
    /// Yeni bir yalitilmis tablo oneki kurar, migration'lari uygular ve depolari hazirlar.
    /// </summary>
    /// <param name="fixture">Calisan SQLite veritabani dosyasi.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="applyMigrations">Migration'lar hemen uygulansin mi.</param>
    /// <returns>Kullanima hazir baglam.</returns>
    public static async ValueTask<SqliteTestContext> CreateAsync(
        SqliteFixture fixture,
        string tenantId = "default",
        bool applyMigrations = true)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var context = Create(fixture, NewTablePrefix(), tenantId);

        if (applyMigrations)
        {
            await context.Migrations.ApplyAsync();
        }

        return context;
    }

    /// <summary>
    /// Var olan bir tablo onekine baglanan ikinci bir baglam kurar.
    /// Es zamanlilik ve kiraci yalitimi testleri icin kullanilir.
    /// </summary>
    /// <param name="fixture">Calisan SQLite veritabani dosyasi.</param>
    /// <param name="tablePrefix">Kullanilacak tablo oneki.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <returns>Ayni onege bakan yeni baglam.</returns>
    public static SqliteTestContext Create(SqliteFixture fixture, string tablePrefix, string tenantId = "default")
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var options = new AgentPrismSqliteOptions
        {
            ConnectionString = fixture.ConnectionString,
            TablePrefix = tablePrefix,
            AutoApplyMigrations = false,
            CommandTimeoutSeconds = 30,
        };

        var dataSource = new SqliteDataSource(options.ConnectionString!);

        return new SqliteTestContext(dataSource, options, tenantId);
    }

    /// <summary>Yeni ve benzersiz bir test tablo oneki uretir.</summary>
    /// <returns>Kucuk harflerden olusan gecerli bir tanimlayici, alt cizgiyle biter.</returns>
    public static string NewTablePrefix()
        => "t_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16] + "_";

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

    /// <summary>Test tablolarini birakir.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// SQLite'ta sema kavrami yoktur; her test oneki kendi tablo kumesini
    /// birakir ki tek dosyada yuzlerce test tablosu birikmesin.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        var tableNames = await ReadTableNamesAsync().ConfigureAwait(false);

        foreach (var tableName in tableNames)
        {
            await ExecuteAsync($"DROP TABLE IF EXISTS \"{tableName}\";").ConfigureAwait(false);
        }

        await DataSource.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<List<string>> ReadTableNamesAsync()
    {
        await using var command = DataSource.CreateCommand(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE @prefix ESCAPE '\\';");

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@prefix";
        parameter.Value = TablePrefix.Replace("_", "\\_", StringComparison.Ordinal) + "%";
        command.Parameters.Add(parameter);

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }
}
