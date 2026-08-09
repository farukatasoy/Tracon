using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
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
internal sealed class PostgresTestContext : IAsyncDisposable
{
    /// <summary>
    /// Testlerde kullanilan varsayilan gomu boyutu (Faz 51). Kucuk tutulur:
    /// vektor testleri disindaki 40+ paket bu degerden BAGIMSIZDIR, yalniz
    /// <c>document_embeddings.embedding</c> sutununun tipini belirler.
    /// </summary>
    public const int DefaultVectorDimensions = 3;

    private PostgresTestContext(
        NpgsqlDataSource dataSource,
        AgentPrismPostgreSqlOptions options,
        ITenantContext tenantContext,
        int vectorDimensions)
    {
        DataSource = dataSource;
        Options = options;
        TenantContext = tenantContext;
        VectorDimensions = vectorDimensions;

        var wrapped = new SqlStoreContext
        {
            DataSource = dataSource,
            Dialect = new PostgresDialect(options.SchemaName),
            CommandTimeoutSeconds = options.CommandTimeoutSeconds,
            ProviderName = "PostgreSQL",
            MigrationTemplateValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dimension"] = vectorDimensions.ToString(CultureInfo.InvariantCulture),
            },
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
        AgentSkills = new SqlAgentSkillStore(wrapped);
        Attachments = new SqlAttachmentStore(wrapped);
        AgentFiles = new SqlAgentFileStore(wrapped, TenantContext);
        Vectors = new PgVectorSearchStore(
            dataSource,
            options,
            new AgentPrismKnowledgeOptions { Dimensions = vectorDimensions });
        Workflows = new SqlWorkflowDefinitionStore(wrapped);
        WorkflowCheckpoints = new SqlWorkflowCheckpointStore(wrapped);
        Jobs = new SqlJobStore(wrapped);
        JobSchedules = new SqlJobScheduleStore(wrapped);
        Evals = new SqlEvalStore(wrapped);
        Experiments = new SqlExperimentStore(wrapped, TenantContext);
        Quotas = new SqlQuotaStore(wrapped);
        Webhooks = new SqlWebhookStore(wrapped);
        ApiKeys = new SqlApiKeyStore(wrapped);
        RetentionPolicies = new SqlRetentionPolicyStore(wrapped);
        RetentionData = new SqlRetentionStore(wrapped);
        VoiceSessions = new SqlVoiceSessionStore(wrapped);
        RunScores = new SqlRunScoreStore(wrapped);
        SingletonLeases = new SqlSingletonLeaseStore(wrapped);
        IdempotencyKeys = new SqlIdempotencyStore(wrapped);
        RunInputs = new SqlRunInputStore(wrapped);
        ConversationBranches = new SqlConversationBranchStore(wrapped);
        PendingApprovals = new SqlPendingApprovalStore(wrapped, TenantContext);
        Migrations = new MigrationRunner(wrapped, NullLogger<MigrationRunner>.Instance);
    }

    /// <summary>Bu baglamin veri kaynagi.</summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>Paylasilan depo katmaninin baglami.</summary>
    public SqlStoreContext StoreContext { get; }

    /// <summary>Bu baglamin ayarlari.</summary>
    public AgentPrismPostgreSqlOptions Options { get; }

    /// <summary>Bu baglamin kiraci baglami.</summary>
    public ITenantContext TenantContext { get; }

    /// <summary>Migration 0024'e uygulanan gomu boyutu (Faz 51).</summary>
    public int VectorDimensions { get; }

    /// <summary>Agent tanim deposu.</summary>
    public SqlAgentDefinitionStore AgentDefinitions { get; }

    /// <summary>Calistirma deposu.</summary>
    public SqlRunStore Runs { get; }

    /// <summary>Oturum deposu.</summary>
    public SqlSessionStore Sessions { get; }

    /// <summary>Span deposu (Faz 6).</summary>
    public SqlTraceStore Traces { get; }

    /// <summary>Kalici onay kurali deposu (Faz 6).</summary>
    public SqlToolApprovalRuleStore ApprovalRules { get; }

    /// <summary>Bekleyen onay istegi deposu (Faz 55).</summary>
    public SqlPendingApprovalStore PendingApprovals { get; }

    /// <summary>MCP sunucu deposu (Faz 6).</summary>
    public SqlMcpServerStore McpServers { get; }

    /// <summary>Kiraci kaydi deposu (Faz 6).</summary>
    public SqlTenantStore Tenants { get; }

    /// <summary>Sohbet gecmisi saglayicisi.</summary>
    public SqlChatHistoryProvider ChatHistory { get; }

    /// <summary>Denetim izi defteri (Faz 9).</summary>
    public SqlAuditLog AuditLog { get; }

    /// <summary>Script calistirma izni deposu (Faz 11).</summary>
    public SqlSkillScriptGrantStore SkillScriptGrants { get; }

    /// <summary>Calisma ani skill deposu (Faz 10).</summary>
    public SqlAgentSkillStore AgentSkills { get; }

    /// <summary>Ek deposu (Faz 14).</summary>
    public SqlAttachmentStore Attachments { get; }

    /// <summary>Kalici agent dosya belleği (Faz 14).</summary>
    public SqlAgentFileStore AgentFiles { get; }

    /// <summary>Vektor tabanli anlamsal arama deposu (Faz 51).</summary>
    public PgVectorSearchStore Vectors { get; }

    /// <summary>Workflow tanim deposu (Faz 15).</summary>
    public SqlWorkflowDefinitionStore Workflows { get; }

    /// <summary>Workflow kontrol noktasi deposu (Faz 15).</summary>
    public SqlWorkflowCheckpointStore WorkflowCheckpoints { get; }

    /// <summary>Is kuyrugu deposu (Faz 17).</summary>
    public SqlJobStore Jobs { get; }

    /// <summary>Zamanlama deposu (Faz 17).</summary>
    public SqlJobScheduleStore JobSchedules { get; }

    /// <summary>Eval takim/vaka/kosu deposu (Faz 18).</summary>
    public SqlEvalStore Evals { get; }

    /// <summary>A/B deneyi deposu (Faz 19).</summary>
    public SqlExperimentStore Experiments { get; }

    /// <summary>Kota deposu (Faz 21).</summary>
    public SqlQuotaStore Quotas { get; }

    /// <summary>Webhook deposu (Faz 21).</summary>
    public SqlWebhookStore Webhooks { get; }

    /// <summary>Kiraci bazli API anahtari deposu (Faz 53).</summary>
    public SqlApiKeyStore ApiKeys { get; }

    /// <summary>Saklama politikasi ve kosu gecmisi deposu (Faz 25).</summary>
    public SqlRetentionPolicyStore RetentionPolicies { get; }

    /// <summary>Saklama veri duzlemi (sayma/silme/arsiv okuma) (Faz 25).</summary>
    public SqlRetentionStore RetentionData { get; }

    /// <summary>Konusma kaydi deposu (Faz 29).</summary>
    public SqlVoiceSessionStore VoiceSessions { get; }

    /// <summary>Calistirma/mesaj puani deposu (Faz 31).</summary>
    public SqlRunScoreStore RunScores { get; }

    /// <summary>Tek yurutucu secimi kira deposu (Faz 42).</summary>
    public SqlSingletonLeaseStore SingletonLeases { get; }

    /// <summary>Idempotency deposu (Faz 43).</summary>
    public SqlIdempotencyStore IdempotencyKeys { get; }

    /// <summary>Calistirma girdi deposu (Faz 47).</summary>
    public SqlRunInputStore RunInputs { get; }

    /// <summary>Konusma dallandirma deposu (Faz 47).</summary>
    public SqlConversationBranchStore ConversationBranches { get; }

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
    public static ValueTask<PostgresTestContext> CreateAsync(
        PostgresFixture fixture,
        string tenantId = "default",
        bool applyMigrations = true,
        int vectorDimensions = DefaultVectorDimensions)
        => CreateAsync(fixture, new FixedTenantContext(tenantId), applyMigrations, vectorDimensions);

    /// <summary>
    /// Kiraci baglami disaridan verilen kurulum. Kiraci yalitimi sozlesmesi
    /// ayni depo ornegi uzerinde kiraci degistirdigi icin bu asiri yuklemeyi
    /// kullanir (Faz 41).
    /// </summary>
    /// <param name="fixture">Calisan PostgreSQL container.</param>
    /// <param name="tenantContext">Depolarin okuyacagi kiraci baglami.</param>
    /// <param name="applyMigrations">Migration'lar hemen uygulansin mi.</param>
    /// <returns>Kullanima hazir baglam.</returns>
    public static async ValueTask<PostgresTestContext> CreateAsync(
        PostgresFixture fixture,
        ITenantContext tenantContext,
        bool applyMigrations = true,
        int vectorDimensions = DefaultVectorDimensions)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var context = Create(fixture, NewSchemaName(), tenantContext, vectorDimensions);

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
    public static PostgresTestContext Create(
        PostgresFixture fixture,
        string schemaName,
        string tenantId = "default",
        int vectorDimensions = DefaultVectorDimensions)
        => Create(fixture, schemaName, new FixedTenantContext(tenantId), vectorDimensions);

    /// <summary>Kiraci baglami disaridan verilen kurulum.</summary>
    /// <param name="fixture">Calisan PostgreSQL container.</param>
    /// <param name="schemaName">Kullanilacak sema adi.</param>
    /// <param name="tenantContext">Depolarin okuyacagi kiraci baglami.</param>
    /// <param name="vectorDimensions">Migration 0024'e uygulanacak gomu boyutu (Faz 51).</param>
    /// <returns>Ayni arka uca bakan yeni baglam.</returns>
    public static PostgresTestContext Create(
        PostgresFixture fixture,
        string schemaName,
        ITenantContext tenantContext,
        int vectorDimensions = DefaultVectorDimensions)
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

        return new PostgresTestContext(dataSource, options, tenantContext, vectorDimensions);
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
}
