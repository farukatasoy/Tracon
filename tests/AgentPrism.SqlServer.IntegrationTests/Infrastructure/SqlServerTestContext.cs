using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.SqlServer.IntegrationTests.Infrastructure;

/// <summary>
/// Yalitilmis bir sema kurar ve depolari hazirlar.
/// </summary>
/// <remarks>
/// Bir sema genellikle bir sozlesme test SINIFI tarafindan paylasilir
/// (bkz. <see cref="SqlServerSchemaFixture"/>); testler arasi izolasyon
/// <see cref="ResetDataAsync"/> ile saglanir, ayri sema ile degil.
/// <c>SchemaName</c> ayarinin varsayilan olmayan bir semada dogru calistigi
/// <c>MigrationRunnerTests</c>'te ayrica dogrulanir.
/// </remarks>
internal sealed class SqlServerTestContext : IAsyncDisposable
{
    private SqlServerTestContext(
        SqlServerDataSource dataSource,
        AgentPrismSqlServerOptions options,
        ITenantContext tenantContext)
    {
        DataSource = dataSource;
        Options = options;
        TenantContext = tenantContext;

        var wrapped = new SqlStoreContext
        {
            DataSource = dataSource,
            Dialect = new SqlServerDialect(options.SchemaName),
            CommandTimeoutSeconds = options.CommandTimeoutSeconds,
            ProviderName = "SQL Server",
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
    public SqlServerDataSource DataSource { get; }

    /// <summary>Paylasilan depo katmaninin baglami.</summary>
    public SqlStoreContext StoreContext { get; }

    /// <summary>Bu baglamin ayarlari.</summary>
    public AgentPrismSqlServerOptions Options { get; }

    /// <summary>Bu baglamin kiraci baglami.</summary>
    public ITenantContext TenantContext { get; }

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
    /// <param name="fixture">Calisan SQL Server container'i.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="applyMigrations">Migration'lar hemen uygulansin mi.</param>
    /// <returns>Kullanima hazir baglam.</returns>
    public static ValueTask<SqlServerTestContext> CreateAsync(
        SqlServerFixture fixture,
        string tenantId = "default",
        bool applyMigrations = true)
        => CreateAsync(fixture, new FixedTenantContext(tenantId), applyMigrations);

    /// <summary>
    /// Kiraci baglami disaridan verilen kurulum. Kiraci yalitimi sozlesmesi
    /// ayni depo ornegi uzerinde kiraci degistirdigi icin bu asiri yuklemeyi
    /// kullanir (Faz 41).
    /// </summary>
    /// <param name="fixture">Calisan SQL Server container.</param>
    /// <param name="tenantContext">Depolarin okuyacagi kiraci baglami.</param>
    /// <param name="applyMigrations">Migration'lar hemen uygulansin mi.</param>
    /// <returns>Kullanima hazir baglam.</returns>
    public static async ValueTask<SqlServerTestContext> CreateAsync(
        SqlServerFixture fixture,
        ITenantContext tenantContext,
        bool applyMigrations = true)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var context = Create(fixture, NewSchemaName(), tenantContext);

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
    /// <param name="fixture">Calisan SQL Server container'i.</param>
    /// <param name="schemaName">Kullanilacak sema adi.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <returns>Ayni semaya bakan yeni baglam.</returns>
    public static SqlServerTestContext Create(SqlServerFixture fixture, string schemaName, string tenantId = "default")
        => Create(fixture, schemaName, new FixedTenantContext(tenantId));

    /// <summary>Kiraci baglami disaridan verilen kurulum.</summary>
    /// <param name="fixture">Calisan SQL Server container.</param>
    /// <param name="schemaName">Kullanilacak sema adi.</param>
    /// <param name="tenantContext">Depolarin okuyacagi kiraci baglami.</param>
    /// <returns>Ayni arka uca bakan yeni baglam.</returns>
    public static SqlServerTestContext Create(SqlServerFixture fixture, string schemaName, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var options = new AgentPrismSqlServerOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaName = schemaName,
            AutoApplyMigrations = false,
            CommandTimeoutSeconds = 30,
        };

        var dataSource = new SqlServerDataSource(options.ConnectionString!);

        return new SqlServerTestContext(dataSource, options, tenantContext);
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

    /// <summary>Test semasini ve icindeki her seyi birakir.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// PostgreSQL testleri semayi birakmaz; container zaten yok edilir. SQL
    /// Server'da her test kendi semasini birakir cunku tek bir veritabaninda
    /// yuzlerce sema birikirse sistem gorunumleri (sys.indexes uzerinden calisan
    /// migration kosullari) belirgin olarak yavaslar.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await DropSchemaAsync().ConfigureAwait(false);
        await DataSource.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Onbelleklenen veri sifirlama toplu SQL metni. Sinif basina bir kez hesaplanir.</summary>
    private string? _resetBatchSql;

    /// <summary>
    /// Semadaki tum veri tablolarini tek round-trip'te bosaltir; sema ve
    /// <c>__migrations</c> defteri KALIR.
    /// </summary>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// Silme sirasi <c>sys.foreign_keys</c>'ten hesaplanan bir topolojik siralamadir
    /// (referans eden tablo, referans edilenden once silinir); tablo listesi de
    /// katalogdan okunur, sabit yazilmaz. Boylece yeni bir migration tablo eklerse
    /// bu metot elle guncellenmeden dogru kalir.
    /// </remarks>
    public async ValueTask ResetDataAsync()
    {
        _resetBatchSql ??= await BuildResetBatchSqlAsync().ConfigureAwait(false);

        if (_resetBatchSql.Length > 0)
        {
            await ExecuteAsync(_resetBatchSql).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Silme sirasini katalogdan hesaplar ve tek bir toplu <c>DELETE</c> metni uretir.
    /// </summary>
    /// <returns>Calistirilacak SQL metni; sema bossa bos dize.</returns>
    /// <remarks>
    /// 🚨 Bu okuma <c>sys.foreign_keys</c>/<c>sys.tables</c> katalog gorunumlerini
    /// tarar — <see cref="DropSchemaAsync"/> ile ayni paylasilan kaynak. Sinif basina
    /// bir kez ve butun sinif fixture'lari BASLANGICTA es zamanli calistigi icin
    /// (bkz. sema fixture'lari) ayni deadlock riski burada da vardir; ayni yeniden
    /// deneme ile korunur.
    /// </remarks>
    private async ValueTask<string> BuildResetBatchSqlAsync()
    {
        // SchemaName SqlServerDialect kurulurken SqlIdentifier.RequireSchemaName ile
        // dogrulanmistir; dogrudan SQL metnine yerlestirmek DropSchemaAsync ile ayni
        // gerekceyle guvenlidir.
        var sql = $"""
            SELECT t.name, NULL
            FROM sys.tables AS t
            JOIN sys.schemas AS s ON t.schema_id = s.schema_id
            WHERE s.name = N'{SchemaName}' AND t.name <> N'__migrations'
            UNION ALL
            SELECT tp.name, tr.name
            FROM sys.foreign_keys AS fk
            JOIN sys.tables AS tp ON fk.parent_object_id = tp.object_id
            JOIN sys.tables AS tr ON fk.referenced_object_id = tr.object_id
            JOIN sys.schemas AS s ON tp.schema_id = s.schema_id
            WHERE s.name = N'{SchemaName}';
            """;

        return await RunWithDeadlockRetryAsync(async () =>
        {
            var tables = new HashSet<string>(StringComparer.Ordinal);
            var edges = new List<(string ReferencingTable, string ReferencedTable)>();

            await using (var command = DataSource.CreateCommand(sql))
            {
                await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var table = reader.GetString(0);
                    tables.Add(table);

                    if (!await reader.IsDBNullAsync(1).ConfigureAwait(false))
                    {
                        edges.Add((table, reader.GetString(1)));
                    }
                }
            }

            var deletionOrder = DeletionOrder(tables, edges);

            if (deletionOrder.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();

            foreach (var table in deletionOrder)
            {
                builder.Append("DELETE FROM ").Append(SchemaName).Append('.').Append(table).Append(";\n");
            }

            return builder.ToString();
        }).ConfigureAwait(false);
    }

    /// <summary>Katalog okumasi bir deadlock'a carparsa yapilacak toplam deneme sayisi.</summary>
    private const int CatalogDeadlockRetryAttempts = 5;

    /// <summary>
    /// Bir katalog okumasini/DDL'ini <c>sys.foreign_keys</c>/<c>sys.tables</c>
    /// uzerindeki metadata kilidi cakismasina (hata 1205) karsi yeniden dener.
    /// </summary>
    /// <typeparam name="T">Islemin dondurdugu deger.</typeparam>
    /// <param name="action">Denenecek islem.</param>
    /// <returns>Islemin sonucu.</returns>
    private static async ValueTask<T> RunWithDeadlockRetryAsync<T>(Func<ValueTask<T>> action)
    {
        for (var attempt = 1; attempt <= CatalogDeadlockRetryAttempts; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (SqlException exception) when (
                exception.Number == DeadlockVictimErrorNumber && attempt < CatalogDeadlockRetryAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt)).ConfigureAwait(false);
            }
        }

        throw new UnreachableException();
    }

    /// <summary>
    /// FK grafiginin topolojik sirasini hesaplar (Kahn) ve tersine cevirir: referans
    /// eden (cocuk) tablolar, referans edilenden (ebeveyn) once silinir.
    /// </summary>
    /// <param name="tables">Semadaki tum veri tablolari.</param>
    /// <param name="edges">(referans eden, referans edilen) FK kenarlari.</param>
    /// <returns>Silme sirasi.</returns>
    private static List<string> DeletionOrder(
        HashSet<string> tables,
        IReadOnlyList<(string ReferencingTable, string ReferencedTable)> edges)
    {
        var dependsOnCount = tables.ToDictionary(t => t, _ => 0, StringComparer.Ordinal);
        var referencedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seenEdges = new HashSet<(string, string)>();

        foreach (var (referencing, referenced) in edges)
        {
            if (string.Equals(referencing, referenced, StringComparison.Ordinal) || !seenEdges.Add((referencing, referenced)))
            {
                continue;
            }

            dependsOnCount[referencing]++;

            if (!referencedBy.TryGetValue(referenced, out var children))
            {
                children = [];
                referencedBy[referenced] = children;
            }

            children.Add(referencing);
        }

        var queue = new Queue<string>(
            tables.Where(t => dependsOnCount[t] == 0).OrderBy(t => t, StringComparer.Ordinal));
        var creationOrder = new List<string>(tables.Count);

        while (queue.Count > 0)
        {
            var table = queue.Dequeue();
            creationOrder.Add(table);

            if (!referencedBy.TryGetValue(table, out var children))
            {
                continue;
            }

            foreach (var child in children.OrderBy(c => c, StringComparer.Ordinal))
            {
                if (--dependsOnCount[child] == 0)
                {
                    queue.Enqueue(child);
                }
            }
        }

        creationOrder.Reverse();

        return creationOrder;
    }

    /// <summary>SQL Server'in "deadlock victim" hatasinin numarasi.</summary>
    private const int DeadlockVictimErrorNumber = 1205;

    /// <summary>
    /// Sema icindeki tum tablolari (once FOREIGN KEY kisitlarini kaldirarak) ve
    /// ardindan semanin kendisini birakir. Migration hic uygulanmadiysa (sema
    /// yoksa) sessizce hicbir sey yapmaz.
    /// </summary>
    /// <remarks>
    /// 🚨 Bu DDL, <c>sys.foreign_keys</c>/<c>sys.tables</c> katalog gorunumlerini
    /// okur — bunlar TUM veritabaninin paylastigi kaynaklardir. Sinif fixture'lari
    /// paralel yok edildiginde baska bir sinifin ayni anda calisan sema
    /// temizligiyle metadata kilidi cakismasi (deadlock) yasanabilir; bu gecicidir
    /// ve <see cref="RunWithDeadlockRetryAsync{T}"/> ile yeniden denenir.
    /// </remarks>
    private async ValueTask DropSchemaAsync()
    {
        // SchemaName, SqlServerDialect kurulurken SqlIdentifier.RequireSchemaName
        // ile dogrulanmistir (yalniz kucuk harf/rakam/alt cizgi); dogrudan SQL
        // metnine yerlestirmek bu yuzden guvenlidir (bkz. SqlServerQueries.CreateSchema).
        var sql = $"""
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{SchemaName}')
            BEGIN
                DECLARE @sql NVARCHAR(MAX) = N'';

                SELECT @sql += N'ALTER TABLE {SchemaName}.' + QUOTENAME(t.name)
                    + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
                FROM sys.foreign_keys AS fk
                JOIN sys.tables AS t ON fk.parent_object_id = t.object_id
                JOIN sys.schemas AS s ON t.schema_id = s.schema_id
                WHERE s.name = N'{SchemaName}';

                EXEC sp_executesql @sql;
                SET @sql = N'';

                SELECT @sql += N'DROP TABLE {SchemaName}.' + QUOTENAME(t.name) + N';'
                FROM sys.tables AS t
                JOIN sys.schemas AS s ON t.schema_id = s.schema_id
                WHERE s.name = N'{SchemaName}';

                EXEC sp_executesql @sql;

                EXEC(N'DROP SCHEMA {SchemaName};');
            END
            """;

        await RunWithDeadlockRetryAsync(async () =>
        {
            await ExecuteAsync(sql).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }
}
