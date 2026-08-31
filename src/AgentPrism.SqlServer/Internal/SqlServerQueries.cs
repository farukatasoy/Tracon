namespace AgentPrism;

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
/// same time.
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
    /// <exception cref="AgentPrismException">The schema name is not a valid identifier.</exception>
    public SqlServerQueries(string schemaName)
        : base(schemaName)
    {
        // CREATE SCHEMA must be the FIRST statement of a batch; conditional
        // execution is therefore wrapped in EXEC.
        CreateSchema = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{Schema}')
                EXEC(N'CREATE SCHEMA {Schema};');
            """;

        // set_name (phase 67): each migration SET numbers its own files from
        // 0001, so id alone is no longer unique — the primary key is
        // (set_name, id). A fresh database gets this shape directly; an
        // existing one (created before phase 67) is upgraded by
        // UpgradeMigrationsTable below — NOT a numbered migration file
        // (K-475: it would collide with InsertMigration's fixed text, see
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
        // (both statements no-op there). An existing pre-phase-67 table gets
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

        UpsertAgentDefinition = $"""
            UPDATE {Schema}.agent_definitions WITH (UPDLOCK, SERIALIZABLE)
               SET version    = version + 1,
                   definition = @definition,
                   updated_at = @now
             OUTPUT inserted.id, inserted.version
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.agent_definitions (id, tenant_id, name, version, definition, created_at, updated_at)
            OUTPUT inserted.id, inserted.version
            VALUES (@id, @tenant_id, @name, 1, @definition, @now, @now);
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
            id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at
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
        // COALESCE-based expression index is not needed here (K-184). The
        // match is still written with ISNULL: a plain `= ` comparison would
        // return UNKNOWN when @script_name is NULL.
        UpsertSkillScriptGrant = $"""
            UPDATE {Schema}.skill_script_grants WITH (UPDLOCK, SERIALIZABLE)
               SET granted_by = @granted_by,
                   granted_at = @granted_at,
                   expires_at = @expires_at,
                   revoked_at = NULL
             OUTPUT inserted.id, inserted.tenant_id, inserted.skill_name, inserted.script_name,
                    inserted.granted_by, inserted.granted_at, inserted.expires_at, inserted.revoked_at
             WHERE tenant_id = @tenant_id
               AND skill_name = @skill_name
               AND ISNULL(script_name, N'') = ISNULL(@script_name, N'');

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.skill_script_grants ({grantColumns})
            OUTPUT inserted.id, inserted.tenant_id, inserted.skill_name, inserted.script_name,
                   inserted.granted_by, inserted.granted_at, inserted.expires_at, inserted.revoked_at
            VALUES (@id, @tenant_id, @skill_name, @script_name, @granted_by, @granted_at, @expires_at, NULL);
            """;

        // A grant is NOT DELETED, it is revoked.
        RevokeSkillScriptGrant = $"""
            UPDATE {Schema}.skill_script_grants
               SET revoked_at = @revoked_at
             WHERE tenant_id = @tenant_id
               AND skill_name = @skill_name
               AND ISNULL(script_name, N'') = ISNULL(@script_name, N'')
               AND revoked_at IS NULL;
            """;

        // --- Sessions ---

        // See PostgresQueries: `version` advances, it is never taken from
        // the incoming row.
        UpsertSession = $"""
            UPDATE {Schema}.sessions WITH (UPDLOCK, SERIALIZABLE)
               SET agent_name     = @agent_name,
                   state          = @state,
                   schema_version = @schema_version,
                   updated_at     = @updated_at,
                   version        = version + 1
             WHERE id = @id AND tenant_id = @tenant_id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.sessions (id, tenant_id, agent_name, state, schema_version, created_at, updated_at, version)
            VALUES (@id, @tenant_id, @agent_name, @state, @schema_version, @created_at, @updated_at, 1);
            """;

        SelectSessions = $"""
            SELECT id, agent_name, state, schema_version, created_at, updated_at, tenant_id, version
            FROM {Schema}.sessions
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              {TakeGuard}
            ORDER BY updated_at DESC
            {Paging}
            """;

        // --- Runs ---

        // 🚨 Phase 46: this is an UPSERT (UPDATE-then-INSERT pattern, the same
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
                   continued_from_run_id = @continued_from_run_id,
                   -- 🚨 COALESCE, not a plain overwrite. This is an UPSERT (phase
                   -- 46): a queued run's placeholder row is written during the HTTP
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
            INSERT INTO {Schema}.runs (id, tenant_id, agent_name, session_id, model_id, status, started_at, is_streaming, event_count,
                                       parent_run_id, root_run_id, depth, kind, workflow_name, agent_version, experiment_id, variant,
                                       replay_of_run_id, user_id, labels, continued_from_run_id)
            OUTPUT inserted.user_id, inserted.labels
            VALUES (@id, @tenant_id, @agent_name, @session_id, @model_id, @status, @started_at, @is_streaming, 0,
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
                   error_type     = @error_type,
                   error_message  = @error_message,
                   error_class    = @error_class,
                   error_fingerprint = @error_fingerprint,
                   input_cost     = @input_cost,
                   output_cost    = @output_cost,
                   cached_input_cost = @cached_input_cost,
                   cost_currency  = @cost_currency,
                   pricing_source = @pricing_source,
                   model_id       = COALESCE(@model_id, model_id)
             WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        UpdateRunCost = $"""
            UPDATE {Schema}.runs
               SET input_cost     = @input_cost,
                   output_cost    = @output_cost,
                   cached_input_cost = @cached_input_cost,
                   cost_currency  = @cost_currency,
                   pricing_source = @pricing_source
             WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        // Orphaned run reconciliation (Phase 54). Only affects Running rows;
        // silently updates zero rows for an id that does not exist or is in
        // a different status.
        TouchRunHeartbeat = $"""
            UPDATE {Schema}.runs
               SET heartbeat_at = @at
             WHERE id = @id AND status = @status_running;
            """;

        // TOP (@n) = 0 does NOT raise an error (unlike SQL Server's FETCH
        // NEXT); no extra guard is needed. error_fingerprint is a FIXED
        // string ("orphaned") -- the rationale is the same as in the
        // PostgreSQL version.
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
                   inserted.error_class, inserted.error_fingerprint, inserted.continued_from_run_id
              FROM {Schema}.runs AS r
             WHERE r.id IN (
                       SELECT TOP (@max) id FROM {Schema}.runs
                        WHERE status = @status_running
                          AND COALESCE(heartbeat_at, started_at) < @stale_before
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
                       MAX(sub.cost_currency)                              AS cost_currency,
                       CAST(COALESCE(SUM(CASE WHEN sub.pricing_source = 2 THEN 1 ELSE 0 END), 0) AS bigint) AS unknown_pricing_rows,
                       CAST(COUNT(sub.pricing_source) AS bigint)           AS pricing_rows
                FROM {Schema}.runs AS sub
                WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id
            ) AS tree
            """;

        // Phase 68 attribution filter, written ONCE and reused by the run list
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
            r.continued_from_run_id
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
                    -- HATA-S2-001: session_id is set only on the ROOT run
                    -- (K-217); a child run's own session_id is NULL. Direct
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

        // scored_runs/positive_rate (Phase 31): matchedRunScoresFilter repeats
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
                   MAX(cost_currency),
                   CAST(COALESCE(SUM(CASE WHEN pricing_source = @pricing_source_unknown THEN 1 ELSE 0 END), 0) AS bigint),
                   (SELECT CAST(COUNT(DISTINCT rs.run_id) AS bigint) FROM {matchedRunScoresFilter}),
                   (SELECT CASE WHEN COALESCE(SUM(CASE WHEN rs.kind = @kind_binary THEN 1 ELSE 0 END), 0) = 0 THEN NULL
                                ELSE CAST(COALESCE(SUM(CASE WHEN rs.kind = @kind_binary AND rs.value = 1 THEN 1 ELSE 0 END), 0) AS float)
                                     / SUM(CASE WHEN rs.kind = @kind_binary THEN 1 ELSE 0 END)
                           END
                    FROM {matchedRunScoresFilter}),
                   -- Ordinals 14-17, APPENDED so the reader's fixed positions
                   -- above do not move. These four are counted INSIDE the
                   -- input/output totals and are reported beside them.
                   CAST({TreeSum("cached_input_tokens", null, true)} AS bigint),
                   CAST({TreeSum("reasoning_tokens", null, true)} AS bigint),
                   CAST({TreeSum("audio_input_tokens", null, true)} AS bigint),
                   CAST({TreeSum("audio_output_tokens", null, true)} AS bigint)
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

            -- Fifth result set: error class breakdown (Phase 44). Rows with a
            -- NULL error_class (written before the error class column existed)
            -- fall into the Unknown (0) bucket -- K-014 does not backfill.
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
            
            -- Seventh result set: breakdown by user (phase 68). Runs carrying no
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

        // --- Conversation branching (Phase 47) ---
        // See PostgresQueries for the rationale and the meaning of the columns.

        // --- Run inputs (Phase 47) ---
        // 🚨 A second write is ignored; T-SQL has no ON CONFLICT, so the
        // condition is written with NOT EXISTS (K-177: MERGE is not used).
        InsertRunInput = $"""
            INSERT INTO {Schema}.run_inputs (run_id, tenant_id, messages, created_at)
            SELECT @run_id, @tenant_id, @messages, @created_at
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
                WHERE tenant_id = @tenant_id AND kind = @score_kind_numeric AND message_id IS NULL
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
                   MAX(r.cost_currency),
                   AVG(s.avg_score)
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
                WHERE tenant_id = @tenant_id
                  AND started_at >= @from_ts AND started_at < @to_ts
                  AND (@agent_name IS NULL OR agent_name = @agent_name)
                  AND (@model_id   IS NULL OR model_id   = @model_id)
                  AND (@kind       IS NULL OR kind       = @kind)
                GROUP BY CASE WHEN @bucket_unit = N'hour'
                              THEN DATEADD(hour, DATEDIFF(hour, 0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), '1900-01-01'))
                              ELSE DATEADD(day,  DATEDIFF(day,  0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), '1900-01-01'))
                         END
            )
            SELECT TODATETIMEOFFSET(buckets.bucket, 0),
                   COALESCE(matched.runs, 0),
                   COALESCE(matched.failed_runs, 0),
                   COALESCE(matched.input_tokens, 0),
                   COALESCE(matched.output_tokens, 0),
                   matched.cost,
                   matched.avg_duration_ms
            FROM buckets
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
        // reads argument_conditions by fixed ordinal 7 (docs/hafiza/postgresql.md's
        // ordinal-position lesson applies equally here).
        const string approvalColumns = """
            id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at, argument_conditions
            """;

        // A second rule is not opened for the same scope; the existing record
        // is returned. UPDATE deliberately replaces a column with itself: the
        // goal is not to write, but to return the existing row via OUTPUT.
        // conditions_hash (Phase 63) joined the WHERE match: a data-rule condition
        // set is bound by the same "no unbounded duplicates" rule as arguments_hash.
        InsertToolApprovalRule = $"""
            UPDATE {Schema}.tool_approval_rules WITH (UPDLOCK, SERIALIZABLE)
               SET tool_name = tool_name
             OUTPUT inserted.id, inserted.tenant_id, inserted.agent_name, inserted.tool_name,
                    inserted.arguments_hash, inserted.created_by, inserted.created_at, inserted.argument_conditions
             WHERE tenant_id = @tenant_id
               AND ISNULL(agent_name, N'') = ISNULL(@agent_name, N'')
               AND tool_name = @tool_name
               AND ISNULL(arguments_hash, N'') = ISNULL(@arguments_hash, N'')
               AND ISNULL(conditions_hash, N'') = ISNULL(@conditions_hash, N'');

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.tool_approval_rules ({approvalColumns}, conditions_hash)
            OUTPUT inserted.id, inserted.tenant_id, inserted.agent_name, inserted.tool_name,
                   inserted.arguments_hash, inserted.created_by, inserted.created_at, inserted.argument_conditions
            VALUES (@id, @tenant_id, @agent_name, @tool_name, @arguments_hash, @created_by, @created_at, @argument_conditions, @conditions_hash);
            """;

        // --- MCP servers ---

        const string mcpServerOutput = """
            inserted.id, inserted.tenant_id, inserted.name, inserted.description, inserted.endpoint,
            inserted.transport, inserted.authorization_configuration_key, inserted.headers,
            inserted.enabled, inserted.requires_approval, inserted.created_at, inserted.updated_at,
            inserted.oauth_enabled, inserted.oauth_client_id,
            inserted.oauth_client_secret_configuration_key, inserted.oauth_scopes,
            inserted.oauth_authorization_mode
            """;

        UpsertMcpServer = $"""
            UPDATE {Schema}.mcp_servers WITH (UPDLOCK, SERIALIZABLE)
               SET description                           = @description,
                   endpoint                              = @endpoint,
                   transport                             = @transport,
                   authorization_configuration_key       = @authorization_configuration_key,
                   headers                               = @headers,
                   enabled                               = @enabled,
                   requires_approval                     = @requires_approval,
                   updated_at                            = @now,
                   oauth_enabled                         = @oauth_enabled,
                   oauth_client_id                       = @oauth_client_id,
                   oauth_client_secret_configuration_key = @oauth_client_secret_configuration_key,
                   oauth_scopes                          = @oauth_scopes,
                   oauth_authorization_mode              = @oauth_authorization_mode
             OUTPUT {mcpServerOutput}
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.mcp_servers ({McpServerColumns})
            OUTPUT {mcpServerOutput}
            VALUES (@id, @tenant_id, @name, @description, @endpoint, @transport,
                    @authorization_configuration_key, @headers, @enabled, @requires_approval, @now, @now,
                    @oauth_enabled, @oauth_client_id, @oauth_client_secret_configuration_key,
                    @oauth_scopes, @oauth_authorization_mode);
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

        // Phase 51, Work Item A: the prefix, depth limit, and glob go down to
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
            VALUES (@id, @tenant_id, @name, 1, @definition, @now, @now);
            """;

        // --- Audit trail ---

        SelectAuditLog = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash
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

        // --- Audit hash chain (Phase 64) ---

        SelectLastAuditHash = $"""
            SELECT TOP (1) hash
            FROM {Schema}.audit_log
            WHERE tenant_id = @tenant_id
            ORDER BY chain_seq DESC;
            """;

        SelectAuditChain = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash
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
               SET kind        = @kind,
                   target_name = @target_name,
                   cron        = @cron,
                   time_zone   = @time_zone,
                   payload     = @payload,
                   enabled     = @enabled,
                   next_run_at = @next_run_at,
                   last_run_at = @last_run_at,
                   updated_at  = @updated_at
             OUTPUT inserted.id, inserted.created_by, inserted.created_at
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.job_schedules ({ScheduleColumns})
            OUTPUT inserted.id, inserted.created_by, inserted.created_at
            VALUES (@id, @tenant_id, @name, @kind, @target_name, @cron, @time_zone, @payload, @enabled,
                    @next_run_at, @last_run_at, @created_by, @created_at, @updated_at);
            """;

        // Not scoped by tenant: this query belongs to the worker, not to an
        // HTTP request.
        SelectDueJobSchedules = $"""
            SELECT {ScheduleColumns}
            FROM {Schema}.job_schedules
            WHERE enabled = 1 AND cron IS NOT NULL AND next_run_at IS NOT NULL AND next_run_at <= @as_of;
            """;

        // --- Inbound triggers (Phase 66) ---

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
        // OPENJSON and matched on `[key]` (0-based index). Rationale: K-182.
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
                WHERE (status = 0 AND scheduled_for <= @now)
                   OR (status IN (1, 2) AND lease_until < @now)
                ORDER BY scheduled_for
            )
            UPDATE next_job
               SET status      = 1,
                   lease_owner = @owner,
                   lease_until = @lease_until,
                   attempt     = attempt + 1,
                   started_at  = COALESCE(started_at, @now)
             OUTPUT inserted.id, inserted.tenant_id, inserted.schedule_id, inserted.kind,
                    inserted.target_name, inserted.status, inserted.payload, inserted.total_items,
                    inserted.done_items, inserted.failed_items, inserted.attempt, inserted.lease_owner,
                    inserted.lease_until, inserted.scheduled_for, inserted.started_at,
                    inserted.completed_at, inserted.error_message, inserted.created_at,
                    inserted.max_attempts;
            """;

        SelectJobs = $"""
            SELECT {JobColumns}
            FROM {Schema}.jobs
            WHERE (@tenant_id   IS NULL OR tenant_id   = @tenant_id)
              AND (@kind        IS NULL OR kind        = @kind)
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
        // (docs/arsiv/fazlar/45-URETIMDEN-EVAL-KUMESI.md, section 45.2); MERGE is not used (K-177).
        InsertEvalCaseWithComputedSeq = $"""
            INSERT INTO {Schema}.eval_cases
                (id, suite_id, seq, query, expected_output, expected_tools, context,
                 source_run_id, source_kind, promoted_at, parameters)
            OUTPUT inserted.id, inserted.suite_id, inserted.seq, inserted.query,
                   inserted.expected_output, inserted.expected_tools, inserted.context,
                   inserted.source_run_id, inserted.source_kind, inserted.promoted_at, inserted.parameters
            VALUES
                (@id, @suite_id,
                 ISNULL((SELECT MAX(seq) FROM {Schema}.eval_cases WHERE suite_id = @suite_id), -1) + 1,
                 @query, @expected_output, @expected_tools, @context,
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
               AND ISNULL(agent_name, N'') = ISNULL(@agent_name, N'')
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

        // --- Webhook ---

        const string webhookSubscriptionOutput = """
            inserted.id, inserted.tenant_id, inserted.name, inserted.url, inserted.events,
            inserted.secret_configuration_key, inserted.headers, inserted.enabled,
            inserted.consecutive_failures, inserted.created_at, inserted.updated_at
            """;

        UpsertWebhookSubscription = $"""
            UPDATE {Schema}.webhook_subscriptions WITH (UPDLOCK, SERIALIZABLE)
               SET url                      = @url,
                   events                   = @events,
                   secret_configuration_key = @secret_configuration_key,
                   headers                  = @headers,
                   enabled                  = @enabled,
                   updated_at               = @updated_at
             OUTPUT {webhookSubscriptionOutput}
             WHERE tenant_id = @tenant_id AND name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.webhook_subscriptions ({WebhookSubscriptionColumns})
            OUTPUT {webhookSubscriptionOutput}
            VALUES (@id, @tenant_id, @name, @url, @events, @secret_configuration_key, @headers, @enabled,
                    @consecutive_failures, @created_at, @updated_at);
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

        // --- Phase 53: tenant-scoped API keys ---

        InsertApiKey = $"""
            INSERT INTO {Schema}.api_keys ({ApiKeyColumns})
            VALUES (@id, @tenant_id, @name, @key_hash, @key_prefix, @scopes, @expires_at, @revoked_at,
                    @last_used_at, @created_at);
            """;

        // Setup health check (ExternalSurfaceGuard, section 53.4): the tenant
        // filter is DELIBERATELY absent. scopes is a JSON array (K-182).
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

        // K-177 two-branch upsert pattern: when UPDATE affects 0 rows, the
        // real row is in the SECOND result set (K-188); DbHelpers.ReadSingleAsync
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
            "id, tenant_id, session_id, agent_name, started_at, ended_at, turns, input_seconds, output_chars, end_reason, created_by";

        // 🚨 MERGE IS NOT USED (K-177): first a locked UPDATE, then INSERT if no row exists.
        UpsertVoiceSession = $"""
            UPDATE {Schema}.voice_sessions WITH (UPDLOCK, SERIALIZABLE)
               SET ended_at      = @ended_at,
                   turns         = @turns,
                   input_seconds = @input_seconds,
                   output_chars  = @output_chars,
                   end_reason    = @end_reason
             WHERE id = @id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.voice_sessions
                ({voiceSessionColumns})
            VALUES
                (@id, @tenant_id, @session_id, @agent_name, @started_at, @ended_at, @turns, @input_seconds, @output_chars, @end_reason, @created_by);
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
        // Phase 31 -- run/message score
        // -------------------------------------------------------------------

        const string runScoreOutput =
            "inserted.id, inserted.tenant_id, inserted.run_id, inserted.message_id, inserted.kind, " +
            "inserted.value, inserted.comment, inserted.source, inserted.author, inserted.created_at";

        // 🚨 MERGE IS NOT USED (K-177): first a locked UPDATE, then INSERT if
        // no row exists.
        //
        // 🚨 NULL uniqueness works in REVERSE here (K-184): the
        // `author = @author` comparison ALWAYS returns UNKNOWN when @author
        // is NULL (an identity-less setup) -- no row matches and the flow
        // falls through to INSERT. This is DELIBERATE: the uniqueness index
        // is filtered with `WHERE author IS NOT NULL` for the same reason
        // (migration 0005). An ISNULL match is enough for message_id; in SQL
        // Server a plain NULL comparison already treats NULLs as equal to
        // each other.
        UpsertRunScore = $"""
            UPDATE {Schema}.run_scores WITH (UPDLOCK, SERIALIZABLE)
               SET kind       = @kind,
                   value      = @value,
                   comment    = @comment,
                   source     = @source,
                   created_at = @created_at
             OUTPUT {runScoreOutput}
             WHERE tenant_id = @tenant_id
               AND run_id = @run_id
               AND ISNULL(message_id, N'') = ISNULL(@message_id, N'')
               AND author = @author;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.run_scores ({RunScoreColumns})
            OUTPUT {runScoreOutput}
            VALUES (@id, @tenant_id, @run_id, @message_id, @kind, @value, @comment, @source, @author, @created_at);
            """;

        // MERGE is not used (K-177). Two-branch pattern: first UPDATE (if we
        // are the owner or the lease expired), then INSERT only if the row is
        // absent. Both branches return the same single column (name) the
        // same way; DbHelpers.ExecuteScalarAsync falls through to the second
        // result set by itself against K-188's multi-result-set trap.
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
                   inserted.decided_by, inserted.decided_at, inserted.expires_at, inserted.created_at
              FROM {Schema}.pending_approvals AS a
             WHERE a.id IN (
                       SELECT TOP (@max) id FROM {Schema}.pending_approvals
                        WHERE status = @status_pending AND expires_at < @older_than
                     ORDER BY expires_at ASC
                   );
            """;

        // -------------------------------------------------------------------
        // Phase 65 -- tenant provider bindings (BYOK) and egress policy
        // -------------------------------------------------------------------

        // Two-branch upsert (K-177: no MERGE). Neither branch needs OUTPUT --
        // UpsertAsync returns no value -- so the K-187/188/189 "OUTPUT lands
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

    }
}
