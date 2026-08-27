using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.SqlServer.IntegrationTests.Infrastructure;

/// <summary>
/// Sets up an isolated schema and prepares the stores.
/// </summary>
/// <remarks>
/// A schema is usually shared by one contract test CLASS (see
/// <see cref="SqlServerSchemaFixture"/>); isolation between tests is
/// provided by <see cref="ResetDataAsync"/>, not by a separate schema. That
/// <c>SchemaName</c> works correctly with a non-default schema is verified
/// separately in <c>MigrationRunnerTests</c>.
/// </remarks>
internal sealed class SqlServerTestContext : IAsyncDisposable
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
            // Phase 111: the "views" set is opt-in in production (K1); default
            // FALSE here too -- only ReadViewContractTests needs it.
            EnabledMigrationSets = options.EnableReadViews
                ? new HashSet<string>(StringComparer.Ordinal) { "views" }
                : System.Collections.Immutable.ImmutableHashSet<string>.Empty,
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
        AuditLog = new SqlAuditLog(wrapped, TenantContext);
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
    public SqlServerDataSource DataSource { get; }

    /// <summary>This context's shared store-layer context.</summary>
    public SqlStoreContext StoreContext { get; }

    /// <summary>This context's options.</summary>
    public AgentPrismSqlServerOptions Options { get; }

    /// <summary>This context's tenant context.</summary>
    public ITenantContext TenantContext { get; }

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

    /// <summary>Migration runner.</summary>
    public MigrationRunner Migrations { get; }

    /// <summary>Data subject export/erasure data plane (Phase 64).</summary>
    public SqlDataSubjectStore DataSubjects { get; }

    /// <summary>The schema name in use.</summary>
    public string SchemaName => Options.SchemaName;

    /// <summary>
    /// Sets up a new isolated schema, applies migrations, and prepares the stores.
    /// </summary>
    /// <param name="fixture">The running SQL Server container.</param>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="applyMigrations">Whether to apply migrations immediately.</param>
    /// <returns>A ready-to-use context.</returns>
    public static ValueTask<SqlServerTestContext> CreateAsync(
        SqlServerFixture fixture,
        string tenantId = "default",
        bool applyMigrations = true,
        bool enableReadViews = false)
        => CreateAsync(fixture, new FixedTenantContext(tenantId), applyMigrations, enableReadViews);

    /// <summary>
    /// Setup with the tenant context supplied externally. The tenant
    /// isolation contract uses this overload because it switches tenants on
    /// the same store instance (Phase 41).
    /// </summary>
    /// <param name="fixture">The running SQL Server container.</param>
    /// <param name="tenantContext">The tenant context the stores will read.</param>
    /// <param name="applyMigrations">Whether to apply migrations immediately.</param>
    /// <param name="enableReadViews">Whether the "views" migration set applies (Phase 111). Default <see langword="false"/>: only <c>ReadViewContractTests</c> needs it.</param>
    /// <returns>A ready-to-use context.</returns>
    public static async ValueTask<SqlServerTestContext> CreateAsync(
        SqlServerFixture fixture,
        ITenantContext tenantContext,
        bool applyMigrations = true,
        bool enableReadViews = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var context = Create(fixture, NewSchemaName(), tenantContext, enableReadViews);

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
    /// <param name="fixture">The running SQL Server container.</param>
    /// <param name="schemaName">The schema name to use.</param>
    /// <param name="tenantId">The tenant ID.</param>
    /// <returns>A new context pointing at the same schema.</returns>
    public static SqlServerTestContext Create(
        SqlServerFixture fixture,
        string schemaName,
        string tenantId = "default",
        bool enableReadViews = false)
        => Create(fixture, schemaName, new FixedTenantContext(tenantId), enableReadViews);

    /// <summary>Setup with the tenant context supplied externally.</summary>
    /// <param name="fixture">The running SQL Server container.</param>
    /// <param name="schemaName">The schema name to use.</param>
    /// <param name="tenantContext">The tenant context the stores will read.</param>
    /// <param name="enableReadViews">Whether the "views" migration set applies (Phase 111). Default <see langword="false"/>: only <c>ReadViewContractTests</c> needs it.</param>
    /// <returns>A new context pointing at the same backend.</returns>
    public static SqlServerTestContext Create(
        SqlServerFixture fixture,
        string schemaName,
        ITenantContext tenantContext,
        bool enableReadViews = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var options = new AgentPrismSqlServerOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaName = schemaName,
            AutoApplyMigrations = false,
            CommandTimeoutSeconds = 30,
            EnableReadViews = enableReadViews,
        };

        var dataSource = new SqlServerDataSource(options.ConnectionString!);

        return new SqlServerTestContext(dataSource, options, tenantContext);
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

    /// <summary>Drops the test schema and everything in it.</summary>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// PostgreSQL tests do not drop the schema; the container is destroyed
    /// anyway. On SQL Server every test drops its own schema, because if
    /// hundreds of schemas accumulate in a single database, the system views
    /// (migration conditions running over sys.indexes) become noticeably slower.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await DropSchemaAsync().ConfigureAwait(false);
        await DataSource.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Cached batch data-reset SQL text. Computed once per class.</summary>
    private string? _resetBatchSql;

    /// <summary>
    /// Empties all data tables in the schema in a single round trip; the
    /// schema and the <c>__migrations</c> ledger REMAIN.
    /// </summary>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// The deletion order is a topological ordering computed from
    /// <c>sys.foreign_keys</c> (a referencing table is deleted before the
    /// one it references); the table list is also read from the catalog,
    /// not hardcoded. This way, if a new migration adds a table, this method
    /// stays correct without a manual update.
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
    /// Computes the deletion order from the catalog and produces a single batched <c>DELETE</c> text.
    /// </summary>
    /// <returns>The SQL text to run; an empty string if the schema is empty.</returns>
    /// <remarks>
    /// 🚨 This read scans the <c>sys.foreign_keys</c>/<c>sys.tables</c>
    /// catalog views — the same shared resource as
    /// <see cref="DropSchemaAsync"/>. Once per class, and because all class
    /// fixtures run concurrently AT STARTUP (see the schema fixtures), the
    /// same deadlock risk exists here too; it is protected by the same retry.
    /// </remarks>
    private async ValueTask<string> BuildResetBatchSqlAsync()
    {
        // SchemaName was validated with SqlIdentifier.RequireSchemaName when
        // SqlServerDialect was constructed; embedding it directly into the
        // SQL text is safe for the same reason as DropSchemaAsync.
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

    /// <summary>Total number of attempts made if a catalog read hits a deadlock.</summary>
    private const int CatalogDeadlockRetryAttempts = 5;

    /// <summary>
    /// Retries a catalog read/DDL against a metadata lock conflict (error
    /// 1205) on <c>sys.foreign_keys</c>/<c>sys.tables</c>.
    /// </summary>
    /// <typeparam name="T">The value the operation returns.</typeparam>
    /// <param name="action">The operation to retry.</param>
    /// <returns>The operation's result.</returns>
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
    /// Computes the topological order of the FK graph (Kahn) and reverses
    /// it: referencing (child) tables are deleted before the ones they
    /// reference (parents).
    /// </summary>
    /// <param name="tables">All data tables in the schema.</param>
    /// <param name="edges">(referencing, referenced) FK edges.</param>
    /// <returns>The deletion order.</returns>
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

    /// <summary>SQL Server's "deadlock victim" error number.</summary>
    private const int DeadlockVictimErrorNumber = 1205;

    /// <summary>
    /// Drops all tables in the schema (first removing FOREIGN KEY
    /// constraints) and then the schema itself. Does nothing silently if the
    /// migration was never applied (schema does not exist).
    /// </summary>
    /// <remarks>
    /// 🚨 This DDL reads the <c>sys.foreign_keys</c>/<c>sys.tables</c>
    /// catalog views — resources shared by the ENTIRE database. When class
    /// fixtures are torn down in parallel, a metadata lock conflict
    /// (deadlock) with another class's concurrently running schema cleanup
    /// can occur; this is transient and retried with
    /// <see cref="RunWithDeadlockRetryAsync{T}"/>.
    /// </remarks>
    private async ValueTask DropSchemaAsync()
    {
        // SchemaName was validated with SqlIdentifier.RequireSchemaName when
        // SqlServerDialect was constructed (lowercase letters/digits/
        // underscore only); embedding it directly into the SQL text is
        // therefore safe (see SqlServerQueries.CreateSchema).
        var sql = $"""
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{SchemaName}')
            BEGIN
                DECLARE @sql NVARCHAR(MAX) = N'';

                -- Phase 111: runs_v1 (the "views" optional set). A view is
                -- dropped BEFORE the table it reads from, and before the
                -- schema drop below -- SQL Server refuses to drop a schema
                -- that still contains any object, view included.
                SELECT @sql += N'DROP VIEW {SchemaName}.' + QUOTENAME(v.name) + N';'
                FROM sys.views AS v
                JOIN sys.schemas AS s ON v.schema_id = s.schema_id
                WHERE s.name = N'{SchemaName}';

                EXEC sp_executesql @sql;
                SET @sql = N'';

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
                SET @sql = N'';

                -- Phase 64: audit_chain_seq. A schema cannot be dropped while
                -- a sequence still lives in it, the same reason tables are
                -- dropped above first.
                SELECT @sql += N'DROP SEQUENCE {SchemaName}.' + QUOTENAME(seq.name) + N';'
                FROM sys.sequences AS seq
                JOIN sys.schemas AS s ON seq.schema_id = s.schema_id
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
