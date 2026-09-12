using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Tracon.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// Sets up an isolated schema and prepares the stores.
/// </summary>
/// <remarks>
/// A schema is usually shared by one contract test CLASS (see
/// <see cref="PostgresSchemaFixture"/>); isolation between tests is provided
/// by <see cref="ResetDataAsync"/>, not by a separate schema. That
/// <c>SchemaName</c> works correctly with a non-default schema is verified
/// separately in <c>MigrationRunnerTests</c>.
/// </remarks>
internal sealed class PostgresTestContext : IAsyncDisposable
{
    // Every contract test CLASS creates its own schema and then applies the whole
    // migration set, and xunit runs classes in parallel — so those transactions used to
    // reach the server together. Measured on SQL Server: '0017_approval_conditions'
    // deadlocked, the server picked one class fixture as the victim, and every test in
    // that class failed with an error naming a migration rather than the race. Schema
    // creation is setup, not the thing under test, so it is serialized per test
    // assembly. The deliberate concurrency tests call ApplyAsync directly and still run
    // in parallel.
    private static readonly SemaphoreSlim SchemaCreationLock = new(1, 1);

    /// <summary>
    /// Default embedding dimension used in tests (Phase 51). Kept small:
    /// the 40+ packages outside the vector tests are INDEPENDENT of this
    /// value, it only determines the type of the
    /// <c>document_embeddings.embedding</c> column.
    /// </summary>
    public const int DefaultVectorDimensions = 3;

    private PostgresTestContext(
        NpgsqlDataSource dataSource,
        TraconPostgreSqlOptions options,
        ITenantContext tenantContext,
        int vectorDimensions,
        bool enableKnowledge,
        bool enableReadViews)
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
            // Phase 67: the "knowledge" set is opt-in in production (K1), but
            // the shared test schema fixtures pre-date that flag and most
            // tests were written when the vector migration was unconditional
            // — default TRUE here keeps ~40 unrelated contract test classes
            // unchanged. Tests that specifically exercise phase 67's K1
            // default pass enableKnowledge: false explicitly.
            // Phase 111: the "views" set is opt-in in production for the same
            // reason as "knowledge" (K1) -- unlike knowledge, it defaults to
            // FALSE here too, since only ReadViewContractTests needs it and
            // the other ~40 contract test classes should not pay for it.
            EnabledMigrationSets = BuildEnabledMigrationSets(enableKnowledge, enableReadViews),
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
        AuditLog = new SqlAuditLog(wrapped, TenantContext);
        SkillScriptGrants = new SqlSkillScriptGrantStore(wrapped);
        AgentSkills = new SqlAgentSkillStore(wrapped);
        Attachments = new SqlAttachmentStore(wrapped);
        AgentFiles = new SqlAgentFileStore(wrapped, TenantContext);
        Vectors = new PgVectorSearchStore(
            dataSource,
            options,
            new TraconKnowledgeOptions { Dimensions = vectorDimensions });
        Workflows = new SqlWorkflowDefinitionStore(wrapped);
        WorkflowCheckpoints = new SqlWorkflowCheckpointStore(wrapped);
        Jobs = new SqlJobStore(wrapped);
        JobSchedules = new SqlJobScheduleStore(wrapped);
        Evals = new SqlEvalStore(wrapped);
        Experiments = new SqlExperimentStore(wrapped, TenantContext);
        Quotas = new SqlQuotaStore(wrapped);
        Webhooks = new SqlWebhookStore(wrapped);
        ApiKeys = new SqlApiKeyStore(wrapped);
        TenantProviderBindings = new SqlTenantProviderBindingStore(wrapped);
        TenantEgressPolicies = new SqlTenantEgressPolicyStore(wrapped);
        InboundTriggers = new SqlInboundTriggerStore(wrapped);
        RetentionPolicies = new SqlRetentionPolicyStore(wrapped);
        RetentionData = new SqlRetentionStore(wrapped);
        VoiceSessions = new SqlVoiceSessionStore(wrapped);
        RunScores = new SqlRunScoreStore(wrapped, TenantContext);
        SingletonLeases = new SqlSingletonLeaseStore(wrapped);
        IdempotencyKeys = new SqlIdempotencyStore(wrapped);
        RunInputs = new SqlRunInputStore(wrapped);
        ConversationBranches = new SqlConversationBranchStore(wrapped);
        PendingApprovals = new SqlPendingApprovalStore(wrapped, TenantContext);
        DataSubjects = new SqlDataSubjectStore(wrapped);
        Migrations = new MigrationRunner(wrapped, NullLogger<MigrationRunner>.Instance);
    }

    /// <summary>This context's data source.</summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>This context's shared store-layer context.</summary>
    public SqlStoreContext StoreContext { get; }

    /// <summary>This context's options.</summary>
    public TraconPostgreSqlOptions Options { get; }

    /// <summary>This context's tenant context.</summary>
    public ITenantContext TenantContext { get; }

    /// <summary>The embedding dimension applied to migration 0024 (Phase 51).</summary>
    public int VectorDimensions { get; }

    /// <summary>Agent definition store.</summary>
    public SqlAgentDefinitionStore AgentDefinitions { get; }

    /// <summary>Run store.</summary>
    public SqlRunStore Runs { get; }

    /// <summary>Session store.</summary>
    public SqlSessionStore Sessions { get; }

    /// <summary>Span store (Phase 6).</summary>
    public SqlTraceStore Traces { get; }

    /// <summary>Persistent tool approval rule store (Phase 6).</summary>
    public SqlToolApprovalRuleStore ApprovalRules { get; }

    /// <summary>Pending approval request store (Phase 55).</summary>
    public SqlPendingApprovalStore PendingApprovals { get; }

    /// <summary>MCP server store (Phase 6).</summary>
    public SqlMcpServerStore McpServers { get; }

    /// <summary>Tenant registry store (Phase 6).</summary>
    public SqlTenantStore Tenants { get; }

    /// <summary>Chat history provider.</summary>
    public SqlChatHistoryProvider ChatHistory { get; }

    /// <summary>Audit trail ledger (Phase 9).</summary>
    public SqlAuditLog AuditLog { get; }

    /// <summary>Script execution grant store (Phase 11).</summary>
    public SqlSkillScriptGrantStore SkillScriptGrants { get; }

    /// <summary>Runtime skill store (Phase 10).</summary>
    public SqlAgentSkillStore AgentSkills { get; }

    /// <summary>Attachment store (Phase 14).</summary>
    public SqlAttachmentStore Attachments { get; }

    /// <summary>Persistent agent file storage (Phase 14).</summary>
    public SqlAgentFileStore AgentFiles { get; }

    /// <summary>Vector-based semantic search store (Phase 51).</summary>
    public PgVectorSearchStore Vectors { get; }

    /// <summary>Workflow definition store (Phase 15).</summary>
    public SqlWorkflowDefinitionStore Workflows { get; }

    /// <summary>Workflow checkpoint store (Phase 15).</summary>
    public SqlWorkflowCheckpointStore WorkflowCheckpoints { get; }

    /// <summary>Job queue store (Phase 17).</summary>
    public SqlJobStore Jobs { get; }

    /// <summary>Schedule store (Phase 17).</summary>
    public SqlJobScheduleStore JobSchedules { get; }

    /// <summary>Eval team/case/run store (Phase 18).</summary>
    public SqlEvalStore Evals { get; }

    /// <summary>A/B experiment store (Phase 19).</summary>
    public SqlExperimentStore Experiments { get; }

    /// <summary>Quota store (Phase 21).</summary>
    public SqlQuotaStore Quotas { get; }

    /// <summary>Webhook store (Phase 21).</summary>
    public SqlWebhookStore Webhooks { get; }

    /// <summary>Tenant-scoped API key store (Phase 53).</summary>
    public SqlApiKeyStore ApiKeys { get; }

    /// <summary>Tenant provider binding store (Phase 65, BYOK).</summary>
    public SqlTenantProviderBindingStore TenantProviderBindings { get; }

    /// <summary>Tenant egress policy store (Phase 65, F-119).</summary>
    public SqlTenantEgressPolicyStore TenantEgressPolicies { get; }

    /// <summary>Inbound trigger definition store (Phase 66).</summary>
    public SqlInboundTriggerStore InboundTriggers { get; }

    /// <summary>Retention policy and run history store (Phase 25).</summary>
    public SqlRetentionPolicyStore RetentionPolicies { get; }

    /// <summary>Retention data plane (count/delete/archive read) (Phase 25).</summary>
    public SqlRetentionStore RetentionData { get; }

    /// <summary>Voice session record store (Phase 29).</summary>
    public SqlVoiceSessionStore VoiceSessions { get; }

    /// <summary>Run/message score store (Phase 31).</summary>
    public SqlRunScoreStore RunScores { get; }

    /// <summary>Single-executor election lease store (Phase 42).</summary>
    public SqlSingletonLeaseStore SingletonLeases { get; }

    /// <summary>Idempotency store (Phase 43).</summary>
    public SqlIdempotencyStore IdempotencyKeys { get; }

    /// <summary>Run input store (Phase 47).</summary>
    public SqlRunInputStore RunInputs { get; }

    /// <summary>Conversation branching store (Phase 47).</summary>
    public SqlConversationBranchStore ConversationBranches { get; }

    /// <summary>Data subject export/erasure data plane (Phase 64).</summary>
    public SqlDataSubjectStore DataSubjects { get; }

    /// <summary>Migration runner.</summary>
    public MigrationRunner Migrations { get; }

    /// <summary>The schema name in use.</summary>
    public string SchemaName => Options.SchemaName;

    /// <summary>
    /// Sets up a new isolated schema, applies migrations, and prepares the stores.
    /// </summary>
    /// <param name="fixture">The running PostgreSQL container.</param>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="applyMigrations">Whether to apply migrations immediately.</param>
    /// <returns>A ready-to-use context.</returns>
    public static ValueTask<PostgresTestContext> CreateAsync(
        PostgresFixture fixture,
        string tenantId = "default",
        bool applyMigrations = true,
        int vectorDimensions = DefaultVectorDimensions,
        bool enableKnowledge = true,
        bool enableReadViews = false)
        => CreateAsync(fixture, new FixedTenantContext(tenantId), applyMigrations, vectorDimensions, enableKnowledge, enableReadViews);

    /// <summary>
    /// Setup with the tenant context supplied externally. The tenant
    /// isolation contract uses this overload because it switches tenants on
    /// the same store instance (Phase 41).
    /// </summary>
    /// <param name="fixture">The running PostgreSQL container.</param>
    /// <param name="tenantContext">The tenant context the stores will read.</param>
    /// <param name="applyMigrations">Whether to apply migrations immediately.</param>
    /// <returns>A ready-to-use context.</returns>
    public static async ValueTask<PostgresTestContext> CreateAsync(
        PostgresFixture fixture,
        ITenantContext tenantContext,
        bool applyMigrations = true,
        int vectorDimensions = DefaultVectorDimensions,
        bool enableKnowledge = true,
        bool enableReadViews = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var context = Create(fixture, NewSchemaName(), tenantContext, vectorDimensions, enableKnowledge, enableReadViews);

        if (applyMigrations)
        {
            await SchemaCreationLock.WaitAsync();

            try
            {
                await context.Migrations.ApplyAsync();
            }
            finally
            {
                SchemaCreationLock.Release();
            }
        }

        return context;
    }

    /// <summary>
    /// Sets up a second context connected to an existing schema.
    /// Used for concurrency and tenant isolation tests.
    /// </summary>
    /// <param name="fixture">The running PostgreSQL container.</param>
    /// <param name="schemaName">The schema name to use.</param>
    /// <param name="tenantId">The tenant ID.</param>
    /// <returns>A new context pointing at the same schema.</returns>
    public static PostgresTestContext Create(
        PostgresFixture fixture,
        string schemaName,
        string tenantId = "default",
        int vectorDimensions = DefaultVectorDimensions,
        bool enableKnowledge = true,
        bool enableReadViews = false)
        => Create(fixture, schemaName, new FixedTenantContext(tenantId), vectorDimensions, enableKnowledge, enableReadViews);

    /// <summary>Setup with the tenant context supplied externally.</summary>
    /// <param name="fixture">The running PostgreSQL container.</param>
    /// <param name="schemaName">The schema name to use.</param>
    /// <param name="tenantContext">The tenant context the stores will read.</param>
    /// <param name="vectorDimensions">The embedding dimension to apply to the knowledge set's 0001_vector migration (Phase 51).</param>
    /// <param name="enableKnowledge">Whether the "knowledge" migration set applies (Phase 67). Default <see langword="true"/> to keep existing contract tests unchanged.</param>
    /// <param name="enableReadViews">Whether the "views" migration set applies (Phase 111). Default <see langword="false"/>: only <c>ReadViewContractTests</c> needs it.</param>
    /// <returns>A new context pointing at the same backend.</returns>
    public static PostgresTestContext Create(
        PostgresFixture fixture,
        string schemaName,
        ITenantContext tenantContext,
        int vectorDimensions = DefaultVectorDimensions,
        bool enableKnowledge = true,
        bool enableReadViews = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        return Create(fixture.ConnectionString, schemaName, tenantContext, vectorDimensions, enableKnowledge, enableReadViews);
    }

    /// <summary>Setup against a server this assembly's shared fixture does not own.</summary>
    /// <param name="connectionString">The server to connect to.</param>
    /// <param name="schemaName">The schema name to use.</param>
    /// <param name="tenantContext">The tenant context the stores will read; the default tenant when omitted.</param>
    /// <param name="vectorDimensions">The embedding dimension the knowledge set's 0001_vector migration applies.</param>
    /// <param name="enableKnowledge">Whether the "knowledge" migration set applies.</param>
    /// <param name="enableReadViews">Whether the "views" migration set applies.</param>
    /// <returns>A new context pointing at that server.</returns>
    /// <remarks>
    /// Phase 157: the failure manifests start a container of their OWN and stop
    /// it on purpose, so they cannot go through <see cref="PostgresFixture"/> -
    /// stopping the shared container would fail every other class in the
    /// assembly for a reason that has nothing to do with them.
    /// </remarks>
    public static PostgresTestContext Create(
        string connectionString,
        string schemaName,
        ITenantContext? tenantContext = null,
        int vectorDimensions = DefaultVectorDimensions,
        bool enableKnowledge = true,
        bool enableReadViews = false)
    {
        var options = new TraconPostgreSqlOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            AutoApplyMigrations = false,
            CommandTimeoutSeconds = 30,
            EnableKnowledge = enableKnowledge,
            EnableReadViews = enableReadViews,
        };

        var dataSource = new NpgsqlDataSourceBuilder(options.ConnectionString).Build();

        return new PostgresTestContext(
            dataSource,
            options,
            tenantContext ?? new FixedTenantContext("default"),
            vectorDimensions,
            enableKnowledge,
            enableReadViews);
    }

    /// <summary>Collects the optional migration sets the given flags turn on.</summary>
    private static HashSet<string> BuildEnabledMigrationSets(bool enableKnowledge, bool enableReadViews)
    {
        var sets = new HashSet<string>(StringComparer.Ordinal);

        if (enableKnowledge)
        {
            sets.Add("knowledge");
        }

        if (enableReadViews)
        {
            sets.Add("views");
        }

        return sets;
    }

    /// <summary>Generates a new, unique test schema name.</summary>
    /// <returns>A valid identifier made of lowercase letters.</returns>
    public static string NewSchemaName()
        => "t_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16];

    /// <summary>Runs raw SQL against the context.</summary>
    /// <param name="sql">The SQL to run.</param>
    /// <returns>The number of affected rows.</returns>
    public async ValueTask<int> ExecuteAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs raw SQL that returns a single value.</summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="sql">The SQL to run.</param>
    /// <returns>The first column of the first row.</returns>
    public async ValueTask<T?> ScalarAsync<T>(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        var result = await command.ExecuteScalarAsync();

        return result is T value ? value : default;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await DataSource.DisposeAsync();

    /// <summary>Cached data-reset SQL text. Computed once per class.</summary>
    private string? _resetSql;

    /// <summary>
    /// Empties all data tables in the schema in a single round trip; the
    /// schema and the <c>__migrations</c> ledger REMAIN.
    /// </summary>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// ALL tables in the schema are given together in a single
    /// <c>TRUNCATE</c> statement; PostgreSQL resolves cross-table FOREIGN
    /// KEYs by itself in such a statement without needing <c>CASCADE</c>.
    /// The table list is read from the catalog, not hardcoded.
    /// </remarks>
    public async ValueTask ResetDataAsync()
    {
        _resetSql ??= await BuildResetSqlAsync().ConfigureAwait(false);

        if (_resetSql.Length > 0)
        {
            await ExecuteAsync(_resetSql).ConfigureAwait(false);
        }
    }

    private async ValueTask<string> BuildResetSqlAsync()
    {
        var sql = $"""
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = '{SchemaName}' AND table_name <> '__migrations';
            """;

        var tables = new List<string>();

        await using (var command = DataSource.CreateCommand(sql))
        {
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (tables.Count == 0)
        {
            return string.Empty;
        }

        var qualified = tables.Select(table => $"{SchemaName}.{table}");

        return $"TRUNCATE TABLE {string.Join(", ", qualified)};";
    }
}
