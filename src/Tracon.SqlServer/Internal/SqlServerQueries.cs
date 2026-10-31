namespace Tracon;

/// <summary>
/// Holds the SQL Server (T-SQL) text for the <see cref="SqlQueriesBase"/> surface.
/// </summary>
/// <remarks>
/// <para>
/// Query <em>names</em> and the column order they return are identical to
/// PostgreSQL; the shared store code cannot tell the two apart. The
/// differences live only inside the text.
/// </para>
/// <para>Translation rules applied:</para>
/// <list type="bullet">
/// <item><description>
/// <strong><c>MERGE</c> IS NOT USED.</strong> The statement has known
/// concurrency and correctness problems. Upserts are written as
/// <c>UPDATE ... WITH (UPDLOCK, SERIALIZABLE) ... OUTPUT</c> followed by
/// <c>IF @@ROWCOUNT = 0 INSERT ... OUTPUT</c>. The <c>SERIALIZABLE</c> hint
/// takes a range lock, so two sessions cannot insert the same key at the
/// same time - but only while the lock lives: the store runs the text inside
/// a transaction (<c>SqlStoreContext.ExecuteUpsertAsync</c>). In autocommit
/// the lock ends with the UPDATE and both sessions insert.
/// </description></item>
/// <item><description>
/// <strong>An upsert's WHERE compares each key column BARE.</strong> A wrapped
/// column (<c>ISNULL(col, N'') = ISNULL(@p, N'')</c>) cannot seek: the UPDATE
/// reads the neighbouring keys, the locks on the rows it rejects drop to
/// shared range locks, and two sessions that both hold one then wait for each
/// other's INSERT - a deadlock. A nullable key column uses
/// <c>EXISTS (SELECT col INTERSECT SELECT @p)</c>: NULL-safe equality that
/// still seeks. <c>IS NOT DISTINCT FROM</c> says the same thing but needs SQL
/// Server 2022; 2019 is supported.
/// </description></item>
/// <item><description>
/// <c>RETURNING</c> -> <c>OUTPUT inserted.*</c> / <c>OUTPUT deleted.*</c>.
/// Both branches of the upsert return <em>the same columns</em>, so a
/// single reader suffices on the C# side.
/// </description></item>
/// <item><description>
/// <c>COUNT(*) FILTER (WHERE p)</c> -> <c>COALESCE(SUM(CASE WHEN p THEN 1 ELSE 0 END), 0)</c>.
/// <c>COALESCE</c> is required: <c>SUM</c> returns <c>NULL</c> over an
/// empty set, whereas PostgreSQL's <c>COUNT</c> returned zero.
/// </description></item>
/// <item><description>
/// <c>LEAST</c> / <c>GREATEST</c> <strong>do not exist</strong> in SQL
/// Server 2019 (added in 2022) and are written with <c>CASE</c> instead.
/// </description></item>
/// <item><description>
/// <c>FOR UPDATE SKIP LOCKED</c> -> <c>WITH (UPDLOCK, READPAST, ROWLOCK)</c>.
/// </description></item>
/// <item><description>
/// <c>UNNEST</c> and <c>= ANY(array)</c> -> <c>OPENJSON</c>; arrays travel
/// as JSON text.
/// </description></item>
/// <item><description>
/// <c>OFFSET ... FETCH NEXT @take ROWS ONLY</c> <strong>raises an
/// error</strong> when <c>@take = 0</c>, whereas PostgreSQL's <c>LIMIT 0</c>
/// returned an empty list. To keep behavior equal, paged queries add an
/// <c>@take &gt; 0</c> condition to the WHERE clause and clamp the
/// <c>FETCH</c> value to at least one.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class SqlServerQueries : SqlQueriesBase
{
    /// <inheritdoc />
    /// <remarks>SQL Server has no <c>FILTER</c> clause; <c>COUNT</c> is emulated with a conditional <c>SUM</c>.</remarks>
    protected override string CountWhereAnyNotNull(IReadOnlyList<string> columns, string? alias)
    {
        var prefix = alias is null ? string.Empty : $"{alias}.";
        var condition = string.Join(" OR ", columns.Select(column => $"{prefix}{column} IS NOT NULL"));

        return $"COALESCE(SUM(CASE WHEN {condition} THEN 1 ELSE 0 END), 0)";
    }

    /// <summary>
    /// The skip/take clause appended to the end of paged queries.
    /// </summary>
    /// <remarks>
    /// The <c>@take = 0</c> case is eliminated on the WHERE side (see
    /// <see cref="TakeGuard"/>); the <c>CASE</c> here only stops <c>FETCH</c>
    /// from ever seeing zero.
    /// </remarks>
    private const string Paging = "OFFSET @skip ROWS FETCH NEXT (CASE WHEN @take < 1 THEN 1 ELSE @take END) ROWS ONLY;";

    /// <summary>The zero-guard condition appended to the WHERE clause of paged queries.</summary>
    private const string TakeGuard = "AND @take > 0";

    /// <summary>Creates a new query set.</summary>
    /// <param name="schemaName">The schema name to validate.</param>
    /// <exception cref="TraconException">The schema name is not a valid identifier.</exception>
    public SqlServerQueries(string schemaName)
        : base(schemaName)
    {
        // CREATE SCHEMA must be the FIRST statement of a batch; conditional
        // execution is therefore wrapped in EXEC.
        CreateSchema = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{Schema}')
                EXEC(N'CREATE SCHEMA {Schema};');
            """;

        // set_name: each migration SET numbers its own files from
        // 0001, so id alone is no longer unique — the primary key is
        // (set_name, id). A fresh database gets this shape directly; an
        // existing one (created) is upgraded by
        // UpgradeMigrationsTable below — NOT a numbered migration file
        // (it would collide with InsertMigration's fixed text, see
        // SqlDialect.UpgradeMigrationsTableAsync).
        CreateMigrationsTable = $"""
            IF OBJECT_ID(N'{Schema}.__migrations', N'U') IS NULL
            CREATE TABLE {Schema}.__migrations (
                set_name   nvarchar(64)      NOT NULL CONSTRAINT __migrations_set_name_df DEFAULT (N'core'),
                id         int               NOT NULL,
                name       nvarchar(200)     NOT NULL,
                checksum   nvarchar(64)      NOT NULL,
                applied_at datetimeoffset(7) NOT NULL,
                CONSTRAINT __migrations_pk PRIMARY KEY (set_name, id)
            );
            """;

        // Idempotent: a fresh database's __migrations already has this shape
        // (both statements no-op there). An existing table of the older shape gets
        // the column backfilled to 'core' and its single-column primary key
        // widened. Runs as its own command — see SqlDialect.UpgradeMigrationsTableAsync.
        // 🚨 The constraint swap is wrapped in EXEC: it references set_name,
        // which the ADD COLUMN statement above added in the SAME batch —
        // without EXEC this gives "Invalid column name" (0018_audit_chain.sql).
        UpgradeMigrationsTable = $"""
            IF COL_LENGTH(N'{Schema}.__migrations', N'set_name') IS NULL
                ALTER TABLE {Schema}.__migrations
                    ADD set_name nvarchar(64) NOT NULL CONSTRAINT __migrations_set_name_df DEFAULT (N'core');

            IF (
                SELECT COUNT(*)
                FROM sys.index_columns ic
                JOIN sys.key_constraints kc
                  ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
                WHERE kc.name = N'__migrations_pk'
            ) = 1
            EXEC(N'ALTER TABLE {Schema}.__migrations DROP CONSTRAINT __migrations_pk;
            ALTER TABLE {Schema}.__migrations ADD CONSTRAINT __migrations_pk PRIMARY KEY (set_name, id);');
            """;

        UpsertTenant = $"""
            IF NOT EXISTS (SELECT 1 FROM {Schema}.tenants WITH (UPDLOCK, SERIALIZABLE) WHERE slug = @slug)
            INSERT INTO {Schema}.tenants (id, slug, display_name, created_at)
            VALUES (@id, @slug, @display_name, @created_at);
            """;

        // --- Agent definitions ---

        // The next number is one more than the larger of the counter and the agent row's own version: a
        // process of the older release (rolling upgrade) writes the row without touching the counter, and a
        // number taken from the counter alone would then collide with its history entry for good. The row is
        // read without a lock: the counter row's UPDLOCK already orders every writer of this name.
        UpsertAgentDefinitionVersionCounter = $"""
            DECLARE @agent_version int = ISNULL(
                (SELECT version FROM {Schema}.agent_definitions WHERE tenant_id = @tenant_id AND name = @name), 0);

            UPDATE {Schema}.agent_definition_version_counters WITH (UPDLOCK, SERIALIZABLE)
               SET last_version = IIF(last_version > @agent_version, last_version, @agent_version) + 1,
                   updated_at   = @now
             OUTPUT inserted.last_version
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.agent_definition_version_counters (tenant_id, name, last_version, updated_at)
            OUTPUT inserted.last_version
            VALUES (@tenant_id, @name, @agent_version + 1, @now);
            """;

        UpsertAgentDefinition = $"""
            UPDATE {Schema}.agent_definitions WITH (UPDLOCK, SERIALIZABLE)
               SET version    = @version,
                   definition = @definition,
                   updated_at = @now
             OUTPUT inserted.id, inserted.version
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.agent_definitions (id, tenant_id, name, version, definition, created_at, updated_at)
            OUTPUT inserted.id, inserted.version
            VALUES (@id, @tenant_id, @name, @version, @definition, @now, @now);
            """;

        // --- Skills ---

        const string skillColumns = """
            id, tenant_id, name, description, instructions, compatibility, license,
            allowed_tools, metadata, enabled, version, created_at, updated_at
            """;

        SelectAgentSkills = $"""
            SELECT {skillColumns}
            FROM {Schema}.agent_skills
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectAgentSkill = $"""
            SELECT {skillColumns}
            FROM {Schema}.agent_skills
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        UpsertAgentSkill = $"""
            UPDATE {Schema}.agent_skills WITH (UPDLOCK, SERIALIZABLE)
               SET description   = @description,
                   instructions  = @instructions,
                   compatibility = @compatibility,
                   license       = @license,
                   allowed_tools = @allowed_tools,
                   metadata      = @metadata,
                   enabled       = @enabled,
                   version       = version + 1,
                   updated_at    = @now
             OUTPUT inserted.id, inserted.tenant_id, inserted.name, inserted.description,
                    inserted.instructions, inserted.compatibility, inserted.license,
                    inserted.allowed_tools, inserted.metadata, inserted.enabled,
                    inserted.version, inserted.created_at, inserted.updated_at
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.agent_skills ({skillColumns})
            OUTPUT inserted.id, inserted.tenant_id, inserted.name, inserted.description,
                   inserted.instructions, inserted.compatibility, inserted.license,
                   inserted.allowed_tools, inserted.metadata, inserted.enabled,
                   inserted.version, inserted.created_at, inserted.updated_at
            VALUES (@id, @tenant_id, @name, @description, @instructions, @compatibility, @license,
                    @allowed_tools, @metadata, @enabled, 1, @now, @now);
            """;

        // --- Skill scripts ---

        // --- Script execution grants ---

        const string grantColumns = """
            id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at, content_hash
            """;

        SelectSkillScriptGrants = $"""
            SELECT {grantColumns}
            FROM {Schema}.skill_script_grants
            WHERE tenant_id = @tenant_id
            ORDER BY skill_name, ISNULL(script_name, N'');
            """;

        // A narrow grant wins over a broad one: rows with script_name IS NOT
        // NULL are sorted first so the script-specific record comes first,
        // and only one row is taken.
        SelectActiveSkillScriptGrant = $"""
            SELECT TOP (1) {grantColumns}
            FROM {Schema}.skill_script_grants
            WHERE tenant_id = @tenant_id
              AND skill_name = @skill_name
              AND (script_name IS NULL OR script_name = @script_name)
              AND revoked_at IS NULL
              AND (expires_at IS NULL OR expires_at > @instant)
            ORDER BY CASE WHEN script_name IS NULL THEN 1 ELSE 0 END;
            """;

        // SQL Server treats NULLs as EQUAL in a unique index; PostgreSQL's
        // COALESCE-based expression index is not needed here. A plain
        // `=` would return UNKNOWN when @script_name is NULL, and ISNULL on the
        // column cannot seek (deadlock under concurrent first grants):
        // the INTERSECT form is the NULL-safe equality that seeks.
        // 🚨 The update branch writes content_hash too: granting the same key again
        // must pin the content of THIS grant, not keep the previous one's hash.
        UpsertSkillScriptGrant = $"""
            UPDATE {Schema}.skill_script_grants WITH (UPDLOCK, SERIALIZABLE)
               SET granted_by = @granted_by,
                   granted_at = @granted_at,
                   expires_at = @expires_at,
                   revoked_at = NULL,
                   content_hash = @content_hash
             OUTPUT inserted.id, inserted.tenant_id, inserted.skill_name, inserted.script_name,
                    inserted.granted_by, inserted.granted_at, inserted.expires_at, inserted.revoked_at,
                    inserted.content_hash
             WHERE tenant_id = @tenant_id
               AND skill_name = @skill_name
               AND EXISTS (SELECT script_name INTERSECT SELECT @script_name);

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.skill_script_grants ({grantColumns})
            OUTPUT inserted.id, inserted.tenant_id, inserted.skill_name, inserted.script_name,
                   inserted.granted_by, inserted.granted_at, inserted.expires_at, inserted.revoked_at,
                   inserted.content_hash
            VALUES (@id, @tenant_id, @skill_name, @script_name, @granted_by, @granted_at, @expires_at, NULL, @content_hash);
            """;

        // A grant is NOT DELETED, it is revoked. The same seekable NULL-safe match
        // as the upsert: a revoke that scans the neighbouring keys races a grant
        // in flight for the locks it reads.
        RevokeSkillScriptGrant = $"""
            UPDATE {Schema}.skill_script_grants
               SET revoked_at = @revoked_at
             WHERE tenant_id = @tenant_id
               AND skill_name = @skill_name
               AND EXISTS (SELECT script_name INTERSECT SELECT @script_name)
               AND revoked_at IS NULL;
            """;

        // --- Sessions ---

        // See PostgresQueries: `version` advances, it is never taken from
        // the incoming row.
        // See PostgresQueries: owner_id is COALESCEd, never assigned straight
        // 🚨 BOTH branches of the two-branch upsert carry the column,
        // the UPDATE with its COALESCE and the INSERT with the raw parameter -
        // adding it to only one is the silent provider difference the two
        // providers guard against.
        UpsertSession = $"""
            UPDATE {Schema}.sessions WITH (UPDLOCK, SERIALIZABLE)
               SET agent_name           = @agent_name,
                   state                = @state,
                   state_schema_version = @state_schema_version,
                   state_maf_version    = @state_maf_version,
                   updated_at           = @updated_at,
                   owner_id             = COALESCE(owner_id, @owner_id),
                   version              = version + 1
             WHERE id = @id AND tenant_id = @tenant_id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at, version, state_maf_version, owner_id)
            VALUES (@id, @tenant_id, @agent_name, @state, @state_schema_version, @created_at, @updated_at, 1, @state_maf_version, @owner_id);
            """;

        // See PostgresQueries: the owner predicate is in the WHERE clause,
        // ahead of the OFFSET/FETCH paging, so paging runs over the narrowed
        // set.
        SelectSessions = $"""
            SELECT id, agent_name, state, state_schema_version, created_at, updated_at, tenant_id, version, state_maf_version, owner_id
            FROM {Schema}.sessions
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@owner_id IS NULL OR owner_id = @owner_id)
              {TakeGuard}
            ORDER BY updated_at DESC
            {Paging}
            """;

        // --- Runs ---

        // This is an UPSERT (UPDATE-then-INSERT pattern, the same
        // as UpsertConversation). A queued run is first written as Queued;
        // when the worker actually runs the job it is called a second time
        // with the SAME id, and the row is updated in place (no new row is
        // OPENED).
        InsertRun = $"""
            UPDATE {Schema}.runs WITH (UPDLOCK, SERIALIZABLE)
               SET tenant_id     = @tenant_id,
                   agent_name    = @agent_name,
                   session_id    = @session_id,
                   model_id      = @model_id,
                   model_provider = @model_provider,
                   status        = @status,
                   started_at    = @started_at,
                   is_streaming  = @is_streaming,
                   parent_run_id = @parent_run_id,
                   root_run_id   = @root_run_id,
                   depth         = @depth,
                   kind          = @kind,
                   workflow_name = @workflow_name,
                   agent_version = @agent_version,
                   experiment_id = @experiment_id,
                   variant       = @variant,
                   replay_of_run_id = @replay_of_run_id,
                   -- COALESCE for the same reason as the attribution below. The
                   -- party that opens a continuation writes the row with the link; the
                   -- engine that runs it writes it again and does not know the link.
                   continued_from_run_id = COALESCE(@continued_from_run_id, continued_from_run_id),
                   -- 🚨 COALESCE, not a plain overwrite. This is an UPSERT:
                   -- a queued run's placeholder row is written during the HTTP
                   -- request, where the user IS known, and rewritten later by the
                   -- background worker, where it is NOT (an HTTP-bound
                   -- IRunAttributionContext has no request to read). A plain
                   -- overwrite would ERASE the attribution the first write got
                   -- right. Attribution never legitimately goes from set back to
                   -- unset for the same run, so preserving is always correct.
                   user_id       = COALESCE(@user_id, user_id),
                   labels        = COALESCE(@labels, labels)
            OUTPUT inserted.user_id, inserted.labels
             WHERE id = @id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.runs (id, tenant_id, agent_name, session_id, model_id, model_provider, status, started_at, is_streaming, event_count,
                                       parent_run_id, root_run_id, depth, kind, workflow_name, agent_version, experiment_id, variant,
                                       replay_of_run_id, user_id, labels, continued_from_run_id)
            OUTPUT inserted.user_id, inserted.labels
            VALUES (@id, @tenant_id, @agent_name, @session_id, @model_id, @model_provider, @status, @started_at, @is_streaming, 0,
                    @parent_run_id, @root_run_id, @depth, @kind, @workflow_name, @agent_version, @experiment_id, @variant,
                    @replay_of_run_id, @user_id, @labels, @continued_from_run_id);
            """;

        UpdateRunCompletion = $"""
            UPDATE {Schema}.runs
               SET status         = @status,
                   completed_at   = @completed_at,
                   event_count    = @event_count,
                   input_tokens   = @input_tokens,
                   output_tokens  = @output_tokens,
                   total_tokens   = @total_tokens,
                   cached_input_tokens = @cached_input_tokens,
                   reasoning_tokens    = @reasoning_tokens,
                   audio_input_tokens  = @audio_input_tokens,
                   audio_output_tokens = @audio_output_tokens,
                   cache_write_input_tokens      = @cache_write_input_tokens,
                   cache_write_long_input_tokens = @cache_write_long_input_tokens,
                   error_type     = @error_type,
                   error_message  = @error_message,
                   error_class    = @error_class,
                   error_fingerprint = @error_fingerprint,
                   input_cost     = @input_cost,
                   input_price_per_mtok = @input_price_per_mtok,
                   output_cost    = @output_cost,
                   output_price_per_mtok = @output_price_per_mtok,
                   cached_input_cost = @cached_input_cost,
                   cached_input_price_per_mtok = @cached_input_price_per_mtok,
                   cache_write_cost = @cache_write_cost,
                   cache_write_price_per_mtok = @cache_write_price_per_mtok,
                   cache_write_long_price_per_mtok = @cache_write_long_price_per_mtok,
                   cost_currency  = @cost_currency,
                   pricing_source = @pricing_source,
                   non_token_cost      = @non_token_cost,
                   non_token_currency  = @non_token_currency,
                   non_token_estimated = @non_token_estimated,
                   non_token_unsummed  = @non_token_unsummed,
                   model_id       = COALESCE(@model_id, model_id),
                   model_provider = COALESCE(@model_provider, model_provider)
             WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        // Never touches model_provider: recalculation resolves the cost of an
        // EXISTING provider attribution, it does not change it.
        UpdateRunCost = $"""
            UPDATE {Schema}.runs
               SET input_cost     = @input_cost,
                   input_price_per_mtok = @input_price_per_mtok,
                   output_cost    = @output_cost,
                   output_price_per_mtok = @output_price_per_mtok,
                   cached_input_cost = @cached_input_cost,
                   cached_input_price_per_mtok = @cached_input_price_per_mtok,
                   cache_write_cost = @cache_write_cost,
                   cache_write_price_per_mtok = @cache_write_price_per_mtok,
                   cache_write_long_price_per_mtok = @cache_write_long_price_per_mtok,
                   cost_currency  = @cost_currency,
                   pricing_source = @pricing_source
             WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        // Orphaned run reconciliation. Only affects Running and Queued
        // rows (a run waiting in-process for a workflow slot is Queued
        // and still owned by this process); silently updates zero rows for an id
        // that does not exist or is in a different status.
        //
        // The statement also reads the row's cancellation request back. OUTPUT sits
        // after SET and before WHERE (T-SQL), and the statement is a single UPDATE, not an
        // upsert, so the two-branch OUTPUT trap does not apply.
        TouchRunHeartbeat = $"""
            UPDATE {Schema}.runs
               SET heartbeat_at = @at
            OUTPUT inserted.tenant_id, inserted.cancel_requested_at
             WHERE id = @id AND status IN (@status_running, @status_queued);
            """;

        // TOP (@n) = 0 does NOT raise an error (unlike SQL Server's FETCH
        // NEXT); no extra guard is needed. error_fingerprint is a FIXED
        // string ("orphaned") -- the rationale is the same as in the
        // PostgreSQL version, and so is the Queued rule: a Queued
        // row is claimed only when it has a heartbeat.
        ClaimOrphanedRuns = $"""
            UPDATE r
               SET status            = @status_failed,
                   completed_at      = @now,
                   error_type        = N'orphaned',
                   error_message     = N'The process running this run is not responding; last heartbeat: '
                                        + CONVERT(nvarchar(40), COALESCE(heartbeat_at, started_at), 127) + N'.',
                   error_class       = @error_class,
                   error_fingerprint = @error_fingerprint,
                   event_count       = event_count + 1
            OUTPUT inserted.id, inserted.tenant_id, inserted.agent_name, inserted.session_id, inserted.status,
                   inserted.started_at, inserted.completed_at, inserted.is_streaming, inserted.model_id,
                   inserted.kind, inserted.workflow_name, inserted.agent_version, inserted.experiment_id,
                   inserted.variant, inserted.replay_of_run_id, inserted.parent_run_id, inserted.root_run_id,
                   inserted.depth, inserted.event_count, inserted.error_type, inserted.error_message,
                   inserted.error_class, inserted.error_fingerprint, inserted.continued_from_run_id,
                   inserted.cancel_requested_at
              FROM {Schema}.runs AS r
             WHERE r.id IN (
                       SELECT TOP (@max) id FROM {Schema}.runs
                        WHERE ((status = @status_running AND COALESCE(heartbeat_at, started_at) < @stale_before)
                            OR (status = @status_queued AND heartbeat_at < @stale_before))
                     ORDER BY COALESCE(heartbeat_at, started_at) ASC
                   );
            """;

        // The counterpart of PostgreSQL's LEFT JOIN LATERAL ... ON TRUE
        // construct is OUTER APPLY. Tree totals are computed ON READ, not
        // stored.
        var treeJoin = $"""
            OUTER APPLY (
                SELECT CAST(COUNT(*) AS int) AS child_count
                FROM {Schema}.runs AS child
                WHERE child.parent_run_id = r.id
            ) AS children
            OUTER APPLY (
                SELECT CAST({TreeSum("input_tokens", "sub", true)} AS bigint)  AS input_tokens,
                       CAST({TreeSum("output_tokens", "sub", true)} AS bigint) AS output_tokens,
                       CAST({TreeSum("total_tokens", "sub", true)} AS bigint)  AS total_tokens,
                       CAST(COUNT(sub.total_tokens) AS bigint)             AS usage_rows,
                       -- 🚨 NOT COALESCE'd to zero, unlike the three above: a
                       -- descendant tree in which nobody reported cache usage
                       -- must read as "not measured", not "measured zero".
                       CAST({TreeSum("cached_input_tokens", "sub", false)} AS bigint)        AS cached_input_tokens,
                       CAST({TreeSum("reasoning_tokens", "sub", false)} AS bigint)           AS reasoning_tokens,
                       CAST({TreeSum("audio_input_tokens", "sub", false)} AS bigint)         AS audio_input_tokens,
                       CAST({TreeSum("audio_output_tokens", "sub", false)} AS bigint)        AS audio_output_tokens,
                       SUM(sub.input_cost)                                 AS cost_input,
                       SUM(sub.output_cost)                                AS cost_output,
                       SUM(sub.cached_input_cost)                          AS cost_cached_input,
                       -- What the descendants wrote into the prompt cache.
                       -- NOT coalesced, like the cache read above; cache_write_cost is the
                       -- FOURTH addend of the tree total, not a subset of input_cost.
                       CAST({TreeSum("cache_write_input_tokens", "sub", false)} AS bigint)      AS cache_write_input_tokens,
                       CAST({TreeSum("cache_write_long_input_tokens", "sub", false)} AS bigint) AS cache_write_long_input_tokens,
                       SUM(sub.cache_write_cost)                           AS cost_cache_write,
                       {CostCurrencyLabel("sub")}                          AS cost_currency,
                       CAST(COALESCE(SUM(CASE WHEN sub.pricing_source = 2 THEN 1 ELSE 0 END), 0) AS bigint) AS unknown_pricing_rows,
                       CAST(COUNT(sub.pricing_source) AS bigint)           AS pricing_rows,
                       -- What the descendants reported beyond tokens. Their own
                       -- totals, NOT terms of the token cost above, and NOT coalesced:
                       -- non_token_unsummed is NULL when no descendant reported anything.
                       SUM(sub.non_token_cost)                             AS non_token_cost,
                       {ReportedCurrencyLabel("sub")}                      AS non_token_currency,
                       CAST(COALESCE(SUM(CASE WHEN sub.non_token_estimated = 1 THEN 1 ELSE 0 END), 0) AS bigint) AS non_token_estimated_rows,
                       CAST(SUM(sub.non_token_unsummed) AS bigint)         AS non_token_unsummed,
                       -- The descendants' currencies the run's own row does not
                       -- carry; the reader adds the own one and leaves a mixed total out.
                       CAST({DescendantCurrencyCount(PricedCurrency, "sub", "own.cost_currency")} AS bigint)       AS cost_currencies,
                       CAST({DescendantCurrencyCount(ReportedCurrency, "sub", "own.non_token_currency")} AS bigint) AS non_token_currencies
                FROM {Schema}.runs AS sub
                -- 🚨 The own row's currencies come in through this inner APPLY: SQL Server
                -- refuses an aggregate whose expression mixes an outer reference (r.*)
                -- with an inner column (measured: "Multiple columns are specified in an
                -- aggregated expression containing an outer reference").
                CROSS APPLY (SELECT {PricedCurrency("r")} AS cost_currency,
                                    {ReportedCurrency("r")} AS non_token_currency) AS own
                WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id
            ) AS tree
            """;

        // attribution filter, written ONCE and reused by the run list
        // and by every result set of the statistics query.
        //
        // 🚨 The NULL check is written as an explicit guard rather than an
        // ISNULL(..., N'<empty object>') wrapper: OPENJSON over NULL is not
        // worth relying on, and a literal brace cannot appear in a single-'$'
        // raw interpolated string anyway. The `key`/`value` columns are
        // BRACKETED -- both are reserved words in T-SQL, unlike PostgreSQL.
        static string AttributionFilter(string prefix) => $"""
                      AND (@user_id IS NULL OR {prefix}user_id = @user_id)
                      AND (@label_key IS NULL
                           OR ({prefix}labels IS NOT NULL
                               AND EXISTS (
                                   SELECT 1 FROM OPENJSON({prefix}labels) AS kv
                                   WHERE kv.[key] = @label_key
                                     AND (@label_value IS NULL OR kv.[value] = @label_value))))
            """;

        var runListAttributionFilter = AttributionFilter("r.");
        var statisticsAttributionFilter = AttributionFilter(string.Empty);

        // 🚨 Column order is IDENTICAL to PostgreSQL: SqlRunStore.ReadRun reads
        // by fixed ordinal position, and the two providers share the same
        // reader.
        const string runColumns = """
            r.id, r.tenant_id, r.agent_name, r.session_id, r.status, r.started_at, r.completed_at, r.is_streaming,
            r.input_tokens, r.output_tokens, r.total_tokens, r.event_count, r.error_type, r.error_message, r.model_id,
            r.parent_run_id, r.root_run_id, r.depth,
            children.child_count,
            tree.input_tokens, tree.output_tokens, tree.total_tokens, tree.usage_rows,
            r.kind, r.workflow_name, r.agent_version, r.experiment_id, r.variant,
            r.input_cost, r.output_cost, r.cost_currency, r.pricing_source,
            tree.cost_input, tree.cost_output, tree.cost_currency, tree.unknown_pricing_rows, tree.pricing_rows,
            r.error_class, r.error_fingerprint,
            r.replay_of_run_id,
            r.user_id, r.labels,
            r.cached_input_tokens, r.reasoning_tokens, r.audio_input_tokens, r.audio_output_tokens,
            r.cached_input_cost,
            tree.cached_input_tokens, tree.reasoning_tokens, tree.audio_input_tokens, tree.audio_output_tokens,
            tree.cost_cached_input,
            r.continued_from_run_id,
            r.model_provider, r.input_price_per_mtok, r.output_price_per_mtok, r.cached_input_price_per_mtok,
            r.non_token_cost, r.non_token_currency, r.non_token_estimated, r.non_token_unsummed,
            tree.non_token_cost, tree.non_token_currency, tree.non_token_estimated_rows, tree.non_token_unsummed,
            tree.cost_currencies, tree.non_token_currencies,
            r.cache_write_input_tokens, r.cache_write_long_input_tokens, r.cache_write_cost,
            r.cache_write_price_per_mtok, r.cache_write_long_price_per_mtok,
            tree.cache_write_input_tokens, tree.cache_write_long_input_tokens, tree.cost_cache_write,
            r.cancel_requested_at
            """;

        SelectRun = $"""
            SELECT {runColumns}
            FROM {Schema}.runs AS r
            {treeJoin}
            WHERE r.id = @id AND r.tenant_id = @tenant_id;
            """;

        SelectRuns = $"""
            SELECT {runColumns}
            FROM {Schema}.runs AS r
            {treeJoin}
            WHERE r.tenant_id = @tenant_id
              AND (@agent_name IS NULL OR r.agent_name = @agent_name)
              AND (@status     IS NULL OR r.status     = @status)
              AND (@kind       IS NULL OR r.kind       = @kind)
              AND (@error_type IS NULL OR r.error_type = @error_type)
              AND (
                    -- Session_id is set only on the ROOT run
                    -- A child run's own session_id is NULL. Direct
                    -- equality would match no child run at all when combined
                    -- with "includeChildren=true" -- a row also matches when
                    -- the ROOT of its own tree belongs to this session.
                    @session_id IS NULL
                 OR r.session_id = @session_id
                 OR EXISTS (
                        SELECT 1 FROM {Schema}.runs AS session_root
                        WHERE session_root.tenant_id = r.tenant_id
                          AND session_root.id = COALESCE(r.root_run_id, r.id)
                          AND session_root.session_id = @session_id
                    )
              )
              AND (@started_after IS NULL OR r.started_at > @started_after)
            {runListAttributionFilter}
              AND (@root_run_id IS NULL OR r.root_run_id = @root_run_id OR r.id = @root_run_id)
              AND (@continued_from_run_id IS NULL OR r.continued_from_run_id = @continued_from_run_id)
              AND (
                    -- When a parent filter is given, the root filter is
                    -- DELIBERATELY ignored.
                    (@parent_run_id IS NOT NULL AND r.parent_run_id = @parent_run_id)
                 OR (@parent_run_id IS NULL AND (@only_root_runs = 0 OR r.parent_run_id IS NULL))
              )
              {TakeGuard}
            ORDER BY r.started_at DESC, r.id DESC
            {Paging}
            """;

        // scored_runs/positive_rate: matchedRunScoresFilter repeats
        // the SAME filter (tenant/eval/agent/date) as runs -- it is added as a
        // scalar subquery instead of a separate CTE so the existing totals
        // row can be extended with the smallest possible change.
        var matchedRunScoresFilter = $"""
            {Schema}.run_scores rs
            JOIN {Schema}.runs r2 ON r2.id = rs.run_id
            WHERE rs.tenant_id = @tenant_id
              AND r2.tenant_id = @tenant_id
              AND r2.kind <> @kind_eval
              AND (@agent_name IS NULL OR r2.agent_name = @agent_name)
              AND (@started_after IS NULL OR r2.started_at > @started_after)
            {AttributionFilter("r2.")}
            """;

        // Four result sets are retrieved in a single round trip.
        SelectRunStatistics = $"""
            SELECT CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_completed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_failed    THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_canceled  THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_running   THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_awaiting  THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST({TreeSum("input_tokens", null, true)} AS bigint),
                   CAST({TreeSum("output_tokens", null, true)} AS bigint),
                   CAST({TreeSum("total_tokens", null, true)} AS bigint),
                   CASE WHEN {CountWhereAnyNotNull(CostAddends, null)} = 0
                        THEN NULL ELSE {CostTotal(null)} END,
                   {CostCurrencyLabel(null)},
                   CAST(COALESCE(SUM(CASE WHEN pricing_source = @pricing_source_unknown THEN 1 ELSE 0 END), 0) AS bigint),
                   (SELECT CAST(COUNT(DISTINCT rs.run_id) AS bigint) FROM {matchedRunScoresFilter}),
                   -- 🚨 A null value is excluded from the DENOMINATOR too; see
                   -- the note in PostgresQueries.
                   (SELECT CASE WHEN COALESCE(SUM(CASE WHEN rs.kind = @kind_binary AND rs.value IS NOT NULL THEN 1 ELSE 0 END), 0) = 0 THEN NULL
                                ELSE CAST(COALESCE(SUM(CASE WHEN rs.kind = @kind_binary AND rs.value = 1 THEN 1 ELSE 0 END), 0) AS float)
                                     / SUM(CASE WHEN rs.kind = @kind_binary AND rs.value IS NOT NULL THEN 1 ELSE 0 END)
                           END
                    FROM {matchedRunScoresFilter}),
                   -- Ordinals 14-17, APPENDED so the reader's fixed positions
                   -- above do not move. These four are counted INSIDE the
                   -- input/output totals and are reported beside them.
                   CAST({TreeSum("cached_input_tokens", null, true)} AS bigint),
                   CAST({TreeSum("reasoning_tokens", null, true)} AS bigint),
                   CAST({TreeSum("audio_input_tokens", null, true)} AS bigint),
                   CAST({TreeSum("audio_output_tokens", null, true)} AS bigint),
                   -- Ordinal 18: the currencies the costs above are in.
                   CAST({CostCurrencyCount(null)} AS bigint),
                   -- Ordinals 19-20: the cache writes, counted INSIDE the input total.
                   CAST({TreeSum("cache_write_input_tokens", null, true)} AS bigint),
                   CAST({TreeSum("cache_write_long_input_tokens", null, true)} AS bigint)
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter};

            SELECT TOP (@max_agents)
                   agent_name,
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_failed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST({TreeSum("total_tokens", null, true)} AS bigint)
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter}
            GROUP BY agent_name
            ORDER BY COUNT(*) DESC, agent_name;

            SELECT TOP (@max_agents)
                   model_id,
                   CAST(COUNT(*) AS bigint),
                   CAST({TreeSum("input_tokens", null, true)} AS bigint),
                   CAST({TreeSum("output_tokens", null, true)} AS bigint),
                   CAST({TreeSum("total_tokens", null, true)} AS bigint),
                   CASE WHEN {CountWhereAnyNotNull(CostAddends, null)} = 0
                        THEN NULL ELSE {CostTotal(null)} END
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND model_id IS NOT NULL
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter}
            GROUP BY model_id
            ORDER BY COUNT(*) DESC, model_id;

            SELECT TOP (@max_agents)
                   agent_name,
                   agent_version,
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_failed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST({TreeSum("total_tokens", null, true)} AS bigint)
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND agent_version IS NOT NULL
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter}
            GROUP BY agent_name, agent_version
            ORDER BY agent_name, agent_version DESC;

            -- Fifth result set: error class breakdown. Rows with a
            -- NULL error_class (written before the error class column existed)
            -- fall into the Unknown (0) bucket -- nothing backfills them.
            SELECT CAST(COALESCE(error_class, 0) AS smallint),
                   CAST(COUNT(*) AS bigint)
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND status = @status_failed
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter}
            GROUP BY COALESCE(error_class, 0)
            ORDER BY COUNT(*) DESC;

            -- Sixth result set: the top fingerprint clusters per class.
            -- See PostgreSQL 0021_error_classification.sql for the rationale.
            WITH failed AS (
                SELECT CAST(COALESCE(error_class, 0) AS smallint) AS error_class,
                       COALESCE(error_fingerprint, N'') AS error_fingerprint,
                       error_message,
                       id,
                       started_at,
                       CAST(COUNT(*) OVER (
                           PARTITION BY COALESCE(error_class, 0), COALESCE(error_fingerprint, N'')
                       ) AS bigint) AS cluster_count,
                       MAX(started_at) OVER (
                           PARTITION BY COALESCE(error_class, 0), COALESCE(error_fingerprint, N'')
                       ) AS last_seen_at,
                       ROW_NUMBER() OVER (
                           PARTITION BY COALESCE(error_class, 0), COALESCE(error_fingerprint, N'')
                           ORDER BY started_at DESC
                       ) AS sample_rank
                FROM {Schema}.runs
                WHERE tenant_id = @tenant_id
                  AND kind <> @kind_eval
                  AND status = @status_failed
                  AND (@agent_name IS NULL OR agent_name = @agent_name)
                  AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter}
            ),
            samples AS (
                SELECT error_class, error_fingerprint, cluster_count, last_seen_at,
                       id AS sample_run_id, COALESCE(error_message, N'') AS sample_message
                FROM failed
                WHERE sample_rank = 1
            ),
            ranked AS (
                SELECT *,
                       ROW_NUMBER() OVER (PARTITION BY error_class ORDER BY cluster_count DESC, last_seen_at DESC) AS cluster_rank
                FROM samples
            )
            SELECT error_class, error_fingerprint, cluster_count, sample_message, sample_run_id, last_seen_at
            FROM ranked
            WHERE cluster_rank <= @top_clusters
            ORDER BY error_class, cluster_count DESC;
            
            -- Seventh result set: breakdown by user. Runs carrying no
            -- user stay OUT of this list but remain in the totals, exactly as
            -- runs with an unknown model do. APPENDED after the existing sets so
            -- their positions in SqlRunStore hold.
            SELECT TOP (@max_agents)
                   user_id,
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_failed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST({TreeSum("total_tokens", null, true)} AS bigint),
                   CASE WHEN {CountWhereAnyNotNull(CostAddends, null)} = 0
                        THEN NULL ELSE {CostTotal(null)} END
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND user_id IS NOT NULL
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter}
            GROUP BY user_id
            ORDER BY COUNT(*) DESC, user_id;

            -- Eighth result set: breakdown by label. CROSS APPLY OPENJSON is the
            -- counterpart of PostgreSQL's jsonb_each_text, so a run carrying
            -- three labels contributes to THREE rows.
            -- 🚨 These rows therefore do NOT sum to TotalRuns, unlike every other
            -- breakdown: a label set is not a partition of the runs.
            SELECT TOP (@max_agents)
                   kv.[key],
                   kv.[value],
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN r.status = @status_failed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST({TreeSum("total_tokens", "r", true)} AS bigint),
                   CASE WHEN {CountWhereAnyNotNull(CostAddends, "r")} = 0
                        THEN NULL ELSE {CostTotal("r")} END
            FROM {Schema}.runs AS r
            CROSS APPLY OPENJSON(r.labels) AS kv
            WHERE r.tenant_id = @tenant_id
              AND r.kind <> @kind_eval
              AND r.labels IS NOT NULL
              AND (@agent_name IS NULL OR r.agent_name = @agent_name)
              AND (@started_after IS NULL OR r.started_at > @started_after)
            {runListAttributionFilter}
            GROUP BY kv.[key], kv.[value]
            ORDER BY COUNT(*) DESC, kv.[key], kv.[value];
""";

        // --- Conversations (chat history) ---

        // UPDATE locks the row, so two transactions writing to the same
        // conversation at the same time wait for their turn on the sequence
        // number.
        UpsertConversation = $"""
            UPDATE {Schema}.conversations WITH (UPDLOCK, SERIALIZABLE)
               SET updated_at = @now
             WHERE id = @id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.conversations (id, tenant_id, agent_name, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @now, @now);
            """;

        // --- Conversation branching ---
        // See PostgresQueries for the rationale and the meaning of the columns.

        // --- Run inputs ---
        // 🚨 A second write is ignored; T-SQL has no ON CONFLICT, so the
        // condition is written with NOT EXISTS (MERGE is not used).
        InsertRunInput = $"""
            INSERT INTO {Schema}.run_inputs (run_id, tenant_id, messages, created_at, parameters)
            SELECT @run_id, @tenant_id, @messages, @created_at, @parameters
            WHERE NOT EXISTS (SELECT 1 FROM {Schema}.run_inputs WHERE run_id = @run_id);
            """;

        // --- Tool invocations ---

        SelectToolUsage = $"""
            SELECT TOP (@max_tools)
                   t.tool_name,
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN t.error IS NOT NULL THEN 1 ELSE 0 END), 0) AS bigint),
                   AVG(CAST(t.duration_ms AS float)),
                   MAX(t.created_at)
            FROM {Schema}.tool_invocations t
            JOIN {Schema}.runs r ON r.id = t.run_id
            WHERE r.tenant_id = @tenant_id
              AND (@started_after IS NULL OR r.started_at > @started_after)
            GROUP BY t.tool_name
            ORDER BY COUNT(*) DESC, t.tool_name;
            """;

        // --- Experiments ---

        const string experimentColumns = """
            id, tenant_id, name, agent_name, variants, status, assignment_key, started_at, ended_at, updated_at,
            canary_policy, rollback_reason
            """;

        SelectExperiments = $"""
            SELECT {experimentColumns}
            FROM {Schema}.experiments
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectExperiment = $"""
            SELECT {experimentColumns}
            FROM {Schema}.experiments
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectRunningExperiment = $"""
            SELECT {experimentColumns}
            FROM {Schema}.experiments
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND status = 1;
            """;

        SelectRunningExperimentsWithCanary = $"""
            SELECT {experimentColumns}
            FROM {Schema}.experiments
            WHERE status = 1 AND canary_policy IS NOT NULL;
            """;

        // 🚨 UPDATE affects zero rows for a non-Draft experiment; the INSERT
        // branch runs only when the record does NOT EXIST AT ALL. Otherwise a
        // uniqueness violation would occur. `@@ROWCOUNT` is first captured
        // into a variable: in a compound condition, the counter would be
        // reset if the subquery were evaluated first.
        // canary_policy/rollback_reason are DELIBERATELY ABSENT from the SET
        // list: a Draft edit (SaveAsync) must not erase a rule that
        // SetCanaryPolicyAsync already defined.
        UpsertExperiment = $"""
            DECLARE @updated int;

            UPDATE {Schema}.experiments WITH (UPDLOCK, SERIALIZABLE)
               SET agent_name     = @agent_name,
                   variants       = @variants,
                   assignment_key = @assignment_key,
                   updated_at     = @updated_at
             OUTPUT inserted.id
             WHERE tenant_id = @tenant_id AND name = @name AND status = 0;

            SET @updated = @@ROWCOUNT;

            IF @updated = 0 AND NOT EXISTS (
                SELECT 1 FROM {Schema}.experiments WITH (UPDLOCK, SERIALIZABLE)
                 WHERE tenant_id = @tenant_id AND name = @name)
            INSERT INTO {Schema}.experiments (id, tenant_id, name, agent_name, variants, status, assignment_key, updated_at)
            OUTPUT inserted.id
            VALUES (@id, @tenant_id, @name, @agent_name, @variants, 0, @assignment_key, @updated_at);
            """;

        StartExperiment = $"""
            UPDATE {Schema}.experiments
               SET status = 1, started_at = @now, updated_at = @now
             OUTPUT inserted.id
             WHERE tenant_id = @tenant_id AND name = @name AND status = 0;
            """;

        StopExperiment = $"""
            UPDATE {Schema}.experiments
               SET status = 2, ended_at = @now, updated_at = @now
             OUTPUT inserted.id
             WHERE tenant_id = @tenant_id AND name = @name AND status = 1;
            """;

        // Runs INDEPENDENTLY of status (Draft or Running) — unlike SaveAsync.
        SetExperimentCanaryPolicy = $"""
            UPDATE {Schema}.experiments
               SET canary_policy = @canary_policy, updated_at = @now
             OUTPUT inserted.id
             WHERE tenant_id = @tenant_id AND name = @name;
            """;

        AdvanceExperimentCanaryRamp = $"""
            UPDATE {Schema}.experiments
               SET variants = @variants, updated_at = @now
             OUTPUT inserted.id
             WHERE tenant_id = @tenant_id AND name = @name AND status = 1;
            """;

        RollbackExperimentCanary = $"""
            UPDATE {Schema}.experiments
               SET variants = @variants, status = 2, ended_at = @now, rollback_reason = @rollback_reason, updated_at = @now
             OUTPUT inserted.id
             WHERE tenant_id = @tenant_id AND name = @name AND status = 1;
            """;

        // EXTRACT(EPOCH FROM (a - b)) * 1000 -> DATEDIFF_BIG(millisecond, b, a).
        // run_avg_scores: FIRST the per-run average, THEN the average of these
        // averages per variant — SAME rationale as PostgreSQL's identical CTE
        // (a direct JOIN would have duplicated the other totals).
        SelectExperimentResults = $"""
            WITH run_avg_scores AS (
                SELECT run_id, AVG(CAST(value AS float)) AS avg_score
                FROM {Schema}.run_scores
                -- RunScore.MessageId is "if EMPTY, the score belongs to the
                -- whole run" -- empty covers both NULL and ''. Matching only
                -- NULL would drop a '' row out of the arm average while the
                -- score summary still counted it as run-level.
                WHERE tenant_id = @tenant_id AND kind = @score_kind_numeric
                  AND (message_id IS NULL OR message_id = N'')
                GROUP BY run_id
            )
            SELECT r.variant,
                   MAX(r.agent_version),
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN r.status = @status_completed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN r.status = @status_failed    THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN r.status = @status_canceled  THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST({TreeSum("input_tokens", "r", true)} AS bigint),
                   CAST({TreeSum("output_tokens", "r", true)} AS bigint),
                   CAST({TreeSum("total_tokens", "r", true)} AS bigint),
                   AVG(CASE WHEN r.completed_at IS NOT NULL
                            THEN CAST(DATEDIFF_BIG(millisecond, r.started_at, r.completed_at) AS float) END),
                   CASE WHEN {CountWhereAnyNotNull(CostAddends, "r")} = 0
                        THEN NULL ELSE {CostTotal("r")} END,
                   {CostCurrencyLabel("r")},
                   AVG(s.avg_score),
                   -- The currencies the arm's cost is in.
                   CAST({CostCurrencyCount("r")} AS bigint)
            FROM {Schema}.runs r
            LEFT JOIN run_avg_scores s ON s.run_id = r.id
            WHERE r.tenant_id = @tenant_id AND r.experiment_id = @experiment_id AND r.variant IS NOT NULL
            GROUP BY r.variant;
            """;

        // 🚨 The counterpart of generate_series is a recursive CTE.
        // date_trunc(@unit, x) cannot take a parameterized DATEPART; it is
        // written with CASE, and the computation is done on datetime2 and
        // converted back with TODATETIMEOFFSET (values are always written in
        // UTC, so the offset is zero).
        //
        // Empty buckets are also returned: otherwise a gap in the chart would
        // look like "zero" instead of "no data".
        // One filter for the buckets and for the series' currency count: a copy
        // that drifted would count the currencies of other runs than the ones it adds.
        var timeSeriesFilter = """
                WHERE tenant_id = @tenant_id
                  AND started_at >= @from_ts AND started_at < @to_ts
                  AND (@agent_name IS NULL OR agent_name = @agent_name)
                  AND (@model_id   IS NULL OR model_id   = @model_id)
                  AND (@kind       IS NULL OR kind       = @kind)
            """;

        SelectRunTimeSeries = $"""
            WITH buckets AS (
                SELECT CASE WHEN @bucket_unit = N'hour'
                            THEN DATEADD(hour, DATEDIFF(hour, 0, CONVERT(datetime2(7), @from_ts)), CONVERT(datetime2(7), '1900-01-01'))
                            ELSE DATEADD(day,  DATEDIFF(day,  0, CONVERT(datetime2(7), @from_ts)), CONVERT(datetime2(7), '1900-01-01'))
                       END AS bucket
                UNION ALL
                SELECT CASE WHEN @bucket_unit = N'hour'
                            THEN DATEADD(hour, 1, bucket)
                            ELSE DATEADD(day,  1, bucket)
                       END
                FROM buckets
                WHERE (CASE WHEN @bucket_unit = N'hour' THEN DATEADD(hour, 1, bucket) ELSE DATEADD(day, 1, bucket) END)
                      < CONVERT(datetime2(7), @to_ts)
            ),
            matched AS (
                SELECT CASE WHEN @bucket_unit = N'hour'
                            THEN DATEADD(hour, DATEDIFF(hour, 0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), '1900-01-01'))
                            ELSE DATEADD(day,  DATEDIFF(day,  0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), '1900-01-01'))
                       END AS bucket,
                       CAST(COUNT(*) AS bigint) AS runs,
                       CAST(COALESCE(SUM(CASE WHEN status = @status_failed THEN 1 ELSE 0 END), 0) AS bigint) AS failed_runs,
                       CAST({TreeSum("input_tokens", null, true)} AS bigint) AS input_tokens,
                       CAST({TreeSum("output_tokens", null, true)} AS bigint) AS output_tokens,
                       CASE WHEN {CountWhereAnyNotNull(CostAddends, null)} = 0
                            THEN NULL ELSE {CostTotal(null)} END AS cost,
                       AVG(CASE WHEN completed_at IS NOT NULL
                                THEN CAST(DATEDIFF_BIG(millisecond, started_at, completed_at) AS float) END) AS avg_duration_ms
                FROM {Schema}.runs
            {timeSeriesFilter}
                GROUP BY CASE WHEN @bucket_unit = N'hour'
                              THEN DATEADD(hour, DATEDIFF(hour, 0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), '1900-01-01'))
                              ELSE DATEADD(day,  DATEDIFF(day,  0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), '1900-01-01'))
                         END
            ),
            -- A point carries no currency, so the WHOLE series is judged as one.
            series AS (
                SELECT CAST({CostCurrencyCount(null)} AS bigint) AS currencies
                FROM {Schema}.runs
            {timeSeriesFilter}
            )
            SELECT TODATETIMEOFFSET(buckets.bucket, 0),
                   COALESCE(matched.runs, 0),
                   COALESCE(matched.failed_runs, 0),
                   COALESCE(matched.input_tokens, 0),
                   COALESCE(matched.output_tokens, 0),
                   matched.cost,
                   matched.avg_duration_ms,
                   series.currencies
            FROM buckets
            CROSS JOIN series
            LEFT JOIN matched ON matched.bucket = buckets.bucket
            WHERE buckets.bucket < CONVERT(datetime2(7), @to_ts)
              AND @bucket_step >= 0
            ORDER BY buckets.bucket
            OPTION (MAXRECURSION 0);
            """;

        // --- Spans ---

        // LEAST / GREATEST do not exist in SQL Server 2019. In PostgreSQL,
        // these two functions SKIP a NULL argument; the CASE chain builds
        // the same behavior.
        UpsertTrace = $"""
            UPDATE {Schema}.traces WITH (UPDLOCK, SERIALIZABLE)
               SET run_id     = COALESCE(@run_id, run_id),
                   started_at = CASE WHEN @started_at IS NULL THEN started_at
                                     WHEN started_at IS NULL THEN @started_at
                                     WHEN @started_at < started_at THEN @started_at
                                     ELSE started_at END,
                   ended_at   = CASE WHEN @ended_at IS NULL THEN ended_at
                                     WHEN ended_at IS NULL THEN @ended_at
                                     WHEN @ended_at > ended_at THEN @ended_at
                                     ELSE ended_at END
             OUTPUT inserted.id
             WHERE tenant_id = @tenant_id AND trace_id = @trace_id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.traces (id, tenant_id, trace_id, run_id, started_at, ended_at)
            OUTPUT inserted.id
            VALUES (@id, @tenant_id, @trace_id, @run_id, @started_at, @ended_at);
            """;

        UpsertSpan = $"""
            UPDATE {Schema}.spans WITH (UPDLOCK, SERIALIZABLE)
               SET ended_at   = @ended_at,
                   attributes = @attributes,
                   status     = @status
             WHERE id = @id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.spans
                (id, trace_id, parent_span_id, span_id, name, kind, started_at, ended_at, attributes, status)
            VALUES
                (@id, @trace_id, @parent_span_id, @span_id, @name, @kind, @started_at, @ended_at, @attributes, @status);
            """;

        // --- Tool approval rules ---

        // 🚨 New columns are ALWAYS appended at the end: SqlToolApprovalRuleStore.ReadRule
        // reads argument_conditions by fixed ordinal 7 (.md's
        // ordinal-position lesson applies equally here).
        const string approvalColumns = """
            id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at, argument_conditions, source_fingerprint
            """;

        // A second rule is not opened for the same scope; the existing record
        // is returned. UPDATE deliberately replaces a column with itself: the
        // goal is not to write, but to return the existing row via OUTPUT.
        // conditions_hash joined the WHERE match: a data-rule condition
        // set is bound by the same "no unbounded duplicates" rule as arguments_hash.
        // source_fingerprint joined it so a rule for a server that moved to
        // another endpoint is a new rule, never the old one returned in its place.
        InsertToolApprovalRule = $"""
            UPDATE {Schema}.tool_approval_rules WITH (UPDLOCK, SERIALIZABLE)
               SET tool_name = tool_name
             OUTPUT inserted.id, inserted.tenant_id, inserted.agent_name, inserted.tool_name,
                    inserted.arguments_hash, inserted.created_by, inserted.created_at, inserted.argument_conditions,
                    inserted.source_fingerprint
             WHERE tenant_id = @tenant_id
               AND EXISTS (SELECT agent_name INTERSECT SELECT @agent_name)
               AND tool_name = @tool_name
               AND EXISTS (SELECT arguments_hash INTERSECT SELECT @arguments_hash)
               AND EXISTS (SELECT conditions_hash INTERSECT SELECT @conditions_hash)
               AND EXISTS (SELECT source_fingerprint INTERSECT SELECT @source_fingerprint);

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.tool_approval_rules ({approvalColumns}, conditions_hash)
            OUTPUT inserted.id, inserted.tenant_id, inserted.agent_name, inserted.tool_name,
                   inserted.arguments_hash, inserted.created_by, inserted.created_at, inserted.argument_conditions,
                   inserted.source_fingerprint
            VALUES (@id, @tenant_id, @agent_name, @tool_name, @arguments_hash, @created_by, @created_at, @argument_conditions, @source_fingerprint, @conditions_hash);
            """;

        // --- MCP servers ---

        const string mcpServerOutput = """
            inserted.id, inserted.tenant_id, inserted.name, inserted.description, inserted.endpoint,
            inserted.transport, inserted.headers,
            inserted.enabled, inserted.requires_approval, inserted.created_at, inserted.updated_at,
            inserted.oauth_enabled, inserted.oauth_client_id,
            inserted.oauth_client_secret_configuration_key, inserted.oauth_scopes,
            inserted.oauth_authorization_mode, inserted.header_configuration_keys
            """;

        UpsertMcpServer = $"""
            UPDATE {Schema}.mcp_servers WITH (UPDLOCK, SERIALIZABLE)
               SET description                           = @description,
                   endpoint                              = @endpoint,
                   transport                             = @transport,
                   headers                               = @headers,
                   enabled                               = @enabled,
                   requires_approval                     = @requires_approval,
                   updated_at                            = @now,
                   oauth_enabled                         = @oauth_enabled,
                   oauth_client_id                       = @oauth_client_id,
                   oauth_client_secret_configuration_key = @oauth_client_secret_configuration_key,
                   oauth_scopes                          = @oauth_scopes,
                   oauth_authorization_mode              = @oauth_authorization_mode,
                   header_configuration_keys             = @header_configuration_keys
             OUTPUT {mcpServerOutput}
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.mcp_servers ({McpServerColumns})
            OUTPUT {mcpServerOutput}
            VALUES (@id, @tenant_id, @name, @description, @endpoint, @transport,
                    @headers, @enabled, @requires_approval, @now, @now,
                    @oauth_enabled, @oauth_client_id, @oauth_client_secret_configuration_key,
                    @oauth_scopes, @oauth_authorization_mode, @header_configuration_keys);
            """;

        // --- Tenants ---

        UpsertTenantDescriptor = $"""
            UPDATE {Schema}.tenants WITH (UPDLOCK, SERIALIZABLE)
               SET display_name = @display_name
             OUTPUT inserted.id, inserted.slug, inserted.display_name, inserted.created_at
             WHERE slug = @slug;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.tenants (id, slug, display_name, created_at)
            OUTPUT inserted.id, inserted.slug, inserted.display_name, inserted.created_at
            VALUES (@id, @slug, @display_name, @created_at);
            """;

        // --- Attachments ---

        const string attachmentColumns = """
            id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256, created_by, created_at
            """;

        SelectAttachments = $"""
            SELECT {attachmentColumns}
            FROM {Schema}.attachments
            WHERE tenant_id = @tenant_id
              AND (@session_id IS NULL OR session_id = @session_id)
              {TakeGuard}
            ORDER BY created_at DESC
            {Paging}
            """;

        DeleteAttachment = $"""
            DELETE FROM {Schema}.attachments
            OUTPUT deleted.external_uri
            WHERE tenant_id = @tenant_id AND id = @id;
            """;

        DeleteAttachmentsBySession = $"""
            DELETE FROM {Schema}.attachments
            OUTPUT deleted.external_uri
            WHERE tenant_id = @tenant_id AND session_id = @session_id;
            """;

        // --- Persistent agent file memory ---

        UpsertAgentFile = $"""
            UPDATE {Schema}.agent_files WITH (UPDLOCK, SERIALIZABLE)
               SET content    = @content,
                   updated_at = @now
             WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @path, @content, @now, @now);
            """;

        // Work Item A: the prefix, depth limit, and glob go down to
        // SQL. SQL Server carries no native regex; the regex_pattern
        // parameter is sent to keep the same call shape as PostgresQueries
        // but is NOT USED here -- the final match is always done client-side
        // with .NET Regex.
        SelectAgentFilesFiltered = $"""
            SELECT path, content
            FROM {Schema}.agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name
              AND path LIKE @prefix_like ESCAPE '\'
              AND (@prefix_deep_like IS NULL OR path NOT LIKE @prefix_deep_like ESCAPE '\')
              AND (@name_like IS NULL OR path LIKE @name_like ESCAPE '\')
            ORDER BY path;
            """;

        // The session workspace is measured by the bytes the column stores. SQL Server keeps text
        // as UTF-16, so DATALENGTH counts two bytes for an ASCII character.
        SelectAgentFileTreeUsage = $"""
            SELECT COUNT(*), COALESCE(SUM(CAST(DATALENGTH(content) AS bigint)), 0)
            FROM {Schema}.agent_files
            WHERE tenant_id = @tenant_id AND path LIKE @prefix_like ESCAPE '\';
            """;

        // --- Workflow'lar ---

        UpsertWorkflow = $"""
            UPDATE {Schema}.workflows WITH (UPDLOCK, SERIALIZABLE)
               SET version    = version + 1,
                   definition = @definition,
                   updated_at = @now
             OUTPUT inserted.version, inserted.updated_at
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.workflows (id, tenant_id, name, version, definition, created_at, updated_at)
            OUTPUT inserted.version, inserted.updated_at
            VALUES (@id, @tenant_id, @name,
                    (SELECT COALESCE(MAX(version), 0) + 1
                       FROM {Schema}.workflow_definition_versions
                      WHERE tenant_id = @tenant_id AND name = @name),
                    @definition, @now, @now);
            """;

        // --- Audit trail ---

        SelectAuditLog = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash, key_id
            FROM {Schema}.audit_log
            WHERE tenant_id = @tenant_id
              AND (@actor          IS NULL OR actor  = @actor)
              AND (@action         IS NULL OR action = @action)
              AND (@entity         IS NULL OR entity = @entity)
              AND (@started_after  IS NULL OR created_at > @started_after)
              AND (@started_before IS NULL OR created_at < @started_before)
              {TakeGuard}
            ORDER BY created_at DESC
            OFFSET 0 ROWS FETCH NEXT (CASE WHEN @take < 1 THEN 1 ELSE @take END) ROWS ONLY;
            """;

        // --- Audit hash chain ---

        SelectLastAuditHash = $"""
            SELECT TOP (1) hash, key_id
            FROM {Schema}.audit_log
            WHERE tenant_id = @tenant_id
            ORDER BY chain_seq DESC;
            """;

        SelectAuditChain = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash, key_id
            FROM {Schema}.audit_log
            WHERE tenant_id = @tenant_id
              AND hash IS NOT NULL
              AND (@started_after  IS NULL OR created_at >= @started_after)
              AND (@started_before IS NULL OR created_at <= @started_before)
            ORDER BY chain_seq;
            """;

        // --- Scheduling and job queue ---

        UpsertJobSchedule = $"""
            UPDATE {Schema}.job_schedules WITH (UPDLOCK, SERIALIZABLE)
               SET handler_key = @handler_key,
                   target_name = @target_name,
                   cron        = @cron,
                   time_zone   = @time_zone,
                   payload     = @payload,
                   enabled     = @enabled,
                   next_run_at = @next_run_at,
                   last_run_at = @last_run_at,
                   updated_at  = @updated_at,
                   lane        = @lane
             OUTPUT inserted.id, inserted.created_by, inserted.created_at
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.job_schedules ({ScheduleColumns})
            OUTPUT inserted.id, inserted.created_by, inserted.created_at
            VALUES (@id, @tenant_id, @name, @handler_key, @target_name, @cron, @time_zone, @payload, @enabled,
                    @next_run_at, @last_run_at, @created_by, @created_at, @updated_at, @lane);
            """;

        // Not scoped by tenant: this query belongs to the worker, not to an
        // HTTP request.
        SelectDueJobSchedules = $"""
            SELECT {ScheduleColumns}
            FROM {Schema}.job_schedules
            WHERE enabled = 1 AND cron IS NOT NULL AND next_run_at IS NOT NULL AND next_run_at <= @as_of;
            """;

        // --- Inbound triggers ---

        UpsertInboundTrigger = $"""
            UPDATE {Schema}.inbound_triggers WITH (UPDLOCK, SERIALIZABLE)
               SET target_kind                        = @target_kind,
                   target_name                         = @target_name,
                   signing_secret_configuration_name   = @signing_secret_configuration_name,
                   payload_mode                        = @payload_mode,
                   payload_path                         = @payload_path,
                   enabled                              = @enabled,
                   updated_at                           = @updated_at
             OUTPUT inserted.id, inserted.created_at
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.inbound_triggers ({InboundTriggerColumns})
            OUTPUT inserted.id, inserted.created_at
            VALUES (@id, @tenant_id, @name, @target_kind, @target_name, @signing_secret_configuration_name,
                    @payload_mode, @payload_path, @enabled, @created_at, @updated_at);
            """;

        // 🚨 SQL Server has no array parameter: instead of `UNNEST(@ids,
        // @inputs) WITH ORDINALITY`, the two JSON arrays are opened with
        // OPENJSON and matched on `[key]` (0-based index).
        InsertJobItems = $"""
            INSERT INTO {Schema}.job_items (id, job_id, seq, input, status)
            SELECT CAST(ids.value AS uniqueidentifier), @job_id, CAST(ids.[key] AS int), inputs.value, 0
            FROM OPENJSON(@ids) AS ids
            JOIN OPENJSON(@inputs) AS inputs ON inputs.[key] = ids.[key];
            """;

        // 🚨 The `FOR UPDATE SKIP LOCKED` counterpart is the `WITH (UPDLOCK,
        // READPAST, ROWLOCK)` hints: UPDLOCK locks the selected row for
        // writing, READPAST SKIPS a row another worker already locked, ROWLOCK
        // keeps the lock at row granularity. The update goes through a CTE;
        // `UPDATE TOP (n)` does not accept ORDER BY and could not guarantee
        // picking the oldest job.
        LeaseJob = $"""
            WITH next_job AS (
                SELECT TOP (1) *
                FROM {Schema}.jobs WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ((status = 0 AND scheduled_for <= @now)
                   OR (status IN (1, 2) AND lease_until < @now))
                  AND (@lanes IS NULL OR EXISTS (SELECT 1 FROM OPENJSON(@lanes) WHERE value = lane))
                ORDER BY scheduled_for
            )
            UPDATE next_job
               SET status      = 1,
                   lease_owner = @owner,
                   lease_until = @lease_until,
                   attempt     = attempt + 1,
                   started_at  = COALESCE(started_at, @now)
             OUTPUT {InsertedJobColumns};
            """;

        // The same statement with a per-tenant ceiling; the rule is the PostgreSQL text's. The
        // tenants at the ceiling are collected into a table variable FIRST, then probed by key.
        //
        // 🚨 Measured on SQL Server 2022 with 100,000 ready jobs: the same set written as an uncorrelated
        // `tenant_id IN (SELECT ... GROUP BY ... HAVING)` was NOT evaluated once. The optimizer ran it per
        // candidate row (100,011 scans of `jobs`, 76.9 million reads, 177 s) where PostgreSQL hashes it.
        // The table variable costs one pass over the live leases and a key seek per candidate (152 ms
        // against 79 ms for the uncapped statement, which already sorts every ready job).
        //
        // The count reads the table under the default isolation, so it waits for a lease another worker is
        // writing at that instant instead of skipping it (READPAST would undercount); only the row being
        // leased takes the UPDLOCK/READPAST hints. The ceiling stays soft for the reason given there.
        LeaseJobWithTenantCap = $"""
            DECLARE @at_cap TABLE (tenant_id nvarchar(200) NOT NULL PRIMARY KEY);

            INSERT INTO @at_cap (tenant_id)
            SELECT held.tenant_id
              FROM {Schema}.jobs AS held
             WHERE held.status IN (1, 2)
               AND held.lease_until >= @now
               AND EXISTS (SELECT 1 FROM OPENJSON(@cap_keys) WHERE value = held.handler_key)
             GROUP BY held.tenant_id
            HAVING COUNT(*) >= @cap_max;

            WITH next_job AS (
                SELECT TOP (1) j.*
                FROM {Schema}.jobs AS j WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ((j.status = 0 AND j.scheduled_for <= @now)
                   OR (j.status IN (1, 2) AND j.lease_until < @now))
                  AND (@lanes IS NULL OR EXISTS (SELECT 1 FROM OPENJSON(@lanes) WHERE value = j.lane))
                  AND NOT (EXISTS (SELECT 1 FROM OPENJSON(@cap_keys) WHERE value = j.handler_key)
                       AND j.tenant_id IN (SELECT tenant_id FROM @at_cap))
                ORDER BY j.scheduled_for
            )
            UPDATE next_job
               SET status      = 1,
                   lease_owner = @owner,
                   lease_until = @lease_until,
                   attempt     = attempt + 1,
                   started_at  = COALESCE(started_at, @now)
             OUTPUT {InsertedJobColumns};
            """;

        SelectJobs = $"""
            SELECT {JobColumns}
            FROM {Schema}.jobs
            WHERE (@tenant_id   IS NULL OR tenant_id   = @tenant_id)
              AND (@handler_key IS NULL OR handler_key = @handler_key)
              AND (@lane        IS NULL OR lane         = @lane)
              AND (@status      IS NULL OR status      = @status)
              AND (@schedule_id IS NULL OR schedule_id = @schedule_id)
              {TakeGuard}
            ORDER BY created_at DESC
            {Paging}
            """;

        // 🚨 T-SQL has no data-modifying CTE; PostgreSQL's
        // `WITH updated AS (UPDATE ... RETURNING)` structure is built by
        // writing to a table variable with OUTPUT. It is idempotent: the
        // item is updated only if it is still Pending (0), so if the same
        // item is reported twice the counters do NOT increment AGAIN.
        ReportJobItem = $"""
            DECLARE @updated TABLE (status smallint);

            UPDATE {Schema}.job_items
               SET status = @status, run_id = @run_id, error = @error
             OUTPUT inserted.status INTO @updated
             WHERE job_id = @job_id AND seq = @seq AND status = 0;

            UPDATE {Schema}.jobs
               SET done_items   = done_items   + (SELECT COUNT(*) FROM @updated WHERE status = 1),
                   failed_items = failed_items + (SELECT COUNT(*) FROM @updated WHERE status = 2)
             WHERE id = @job_id;
            """;

        // --- Degerlendirme (eval) ---

        UpsertEvalSuite = $"""
            UPDATE {Schema}.eval_suites WITH (UPDLOCK, SERIALIZABLE)
               SET description = @description,
                   agent_name  = @agent_name,
                   checks      = @checks,
                   updated_at  = @now
             OUTPUT inserted.id, inserted.tenant_id, inserted.name, inserted.description,
                    inserted.agent_name, inserted.checks, inserted.created_at, inserted.updated_at
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.eval_suites ({SuiteColumns})
            OUTPUT inserted.id, inserted.tenant_id, inserted.name, inserted.description,
                   inserted.agent_name, inserted.checks, inserted.created_at, inserted.updated_at
            VALUES (@id, @tenant_id, @name, @description, @agent_name, @checks, @now, @now);
            """;

        // Same rationale as PostgreSQL's InsertEvalCaseWithComputedSeq
        // MERGE is not used.
        InsertEvalCaseWithComputedSeq = $"""
            INSERT INTO {Schema}.eval_cases
                (id, suite_id, seq, query, expected_output, expected_tool_calls, context,
                 source_run_id, source_kind, promoted_at, parameters)
            OUTPUT inserted.id, inserted.suite_id, inserted.seq, inserted.query,
                   inserted.expected_output, inserted.expected_tools, inserted.context,
                   inserted.source_run_id, inserted.source_kind, inserted.promoted_at, inserted.parameters,
                   inserted.expected_tool_calls
            VALUES
                (@id, @suite_id,
                 ISNULL((SELECT MAX(seq) FROM {Schema}.eval_cases WHERE suite_id = @suite_id), -1) + 1,
                 @query, @expected_output, @expected_tool_calls, @context,
                 @source_run_id, @source_kind, @promoted_at, @parameters);
            """;

        InsertEvalRun = $"""
            INSERT INTO {Schema}.eval_runs
                (id, tenant_id, suite_id, job_id, status, total, passed, failed, started_at)
            OUTPUT inserted.id, inserted.tenant_id, inserted.suite_id, inserted.job_id,
                   inserted.agent_version, inserted.model_id, inserted.status, inserted.total,
                   inserted.passed, inserted.failed, inserted.input_tokens, inserted.output_tokens,
                   inserted.started_at, inserted.completed_at
            VALUES (@id, @tenant_id, @suite_id, @job_id, 0, @total, 0, 0, @started_at);
            """;

        SelectEvalRuns = $"""
            SELECT {EvalRunColumns}
            FROM {Schema}.eval_runs
            WHERE (@tenant_id IS NULL OR tenant_id = @tenant_id)
              AND (@suite_id  IS NULL OR suite_id  = @suite_id)
              {TakeGuard}
            ORDER BY started_at DESC
            {Paging}
            """;

        // --- Kota ---

        const string quotaOutput = """
            inserted.id, inserted.tenant_id, inserted.agent_name, inserted.period, inserted.max_runs,
            inserted.max_tokens, inserted.max_cost, inserted.enabled, inserted.created_at,
            inserted.updated_at
            """;

        UpsertQuota = $"""
            UPDATE {Schema}.quotas WITH (UPDLOCK, SERIALIZABLE)
               SET max_runs   = @max_runs,
                   max_tokens = @max_tokens,
                   max_cost   = @max_cost,
                   enabled    = @enabled,
                   updated_at = @updated_at
             OUTPUT {quotaOutput}
             WHERE tenant_id = @tenant_id
               AND EXISTS (SELECT agent_name INTERSECT SELECT @agent_name)
               AND period = @period;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.quotas ({QuotaColumns})
            OUTPUT {quotaOutput}
            VALUES (@id, @tenant_id, @agent_name, @period, @max_runs, @max_tokens, @max_cost, @enabled,
                    @created_at, @updated_at);
            """;

        SelectQuotas = $"""
            SELECT {QuotaColumns}
            FROM {Schema}.quotas
            WHERE tenant_id = @tenant_id
            ORDER BY ISNULL(agent_name, N''), period;
            """;

        // Consumption is incremented ATOMICALLY. UPDATE ... SET x = x + @y is
        // a single statement; no increment is lost across concurrent runs.
        AddQuotaUsage = $"""
            UPDATE {Schema}.quota_usage WITH (UPDLOCK, SERIALIZABLE)
               SET runs       = runs   + @runs,
                   tokens     = tokens + @tokens,
                   cost       = cost   + @cost,
                   updated_at = @updated_at
             WHERE tenant_id = @tenant_id AND agent_name = @agent_name
               AND period = @period AND period_start = @period_start;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.quota_usage
                (tenant_id, agent_name, period, period_start, runs, tokens, cost, updated_at)
            VALUES
                (@tenant_id, @agent_name, @period, @period_start, @runs, @tokens, @cost, @updated_at);
            """;

        // For the ',key1,key2,' delimiter rationale see PostgreSQL's
        // TryClaimQuotaThresholdNotification. UPDLOCK/SERIALIZABLE matches the
        // AddQuotaUsage pattern above -- two workers racing the same threshold
        // must not both read "not present" before either writes.
        TryClaimQuotaThresholdNotification = $"""
            UPDATE {Schema}.quota_usage WITH (UPDLOCK, SERIALIZABLE)
               SET notified_thresholds = COALESCE(notified_thresholds, ',') + @key + ','
             WHERE tenant_id = @tenant_id AND agent_name = @agent_name
               AND period = @period AND period_start = @period_start
               AND (notified_thresholds IS NULL OR notified_thresholds NOT LIKE '%,' + @key + ',%');
            """;

        // --- Webhook ---

        const string webhookSubscriptionOutput = """
            inserted.id, inserted.tenant_id, inserted.name, inserted.url, inserted.events,
            inserted.secret_configuration_key, inserted.headers, inserted.enabled,
            inserted.consecutive_failures, inserted.created_at, inserted.updated_at,
            inserted.header_configuration_keys
            """;

        UpsertWebhookSubscription = $"""
            UPDATE {Schema}.webhook_subscriptions WITH (UPDLOCK, SERIALIZABLE)
               SET url                      = @url,
                   events                   = @events,
                   secret_configuration_key = @secret_configuration_key,
                   headers                   = @headers,
                   enabled                   = @enabled,
                   consecutive_failures      = @consecutive_failures,
                   updated_at                = @updated_at,
                   header_configuration_keys = @header_configuration_keys
             OUTPUT {webhookSubscriptionOutput}
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.webhook_subscriptions ({WebhookSubscriptionColumns})
            OUTPUT {webhookSubscriptionOutput}
            VALUES (@id, @tenant_id, @name, @url, @events, @secret_configuration_key, @headers, @enabled,
                    @consecutive_failures, @created_at, @updated_at, @header_configuration_keys);
            """;

        // 🚨 `events` is a JSON array; the counterpart of PostgreSQL's
        // `@event_type = ANY(events)` is an EXACT match over OPENJSON. A
        // LIKE-based search would wrongly match a 'run.completed.v2'
        // subscription while searching for 'run.completed'.
        SelectWebhookSubscriptionsForEvent = $"""
            SELECT {WebhookSubscriptionColumns}
            FROM {Schema}.webhook_subscriptions
            WHERE tenant_id = @tenant_id
              AND enabled = 1
              AND EXISTS (SELECT 1 FROM OPENJSON(events) WHERE value = @event_type)
            ORDER BY name;
            """;

        // The consecutive-failure counter and the auto-disable happen in a
        // SINGLE statement. The returned row answers "did this call disable
        // the subscription".
        UpdateWebhookSubscriptionOutcome = $"""
            UPDATE {Schema}.webhook_subscriptions
               SET consecutive_failures = CASE WHEN @succeeded = 1 THEN 0 ELSE consecutive_failures + 1 END,
                   enabled = CASE
                       WHEN @succeeded = 1 THEN enabled
                       WHEN @threshold > 0 AND consecutive_failures + 1 >= @threshold THEN 0
                       ELSE enabled
                   END,
                   updated_at = @updated_at
             OUTPUT CASE WHEN inserted.enabled = 0 AND @succeeded = 0
                         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
             WHERE id = @id;
            """;

        SelectWebhookDeliveries = $"""
            SELECT {WebhookDeliveryColumns}
            FROM {Schema}.webhook_deliveries
            WHERE tenant_id = @tenant_id
              AND (@subscription_id IS NULL OR subscription_id = @subscription_id)
              AND (@status          IS NULL OR status          = @status)
              {TakeGuard}
            ORDER BY created_at DESC
            {Paging}
            """;

        // --- Tenant-scoped API keys ---

        InsertApiKey = $"""
            INSERT INTO {Schema}.api_keys ({ApiKeyColumns})
            VALUES (@id, @tenant_id, @name, @key_hash, @key_prefix, @scopes, @expires_at, @revoked_at,
                    @last_used_at, @created_at);
            """;

        // Setup health check (ExternalSurfaceGuard): the tenant
        // filter is DELIBERATELY absent. scopes is a JSON array.
        HasApiKeyWithScope = $"""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM {Schema}.api_keys
                WHERE revoked_at IS NULL
                  AND (expires_at IS NULL OR expires_at > @now)
                  AND EXISTS (SELECT 1 FROM OPENJSON(scopes) WHERE value = @scope)
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
            """;

        const string retentionPolicyOutput = """
            inserted.id, inserted.tenant_id, inserted.target, inserted.max_age_days, inserted.max_rows,
            inserted.archive, inserted.enabled, inserted.created_at, inserted.updated_at
            """;

        // Two-branch upsert pattern: when UPDATE affects 0 rows, the
        // real row is in the SECOND result set; DbHelpers.ReadSingleAsync
        // steps into it with NextResultAsync.
        UpsertRetentionPolicy = $"""
            UPDATE {Schema}.retention_policies WITH (UPDLOCK, SERIALIZABLE)
               SET max_age_days = @max_age_days,
                   max_rows     = @max_rows,
                   archive      = @archive,
                   enabled      = @enabled,
                   updated_at   = @updated_at
             OUTPUT {retentionPolicyOutput}
             WHERE tenant_id = @tenant_id
               AND target    = @target;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.retention_policies
                ({RetentionPolicyColumns})
             OUTPUT {retentionPolicyOutput}
            VALUES
                (@id, @tenant_id, @target, @max_age_days, @max_rows, @archive, @enabled, @created_at, @updated_at);
            """;

        SelectRetentionRuns = $"""
            SELECT {RetentionRunColumns}
            FROM {Schema}.retention_runs
            WHERE tenant_id = @tenant_id
              AND (@target IS NULL OR target = @target)
              {TakeGuard}
            ORDER BY started_at DESC
            {Paging}
            """;
        const string voiceSessionColumns =
            "id, tenant_id, session_id, agent_name, started_at, ended_at, turns, input_seconds, output_chars, end_reason, created_by, "
            + "provider, model, live_seconds, duration_cost, character_cost, currency";

        // 🚨 MERGE IS NOT USED: first a locked UPDATE, then INSERT if no row exists.
        UpsertVoiceSession = $"""
            UPDATE {Schema}.voice_sessions WITH (UPDLOCK, SERIALIZABLE)
               SET ended_at       = @ended_at,
                   turns          = @turns,
                   input_seconds  = @input_seconds,
                   output_chars   = @output_chars,
                   end_reason     = @end_reason,
                   provider       = @provider,
                   model          = @model,
                   live_seconds   = @live_seconds,
                   duration_cost  = @duration_cost,
                   character_cost = @character_cost,
                   currency       = @currency
             WHERE id = @id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.voice_sessions
                ({voiceSessionColumns})
            VALUES
                (@id, @tenant_id, @session_id, @agent_name, @started_at, @ended_at, @turns, @input_seconds, @output_chars, @end_reason, @created_by,
                 @provider, @model, @live_seconds, @duration_cost, @character_cost, @currency);
            """;

        SelectVoiceSessions = $"""
            SELECT {voiceSessionColumns}
            FROM {Schema}.voice_sessions
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@session_id IS NULL OR session_id = @session_id)
              {TakeGuard}
            ORDER BY started_at DESC
            {Paging}
            """;

        // -------------------------------------------------------------------
        // Run/message score
        // -------------------------------------------------------------------

        const string runScoreOutput =
            "inserted.id, inserted.tenant_id, inserted.run_id, inserted.message_id, inserted.kind, " +
            "inserted.value, inserted.comment, inserted.source, inserted.author, inserted.created_at, " +
            "inserted.name, inserted.text_value, inserted.evaluator_version";

        // 🚨 MERGE IS NOT USED: first a locked UPDATE, then INSERT if
        // no row exists.
        //
        // 🚨 NULL uniqueness works in REVERSE here: the
        // `author = @author` comparison ALWAYS returns UNKNOWN when @author
        // is NULL (an identity-less setup) -- no row matches and the flow
        // falls through to INSERT. This is DELIBERATE: the uniqueness index
        // is filtered with `WHERE author IS NOT NULL` for the same reason
        // (migration 0005).
        //
        // 🚨 message_key, NOT ISNULL(message_id, N''), closes a defect.
        // Wrapping the COLUMN in
        // ISNULL made the predicate non-sargable, so WITH (UPDLOCK,
        // SERIALIZABLE) could not range-lock `run_scores_target_author_name_idx`
        // and two concurrent upserts of the same target both inserted.
        // `message_key` (migration 0037) is a PERSISTED computed column that
        // carries the SAME normalization as a plain, indexed value -- the
        // predicate wraps only the PARAMETER, which does not affect sargability.
        UpsertRunScore = $"""
            UPDATE {Schema}.run_scores WITH (UPDLOCK, SERIALIZABLE)
               SET kind              = @kind,
                   value             = @value,
                   text_value        = @text_value,
                   comment           = @comment,
                   source            = @source,
                   created_at        = @created_at,
                   evaluator_version = @evaluator_version
             OUTPUT {runScoreOutput}
             WHERE tenant_id = @tenant_id
               AND run_id = @run_id
               AND message_key = ISNULL(@message_id, N'')
               AND author = @author
               AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.run_scores ({RunScoreColumns})
            OUTPUT {runScoreOutput}
            VALUES (@id, @tenant_id, @run_id, @message_id, @kind, @value, @comment, @source, @author, @created_at, @name, @text_value, @evaluator_version);
            """;

        // -------------------------------------------------------------------
        // Score summary (a query, not a counter; 154.1)
        // -------------------------------------------------------------------

        // Shared by every breakdown AND the series query below. `prefix` is
        // the table alias with its trailing dot ("s." for the joined ByAgent
        // query), or "" for the single-table ones.
        static string ScoreFilter(string prefix) => $"""
              AND (@from_ts IS NULL OR {prefix}created_at >= @from_ts)
              AND (@to_ts IS NULL OR {prefix}created_at < @to_ts)
              AND (@score_name IS NULL OR {prefix}name = @score_name)
              AND (@source IS NULL OR {prefix}source = @source)
              AND (@author IS NULL OR {prefix}author = @author)
              AND (@target = 0
                   OR (@target = 1 AND ({prefix}message_id IS NULL OR {prefix}message_id = N''))
                   OR (@target = 2 AND {prefix}message_id IS NOT NULL AND {prefix}message_id <> N''))
            """;

        // The ByAgent query joins `runs` directly, so its agent filter is a
        // plain column comparison; the other four have no such join and reach
        // the agent name through a subquery instead.
        var scoreAgentFilterViaSubquery = $"""
              AND (@agent_name IS NULL OR run_id IN (SELECT id FROM {Schema}.runs WHERE tenant_id = @tenant_id AND agent_name = @agent_name))
            """;

        SelectRunScoreSummary = $"""
            SELECT TOP (@max_rows)
                   name,
                   kind,
                   CAST(COUNT(*) AS bigint),
                   CAST(COUNT(*) - COUNT(value) AS bigint),
                   AVG(value),
                   MIN(value),
                   MAX(value)
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id
            {ScoreFilter(string.Empty)}
            {scoreAgentFilterViaSubquery}
            GROUP BY name, kind
            ORDER BY COUNT(*) DESC, name;

            -- Second result set: this breakdown's category counts. Fetched
            -- UNBOUNDED (no TOP here) and capped per name in code, because the
            -- cap is PER GROUP, not over the whole result set.
            SELECT name,
                   text_value,
                   CAST(COUNT(*) AS bigint)
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id
              AND kind = @kind_categorical
              AND text_value IS NOT NULL
            {ScoreFilter(string.Empty)}
            {scoreAgentFilterViaSubquery}
            GROUP BY name, text_value
            ORDER BY name, COUNT(*) DESC;

            -- Third result set: breakdown by author. Scores with no author
            -- (an identity-less setup) are excluded.
            SELECT TOP (@max_rows)
                   author,
                   kind,
                   CAST(COUNT(*) AS bigint),
                   CAST(COUNT(*) - COUNT(value) AS bigint),
                   AVG(value),
                   MIN(value),
                   MAX(value)
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id
              AND author IS NOT NULL AND author <> N''
            {ScoreFilter(string.Empty)}
            {scoreAgentFilterViaSubquery}
            GROUP BY author, kind
            ORDER BY COUNT(*) DESC, author;

            -- Fourth result set: breakdown by source.
            SELECT TOP (@max_rows)
                   source,
                   kind,
                   CAST(COUNT(*) AS bigint),
                   CAST(COUNT(*) - COUNT(value) AS bigint),
                   AVG(value),
                   MIN(value),
                   MAX(value)
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id
            {ScoreFilter(string.Empty)}
            {scoreAgentFilterViaSubquery}
            GROUP BY source, kind
            ORDER BY COUNT(*) DESC, source;

            -- Fifth result set: breakdown by the agent that produced the
            -- scored run (a score itself carries no agent name).
            SELECT TOP (@max_rows)
                   r.agent_name,
                   s.kind,
                   CAST(COUNT(*) AS bigint),
                   CAST(COUNT(*) - COUNT(s.value) AS bigint),
                   AVG(s.value),
                   MIN(s.value),
                   MAX(s.value)
            FROM {Schema}.run_scores AS s
            JOIN {Schema}.runs AS r ON r.tenant_id = s.tenant_id AND r.id = s.run_id
            WHERE s.tenant_id = @tenant_id
            {ScoreFilter("s.")}
              AND (@agent_name IS NULL OR r.agent_name = @agent_name)
            GROUP BY r.agent_name, s.kind
            ORDER BY COUNT(*) DESC, r.agent_name;
            """;

        // 1900-01-01 was a Monday, so DATEDIFF(day, 0, x) % 7 gives the number
        // of days since the preceding Monday directly (ISO 8601), without a
        // locale-dependent DATEFIRST/DATEPART(weekday, ...) computation --
        // matching RunScoreBucketing.Truncate.
        static string BucketExpr(string prefix) => $"""
            CASE @bucket_unit
                WHEN N'hour' THEN DATEADD(hour, DATEDIFF(hour, 0, CONVERT(datetime2(7), {prefix}created_at)), CONVERT(datetime2(7), '1900-01-01'))
                WHEN N'day'  THEN DATEADD(day,  DATEDIFF(day,  0, CONVERT(datetime2(7), {prefix}created_at)), CONVERT(datetime2(7), '1900-01-01'))
                ELSE DATEADD(day,
                             DATEDIFF(day, 0, CONVERT(datetime2(7), {prefix}created_at)) - DATEDIFF(day, 0, CONVERT(datetime2(7), {prefix}created_at)) % 7,
                             CONVERT(datetime2(7), '1900-01-01'))
            END
            """;

        // A single result set, run only when RunScoreQuery.Bucket is given
        // (154: no cost when it is not). Buckets with no
        // matching score are NOT produced -- a sparse series, unlike
        // SelectRunTimeSeries.
        SelectRunScoreSeries = $"""
            SELECT TODATETIMEOFFSET({BucketExpr(string.Empty)}, 0),
                   name,
                   kind,
                   CAST(COUNT(*) AS bigint),
                   CAST(COUNT(*) - COUNT(value) AS bigint),
                   AVG(value),
                   MIN(value),
                   MAX(value)
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id
            {ScoreFilter(string.Empty)}
            {scoreAgentFilterViaSubquery}
            GROUP BY {BucketExpr(string.Empty)}, name, kind
            ORDER BY 1, name;
            """;

        // MERGE is not used. Two-branch pattern: first UPDATE (if we
        // are the owner or the lease expired), then INSERT only if the row is
        // absent. Both branches return the same single column (name) the
        // same way; DbHelpers.ExecuteScalarAsync falls through to the second
        // result set by itself against the multi-result-set trap.
        AcquireSingletonLease = $"""
            DECLARE @updated int;

            UPDATE {Schema}.singleton_leases WITH (UPDLOCK, SERIALIZABLE)
               SET owner_id = @owner_id, expires_at = @expires_at, updated_at = @now
             OUTPUT inserted.name
             WHERE name = @name AND (owner_id = @owner_id OR expires_at < @now);

            SET @updated = @@ROWCOUNT;

            IF @updated = 0 AND NOT EXISTS (
                SELECT 1 FROM {Schema}.singleton_leases WITH (UPDLOCK, SERIALIZABLE)
                 WHERE name = @name)
            INSERT INTO {Schema}.singleton_leases (name, owner_id, expires_at, updated_at)
            OUTPUT inserted.name
            VALUES (@name, @owner_id, @expires_at, @now);
            """;

        // Rate-limit window counter. MERGE is not used: the two-branch
        // pattern of AcquireSingletonLease, inside the transaction ExecuteUpsertAsync opens.
        // Every column reference on the right of SET reads the row as it was BEFORE the
        // update, so the two CASE expressions see the same stored window. A later window
        // restarts the count; an earlier one is counted in the stored window and does not
        // move it back. Both branches return the same two columns; DbHelpers.ReadSingleAsync
        // falls through to the second result set when the UPDATE touched no row.
        IncrementRateLimitCounter = $"""
            DECLARE @updated int;

            UPDATE {Schema}.rate_limit_counters WITH (UPDLOCK, SERIALIZABLE)
               SET permit_count = CASE WHEN @window_start > window_start THEN 1 ELSE permit_count + 1 END,
                   window_start = CASE WHEN @window_start > window_start THEN @window_start ELSE window_start END
            OUTPUT inserted.window_start, inserted.permit_count
             WHERE partition_key = @partition_key;

            SET @updated = @@ROWCOUNT;

            IF @updated = 0 AND NOT EXISTS (
                SELECT 1 FROM {Schema}.rate_limit_counters WITH (UPDLOCK, SERIALIZABLE)
                 WHERE partition_key = @partition_key)
            INSERT INTO {Schema}.rate_limit_counters (partition_key, window_start, permit_count)
            OUTPUT inserted.window_start, inserted.permit_count
            VALUES (@partition_key, @window_start, 1);
            """;

        // Plain INSERT: a uniqueness violation is caught with
        // SqlDialect.IsUniqueViolation, the caller reads the existing row with
        // SelectIdempotencyKey.
        // 🚨 `key` is a RESERVED word; it is bracket-quoted as [key].
        InsertIdempotencyKey = $"""
            INSERT INTO {Schema}.idempotency_keys (tenant_id, [key], fingerprint, state, created_at)
            VALUES (@tenant_id, @key, @fingerprint, 0, @created_at);
            """;

        SelectIdempotencyKey = $"""
            SELECT state, fingerprint, status_code, content_type, body, run_id, headers
              FROM {Schema}.idempotency_keys
             WHERE tenant_id = @tenant_id AND [key] = @key;
            """;

        CompleteIdempotencyKey = $"""
            UPDATE {Schema}.idempotency_keys
               SET state = 2, status_code = @status_code, content_type = @content_type,
                   body = @body, run_id = @run_id, headers = @headers, completed_at = @completed_at
             WHERE tenant_id = @tenant_id AND [key] = @key;
            """;

        DeleteIdempotencyKey = $"""
            DELETE FROM {Schema}.idempotency_keys WHERE tenant_id = @tenant_id AND [key] = @key;
            """;

        // The SAME pattern as ClaimOrphanedRuns: closed rows are read via
        // OUTPUT. There is NO tenant filter -- this is a maintenance
        // operation.
        ExpirePendingApprovals = $"""
            UPDATE a
               SET status = @status_expired
            OUTPUT inserted.id, inserted.tenant_id, inserted.run_id, inserted.session_id,
                   inserted.request_id, inserted.tool_name, inserted.arguments, inserted.status,
                   inserted.decided_by, inserted.decided_at, inserted.expires_at, inserted.created_at,
                   inserted.presentation
              FROM {Schema}.pending_approvals AS a
             WHERE a.id IN (
                       SELECT TOP (@max) id FROM {Schema}.pending_approvals
                        WHERE status = @status_pending AND expires_at < @older_than
                     ORDER BY expires_at ASC
                   );
            """;

        // The workflow request inbox (see PostgreSQL for the pattern). SQL Server has
        // no row-value IN, so the candidates are a CTE joined back on the key. take >= 1 is
        // guaranteed by the store, which cuts it to 1..200.
        SelectWorkflowRequests = BuildSelectWorkflowRequests("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;");

        SelectStrandedWorkflowRequests = BuildSelectStrandedWorkflowRequests("TOP (@max) ", ";");

        ExpireWorkflowRequests = $"""
            WITH due AS (
                SELECT TOP (@max) run_id, request_id FROM {Schema}.workflow_requests
                 WHERE status = @status_open AND expires_at IS NOT NULL AND expires_at <= @now
                 ORDER BY expires_at ASC
            )
            UPDATE w
               SET status = @status_closed, closed_at = @now
            OUTPUT inserted.run_id, inserted.request_id, inserted.tenant_id, inserted.session_id,
                   inserted.workflow_name, inserted.port_id, inserted.event_seq, inserted.requested_at,
                   inserted.expires_at, inserted.status, inserted.answered_at,
                   inserted.last_response_run_id, inserted.closed_at
              FROM {Schema}.workflow_requests AS w
              JOIN due ON due.run_id = w.run_id AND due.request_id = w.request_id
             WHERE w.status = @status_open;
            """;

        // -------------------------------------------------------------------
        // Tenant provider bindings (BYOK) and egress policy
        // -------------------------------------------------------------------

        // Two-branch upsert (no MERGE). Neither branch needs OUTPUT --
        // UpsertAsync returns no value -- so the "OUTPUT lands
        // in the second result set" pitfall does not apply here.
        UpsertTenantProviderBinding = $"""
            UPDATE {Schema}.tenant_provider_bindings WITH (UPDLOCK, SERIALIZABLE)
               SET api_key_configuration_name = @api_key_configuration_name,
                   endpoint = @endpoint,
                   updated_at = @updated_at
             WHERE tenant_id = @tenant_id AND provider_name = @provider_name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.tenant_provider_bindings ({TenantProviderBindingColumns})
            VALUES (@tenant_id, @provider_name, @api_key_configuration_name, @endpoint, @updated_at);
            """;

        UpsertTenantEgressPolicy = $"""
            UPDATE {Schema}.tenant_egress_policies WITH (UPDLOCK, SERIALIZABLE)
               SET allowed_providers = @allowed_providers,
                   updated_at = @updated_at
             WHERE tenant_id = @tenant_id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES (@tenant_id, @allowed_providers, @updated_at);
            """;

        UpsertTenantToolPolicy = $"""
            UPDATE {Schema}.tenant_tool_policies WITH (UPDLOCK, SERIALIZABLE)
               SET allowed_tools = @allowed_tools,
                   granted_permissions = @granted_permissions,
                   updated_at = @updated_at
             WHERE tenant_id = @tenant_id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.tenant_tool_policies (tenant_id, allowed_tools, granted_permissions, updated_at)
            VALUES (@tenant_id, @allowed_tools, @granted_permissions, @updated_at);
            """;

    }
}
