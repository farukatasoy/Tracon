using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// Sets up an isolated table prefix and prepares the stores.
/// </summary>
/// <remarks>
/// A table prefix is usually shared by a contract test CLASS (see
/// <see cref="SqliteSchemaFixture"/>); isolation between tests is provided by
/// <see cref="ResetDataAsync"/>, not by a separate prefix per test.
/// <c>MigrationRunnerTests</c> separately verifies that the <c>TablePrefix</c> setting works
/// correctly with a non-default prefix.
/// </remarks>
internal sealed class SqliteTestContext : IAsyncDisposable
{
    private SqliteTestContext(
        SqliteDataSource dataSource,
        AgentPrismSqliteOptions options,
        ITenantContext tenantContext)
    {
        DataSource = dataSource;
        Options = options;
        TenantContext = tenantContext;

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
        TenantProviderBindings = new SqlTenantProviderBindingStore(wrapped);
        TenantEgressPolicies = new SqlTenantEgressPolicyStore(wrapped);
        InboundTriggers = new SqlInboundTriggerStore(wrapped);
        RetentionPolicies = new SqlRetentionPolicyStore(wrapped);
        RetentionData = new SqlRetentionStore(wrapped);
        VoiceSessions = new SqlVoiceSessionStore(wrapped);
        RunScores = new SqlRunScoreStore(wrapped);
        SingletonLeases = new SqlSingletonLeaseStore(wrapped);
        IdempotencyKeys = new SqlIdempotencyStore(wrapped);
        RunInputs = new SqlRunInputStore(wrapped);
        ConversationBranches = new SqlConversationBranchStore(wrapped);
        PendingApprovals = new SqlPendingApprovalStore(wrapped, TenantContext);
        DataSubjects = new SqlDataSubjectStore(wrapped);
        Migrations = new MigrationRunner(wrapped, NullLogger<MigrationRunner>.Instance);
    }

    /// <summary>This context's data source.</summary>
    public SqliteDataSource DataSource { get; }

    /// <summary>The shared store layer's context.</summary>
    public SqlStoreContext StoreContext { get; }

    /// <summary>This context's settings.</summary>
    public AgentPrismSqliteOptions Options { get; }

    /// <summary>This context's tenant context.</summary>
    public ITenantContext TenantContext { get; }

    /// <summary>Agent definition store.</summary>
    public SqlAgentDefinitionStore AgentDefinitions { get; }

    /// <summary>Run store.</summary>
    public SqlRunStore Runs { get; }

    /// <summary>Session store.</summary>
    public SqlSessionStore Sessions { get; }

    /// <summary>Span store.</summary>
    public SqlTraceStore Traces { get; }

    /// <summary>Persistent approval rule store.</summary>
    public SqlToolApprovalRuleStore ApprovalRules { get; }

    /// <summary>Pending approval request store (Phase 55).</summary>
    public SqlPendingApprovalStore PendingApprovals { get; }

    /// <summary>MCP server store.</summary>
    public SqlMcpServerStore McpServers { get; }

    /// <summary>Tenant registry store.</summary>
    public SqlTenantStore Tenants { get; }

    /// <summary>Chat history provider.</summary>
    public SqlChatHistoryProvider ChatHistory { get; }

    /// <summary>Audit log ledger.</summary>
    public SqlAuditLog AuditLog { get; }

    /// <summary>Script execution grant store.</summary>
    public SqlSkillScriptGrantStore SkillScriptGrants { get; }

    /// <summary>Runtime skill store (Phase 10).</summary>
    public SqlAgentSkillStore AgentSkills { get; }

    /// <summary>Attachment store.</summary>
    public SqlAttachmentStore Attachments { get; }

    /// <summary>Persistent agent file storage.</summary>
    public SqlAgentFileStore AgentFiles { get; }

    /// <summary>Workflow definition store.</summary>
    public SqlWorkflowDefinitionStore Workflows { get; }

    /// <summary>Workflow checkpoint store.</summary>
    public SqlWorkflowCheckpointStore WorkflowCheckpoints { get; }

    /// <summary>Job queue store.</summary>
    public SqlJobStore Jobs { get; }

    /// <summary>Schedule store.</summary>
    public SqlJobScheduleStore JobSchedules { get; }

    /// <summary>Eval suite/case/run store.</summary>
    public SqlEvalStore Evals { get; }

    /// <summary>A/B experiment store.</summary>
    public SqlExperimentStore Experiments { get; }

    /// <summary>Quota store.</summary>
    public SqlQuotaStore Quotas { get; }

    /// <summary>Webhook store.</summary>
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

    /// <summary>Voice session store (Phase 29).</summary>
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

    /// <summary>Migration runner.</summary>
    public MigrationRunner Migrations { get; }

    /// <summary>Data subject export/erasure data plane (Phase 64).</summary>
    public SqlDataSubjectStore DataSubjects { get; }

    /// <summary>The table prefix in use.</summary>
    public string TablePrefix => Options.TablePrefix;

    /// <summary>
    /// Sets up a new isolated table prefix, applies migrations, and prepares the stores.
    /// </summary>
    /// <param name="fixture">The running SQLite database file.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="applyMigrations">Whether migrations should be applied immediately.</param>
    /// <returns>A context ready for use.</returns>
    public static ValueTask<SqliteTestContext> CreateAsync(
        SqliteFixture fixture,
        string tenantId = "default",
        bool applyMigrations = true)
        => CreateAsync(fixture, new FixedTenantContext(tenantId), applyMigrations);

    /// <summary>
    /// Setup with an externally supplied tenant context. Used because the tenant isolation
    /// contract needs to switch tenants on the same store instance (Phase 41).
    /// </summary>
    /// <param name="fixture">The running SQLite file.</param>
    /// <param name="tenantContext">The tenant context the stores will read.</param>
    /// <param name="applyMigrations">Whether migrations should be applied immediately.</param>
    /// <returns>A context ready for use.</returns>
    public static async ValueTask<SqliteTestContext> CreateAsync(
        SqliteFixture fixture,
        ITenantContext tenantContext,
        bool applyMigrations = true)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var context = Create(fixture, NewTablePrefix(), tenantContext);

        if (applyMigrations)
        {
            await context.Migrations.ApplyAsync();
        }

        return context;
    }

    /// <summary>
    /// Sets up a second context connecting to an existing table prefix. Used for concurrency
    /// and tenant isolation tests.
    /// </summary>
    /// <param name="fixture">The running SQLite database file.</param>
    /// <param name="tablePrefix">The table prefix to use.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>A new context pointing at the same prefix.</returns>
    public static SqliteTestContext Create(SqliteFixture fixture, string tablePrefix, string tenantId = "default")
        => Create(fixture, tablePrefix, new FixedTenantContext(tenantId));

    /// <summary>Setup with an externally supplied tenant context.</summary>
    /// <param name="fixture">The running SQLite file.</param>
    /// <param name="tablePrefix">The table prefix to use.</param>
    /// <param name="tenantContext">The tenant context the stores will read.</param>
    /// <returns>A new context pointing at the same backend.</returns>
    public static SqliteTestContext Create(SqliteFixture fixture, string tablePrefix, ITenantContext tenantContext)
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

        return new SqliteTestContext(dataSource, options, tenantContext);
    }

    /// <summary>Generates a new, unique test table prefix.</summary>
    /// <returns>A valid identifier made of lowercase letters, ending with an underscore.</returns>
    public static string NewTablePrefix()
        => "t_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16] + "_";

    /// <summary>Executes raw SQL on this context.</summary>
    /// <param name="sql">The SQL to execute.</param>
    /// <returns>The number of affected rows.</returns>
    public async ValueTask<int> ExecuteAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Executes raw SQL that returns a single value.</summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="sql">The SQL to execute.</param>
    /// <returns>The first column of the first row.</returns>
    public async ValueTask<T?> ScalarAsync<T>(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        var result = await command.ExecuteScalarAsync();

        return result is T value ? value : default;
    }

    /// <summary>Drops the test tables.</summary>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// SQLite has no schema concept; each test prefix drops its own set of tables so that
    /// hundreds of test tables do not accumulate in a single file.
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

    /// <summary>
    /// Empties all data tables in the prefix within a single transaction; the tables and the
    /// <c>__migrations</c> ledger REMAIN.
    /// </summary>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// <c>PRAGMA defer_foreign_keys = ON</c> defers FOREIGN KEY checking to the end of the
    /// transaction, so all tables can be emptied in a single transaction regardless of delete
    /// order. The table list is read from the catalog (<see cref="ReadTableNamesAsync"/>), not
    /// hardcoded.
    /// </remarks>
    public async ValueTask ResetDataAsync()
    {
        var migrationsTable = $"{TablePrefix}__migrations";
        var tableNames = (await ReadTableNamesAsync().ConfigureAwait(false))
            .Where(name => !string.Equals(name, migrationsTable, StringComparison.Ordinal))
            .ToList();

        if (tableNames.Count == 0)
        {
            return;
        }

        await using var connection = await DataSource.OpenConnectionAsync().ConfigureAwait(false);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA defer_foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))
        {
            foreach (var tableName in tableNames)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DELETE FROM \"{tableName}\";";
                command.Transaction = transaction;
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
    }
}
