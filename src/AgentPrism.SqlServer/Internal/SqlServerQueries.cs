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
///   <item><description>
///     <strong><c>MERGE</c> IS NOT USED.</strong> The statement has known
///     concurrency and correctness problems. Upserts are written as
///     <c>UPDATE ... WITH (UPDLOCK, SERIALIZABLE) ... OUTPUT</c> followed by
///     <c>IF @@ROWCOUNT = 0 INSERT ... OUTPUT</c>. The <c>SERIALIZABLE</c> hint
///     takes a range lock, so two sessions cannot insert the same key at the
///     same time. Rationale: <c>docs/KARARLAR.md</c>, decision K-177.
///   </description></item>
///   <item><description>
///     <c>RETURNING</c> -> <c>OUTPUT inserted.*</c> / <c>OUTPUT deleted.*</c>.
///     Both branches of the upsert return <em>the same columns</em>, so a
///     single reader suffices on the C# side.
///   </description></item>
///   <item><description>
///     <c>COUNT(*) FILTER (WHERE p)</c> -> <c>COALESCE(SUM(CASE WHEN p THEN 1 ELSE 0 END), 0)</c>.
///     🚨 <c>COALESCE</c> is required: <c>SUM</c> returns <c>NULL</c> over an
///     empty set, whereas PostgreSQL's <c>COUNT</c> returned zero.
///   </description></item>
///   <item><description>
///     <c>LEAST</c> / <c>GREATEST</c> <strong>do not exist</strong> in SQL
///     Server 2019 (added in 2022) and are written with <c>CASE</c> instead.
///   </description></item>
///   <item><description>
///     <c>FOR UPDATE SKIP LOCKED</c> -> <c>WITH (UPDLOCK, READPAST, ROWLOCK)</c>.
///   </description></item>
///   <item><description>
///     <c>UNNEST</c> and <c>= ANY(array)</c> -> <c>OPENJSON</c>; arrays travel
///     as JSON text (K-182).
///   </description></item>
///   <item><description>
///     🚨 <c>OFFSET ... FETCH NEXT @take ROWS ONLY</c> <strong>raises an
///     error</strong> when <c>@take = 0</c>, whereas PostgreSQL's <c>LIMIT 0</c>
///     returned an empty list. To keep behavior equal, paged queries add an
///     <c>@take &gt; 0</c> condition to the WHERE clause and clamp the
///     <c>FETCH</c> value to at least one.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class SqlServerQueries : SqlQueriesBase
{
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
    {
        Schema = SqlIdentifier.RequireSchemaName(schemaName);

        // CREATE SCHEMA must be the FIRST statement of a batch; conditional
        // execution is therefore wrapped in EXEC.
        CreateSchema = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{Schema}')
                EXEC(N'CREATE SCHEMA {Schema};');
            """;

        CreateMigrationsTable = $"""
            IF OBJECT_ID(N'{Schema}.__migrations', N'U') IS NULL
            CREATE TABLE {Schema}.__migrations (
                id         int               NOT NULL CONSTRAINT __migrations_pk PRIMARY KEY,
                name       nvarchar(200)     NOT NULL,
                checksum   nvarchar(64)      NOT NULL,
                applied_at datetimeoffset(7) NOT NULL
            );
            """;

        SelectAppliedMigrations = $"SELECT id, name, checksum FROM {Schema}.__migrations ORDER BY id;";

        InsertMigration = $"""
            INSERT INTO {Schema}.__migrations (id, name, checksum, applied_at)
            VALUES (@id, @name, @checksum, @applied_at);
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

        InsertAgentDefinitionVersion = $"""
            INSERT INTO {Schema}.agent_definition_versions (id, agent_id, version, definition, created_by, created_at)
            VALUES (@id, @agent_id, @version, @definition, @created_by, @created_at);
            """;

        SelectAgentDefinition = $"""
            SELECT definition, version, updated_at
            FROM {Schema}.agent_definitions
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectAgentDefinitions = $"""
            SELECT name, definition, version, updated_at
            FROM {Schema}.agent_definitions
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        DeleteAgentDefinition = $"DELETE FROM {Schema}.agent_definitions WHERE tenant_id = @tenant_id AND name = @name;";

        SelectAgentDefinitionVersions = $"""
            SELECT v.definition, v.version, v.created_at
            FROM {Schema}.agent_definition_versions v
            JOIN {Schema}.agent_definitions d ON d.id = v.agent_id
            WHERE d.tenant_id = @tenant_id AND d.name = @name
            ORDER BY v.version DESC;
            """;

        SelectAgentDefinitionVersion = $"""
            SELECT v.definition, v.created_at
            FROM {Schema}.agent_definition_versions v
            JOIN {Schema}.agent_definitions d ON d.id = v.agent_id
            WHERE d.tenant_id = @tenant_id AND d.name = @name AND v.version = @version;
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

        DeleteAgentSkill = $"DELETE FROM {Schema}.agent_skills WHERE tenant_id = @tenant_id AND name = @name;";

        DeleteAgentSkillResources = $"DELETE FROM {Schema}.agent_skill_resources WHERE skill_id = @skill_id;";

        InsertAgentSkillResource = $"""
            INSERT INTO {Schema}.agent_skill_resources
                (id, skill_id, name, description, media_type, content, created_at)
            VALUES
                (@id, @skill_id, @name, @description, @media_type, @content, @created_at);
            """;

        SelectAgentSkillResources = $"""
            SELECT name, description, media_type, content
            FROM {Schema}.agent_skill_resources
            WHERE skill_id = @skill_id
            ORDER BY name;
            """;

        // --- Skill scripts ---

        DeleteAgentSkillScripts = $"DELETE FROM {Schema}.agent_skill_scripts WHERE skill_id = @skill_id;";

        InsertAgentSkillScript = $"""
            INSERT INTO {Schema}.agent_skill_scripts
                (id, skill_id, name, description, extension, content, parameters_schema, created_at)
            VALUES
                (@id, @skill_id, @name, @description, @extension, @content, @parameters_schema, @created_at);
            """;

        SelectAgentSkillScripts = $"""
            SELECT name, description, extension, content, parameters_schema
            FROM {Schema}.agent_skill_scripts
            WHERE skill_id = @skill_id
            ORDER BY name;
            """;

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

        UpsertSession = $"""
            UPDATE {Schema}.sessions WITH (UPDLOCK, SERIALIZABLE)
               SET agent_name     = @agent_name,
                   state          = @state,
                   schema_version = @schema_version,
                   updated_at     = @updated_at
             WHERE id = @id AND tenant_id = @tenant_id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.sessions (id, tenant_id, agent_name, state, schema_version, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @state, @schema_version, @created_at, @updated_at);
            """;

        InsertSession = $"""
            INSERT INTO {Schema}.sessions (id, tenant_id, agent_name, state, schema_version, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @state, @schema_version, @created_at, @updated_at);
            """;

        SelectSession = $"""
            SELECT agent_name, state, schema_version, created_at, updated_at, tenant_id
            FROM {Schema}.sessions
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectSessionOwner = $"SELECT tenant_id FROM {Schema}.sessions WHERE id = @id;";

        DeleteSession = $"DELETE FROM {Schema}.sessions WHERE id = @id AND tenant_id = @tenant_id;";

        SelectSessions = $"""
            SELECT id, agent_name, state, schema_version, created_at, updated_at, tenant_id
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
                   replay_of_run_id = @replay_of_run_id
             WHERE id = @id;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.runs (id, tenant_id, agent_name, session_id, model_id, status, started_at, is_streaming, event_count,
                                       parent_run_id, root_run_id, depth, kind, workflow_name, agent_version, experiment_id, variant,
                                       replay_of_run_id)
            VALUES (@id, @tenant_id, @agent_name, @session_id, @model_id, @status, @started_at, @is_streaming, 0,
                    @parent_run_id, @root_run_id, @depth, @kind, @workflow_name, @agent_version, @experiment_id, @variant,
                    @replay_of_run_id);
            """;

        UpdateRunCompletion = $"""
            UPDATE {Schema}.runs
               SET status         = @status,
                   completed_at   = @completed_at,
                   event_count    = @event_count,
                   input_tokens   = @input_tokens,
                   output_tokens  = @output_tokens,
                   total_tokens   = @total_tokens,
                   error_type     = @error_type,
                   error_message  = @error_message,
                   error_class    = @error_class,
                   error_fingerprint = @error_fingerprint,
                   input_cost     = @input_cost,
                   output_cost    = @output_cost,
                   cost_currency  = @cost_currency,
                   pricing_source = @pricing_source,
                   model_id       = COALESCE(@model_id, model_id)
             WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        UpdateRunCost = $"""
            UPDATE {Schema}.runs
               SET input_cost     = @input_cost,
                   output_cost    = @output_cost,
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
                   inserted.error_class, inserted.error_fingerprint
              FROM {Schema}.runs AS r
             WHERE r.id IN (
                       SELECT TOP (@max) id FROM {Schema}.runs
                        WHERE status = @status_running
                          AND COALESCE(heartbeat_at, started_at) < @stale_before
                     ORDER BY COALESCE(heartbeat_at, started_at) ASC
                   );
            """;

        InsertOrphanRunEvent = $"""
            INSERT INTO {Schema}.run_events (run_id, seq, type, text, created_at)
            VALUES (@run_id,
                    COALESCE((SELECT MAX(seq) FROM {Schema}.run_events WHERE run_id = @run_id), -1) + 1,
                    @type, @text, @created_at);
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
                SELECT CAST(COALESCE(SUM(sub.input_tokens), 0) AS bigint)  AS input_tokens,
                       CAST(COALESCE(SUM(sub.output_tokens), 0) AS bigint) AS output_tokens,
                       CAST(COALESCE(SUM(sub.total_tokens), 0) AS bigint)  AS total_tokens,
                       CAST(COUNT(sub.total_tokens) AS bigint)             AS usage_rows,
                       SUM(sub.input_cost)                                 AS cost_input,
                       SUM(sub.output_cost)                                AS cost_output,
                       MAX(sub.cost_currency)                              AS cost_currency,
                       CAST(COALESCE(SUM(CASE WHEN sub.pricing_source = 2 THEN 1 ELSE 0 END), 0) AS bigint) AS unknown_pricing_rows,
                       CAST(COUNT(sub.pricing_source) AS bigint)           AS pricing_rows
                FROM {Schema}.runs AS sub
                WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id
            ) AS tree
            """;

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
            r.replay_of_run_id
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
            """;

        // Four result sets are retrieved in a single round trip.
        SelectRunStatistics = $"""
            SELECT CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_completed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_failed    THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_canceled  THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_running   THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_awaiting  THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(input_tokens), 0) AS bigint),
                   CAST(COALESCE(SUM(output_tokens), 0) AS bigint),
                   CAST(COALESCE(SUM(total_tokens), 0) AS bigint),
                   CASE WHEN COALESCE(SUM(CASE WHEN input_cost IS NOT NULL OR output_cost IS NOT NULL THEN 1 ELSE 0 END), 0) = 0
                        THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END,
                   MAX(cost_currency),
                   CAST(COALESCE(SUM(CASE WHEN pricing_source = @pricing_source_unknown THEN 1 ELSE 0 END), 0) AS bigint),
                   (SELECT CAST(COUNT(DISTINCT rs.run_id) AS bigint) FROM {matchedRunScoresFilter}),
                   (SELECT CASE WHEN COALESCE(SUM(CASE WHEN rs.kind = @kind_binary THEN 1 ELSE 0 END), 0) = 0 THEN NULL
                                ELSE CAST(COALESCE(SUM(CASE WHEN rs.kind = @kind_binary AND rs.value = 1 THEN 1 ELSE 0 END), 0) AS float)
                                     / SUM(CASE WHEN rs.kind = @kind_binary THEN 1 ELSE 0 END)
                           END
                    FROM {matchedRunScoresFilter})
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after);

            SELECT TOP (@max_agents)
                   agent_name,
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_failed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(total_tokens), 0) AS bigint)
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY agent_name
            ORDER BY COUNT(*) DESC, agent_name;

            SELECT TOP (@max_agents)
                   model_id,
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(input_tokens), 0) AS bigint),
                   CAST(COALESCE(SUM(output_tokens), 0) AS bigint),
                   CAST(COALESCE(SUM(total_tokens), 0) AS bigint),
                   CASE WHEN COALESCE(SUM(CASE WHEN input_cost IS NOT NULL OR output_cost IS NOT NULL THEN 1 ELSE 0 END), 0) = 0
                        THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND model_id IS NOT NULL
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY model_id
            ORDER BY COUNT(*) DESC, model_id;

            SELECT TOP (@max_agents)
                   agent_name,
                   agent_version,
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_failed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(total_tokens), 0) AS bigint)
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND agent_version IS NOT NULL
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
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
            """;

        // 🚨 SELECT ... WHERE EXISTS, not VALUES: the write applies only if the
        // target run belongs to the EXPECTED tenant (K-355). No check is
        // performed when @tenant_id is NULL.
        InsertRunEvent = $"""
            INSERT INTO {Schema}.run_events (run_id, seq, type, text, tool_name, tool_call_id, payload, created_at)
            SELECT @run_id, @seq, @type, @text, @tool_name, @tool_call_id, @payload, @created_at
            WHERE EXISTS (
                SELECT 1 FROM {Schema}.runs r
                WHERE r.id = @run_id AND (@tenant_id IS NULL OR r.tenant_id = @tenant_id));
            """;

        SelectRunEvents = $"""
            SELECT e.run_id, e.seq, e.type, e.text, e.tool_name, e.tool_call_id, e.payload, e.created_at
            FROM {Schema}.run_events e
            JOIN {Schema}.runs r ON r.id = e.run_id
            WHERE e.run_id = @run_id AND e.seq >= @from_sequence AND r.tenant_id = @tenant_id
            ORDER BY e.seq;
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

        SelectNextConversationSequence = $"""
            SELECT COALESCE(MAX(seq), -1) + 1
            FROM {Schema}.conversation_items
            WHERE conversation_id = @conversation_id;
            """;

        InsertConversationItem = $"""
            INSERT INTO {Schema}.conversation_items (id, conversation_id, seq, item, created_at)
            VALUES (@id, @conversation_id, @seq, @item, @created_at);
            """;

        SelectConversationItems = $"""
            SELECT i.item
            FROM {Schema}.conversation_items i
            JOIN {Schema}.conversations c ON c.id = i.conversation_id
            WHERE i.conversation_id = @conversation_id AND c.tenant_id = @tenant_id
            ORDER BY i.seq;
            """;

        // --- Conversation branching (Phase 47) ---
        // See PostgresQueries for the rationale and the meaning of the columns.

        SelectConversationBranchPoint = $"""
            SELECT COALESCE(MAX(seq), -1), COUNT(*)
            FROM {Schema}.conversation_items
            WHERE conversation_id = @conversation_id
              AND (@up_to_sequence IS NULL OR seq <= @up_to_sequence);
            """;

        InsertBranchConversation = $"""
            INSERT INTO {Schema}.conversations
                (id, tenant_id, agent_name, metadata, created_at, updated_at,
                 parent_conversation_id, branch_from_seq)
            SELECT @id, c.tenant_id, c.agent_name, c.metadata, @now, @now,
                   c.id, @branch_from_seq
            FROM {Schema}.conversations c
            WHERE c.id = @parent_conversation_id AND c.tenant_id = @tenant_id;
            """;

        SelectConversationItemsForBranch = $"""
            SELECT i.seq, i.item, i.created_at
            FROM {Schema}.conversation_items i
            WHERE i.conversation_id = @conversation_id
              AND (@up_to_sequence IS NULL OR i.seq <= @up_to_sequence)
            ORDER BY i.seq;
            """;

        // --- Run inputs (Phase 47) ---
        // 🚨 A second write is ignored; T-SQL has no ON CONFLICT, so the
        // condition is written with NOT EXISTS (K-177: MERGE is not used).
        InsertRunInput = $"""
            INSERT INTO {Schema}.run_inputs (run_id, tenant_id, messages, created_at)
            SELECT @run_id, @tenant_id, @messages, @created_at
            WHERE NOT EXISTS (SELECT 1 FROM {Schema}.run_inputs WHERE run_id = @run_id);
            """;

        SelectRunInput = $"""
            SELECT messages, created_at
            FROM {Schema}.run_inputs
            WHERE run_id = @run_id AND tenant_id = @tenant_id;
            """;

        // --- Tool invocations ---

        // Same tenant guard as InsertRunEvent (K-355).
        InsertToolInvocation = $"""
            INSERT INTO {Schema}.tool_invocations
                (id, run_id, tool_name, tool_call_id, source, arguments, result, duration_ms, error, created_at,
                 usage_unit, usage_quantity, usage_estimated, cost, cost_currency)
            SELECT @id, @run_id, @tool_name, @tool_call_id, @source, @arguments, @result, @duration_ms, @error, @created_at,
                   @usage_unit, @usage_quantity, @usage_estimated, @cost, @cost_currency
            WHERE EXISTS (
                SELECT 1 FROM {Schema}.runs r
                WHERE r.id = @run_id AND (@tenant_id IS NULL OR r.tenant_id = @tenant_id));
            """;

        // 🚨 New columns are ALWAYS appended at the end; existing fixed-index
        // readers (ReadToolInvocation) are never renumbered. Lesson from
        // Phase 20.
        SelectToolInvocations = $"""
            SELECT t.id, t.run_id, t.tool_name, t.tool_call_id, t.source, t.arguments, t.result,
                   t.duration_ms, t.error, t.created_at,
                   t.usage_unit, t.usage_quantity, t.usage_estimated, t.cost, t.cost_currency
            FROM {Schema}.tool_invocations t
            JOIN {Schema}.runs r ON r.id = t.run_id
            WHERE t.run_id = @run_id AND r.tenant_id = @tenant_id
            ORDER BY t.created_at, t.id;
            """;

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

        DeleteExperiment = $"""
            DELETE FROM {Schema}.experiments
            WHERE tenant_id = @tenant_id AND name = @name AND status <> 1;
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
                   CAST(COALESCE(SUM(r.input_tokens), 0) AS bigint),
                   CAST(COALESCE(SUM(r.output_tokens), 0) AS bigint),
                   CAST(COALESCE(SUM(r.total_tokens), 0) AS bigint),
                   AVG(CASE WHEN r.completed_at IS NOT NULL
                            THEN CAST(DATEDIFF_BIG(millisecond, r.started_at, r.completed_at) AS float) END),
                   CASE WHEN COALESCE(SUM(CASE WHEN r.input_cost IS NOT NULL OR r.output_cost IS NOT NULL THEN 1 ELSE 0 END), 0) = 0
                        THEN NULL ELSE COALESCE(SUM(r.input_cost), 0) + COALESCE(SUM(r.output_cost), 0) END,
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
                       CAST(COALESCE(SUM(input_tokens), 0) AS bigint) AS input_tokens,
                       CAST(COALESCE(SUM(output_tokens), 0) AS bigint) AS output_tokens,
                       CASE WHEN COALESCE(SUM(CASE WHEN input_cost IS NOT NULL OR output_cost IS NOT NULL THEN 1 ELSE 0 END), 0) = 0
                            THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END AS cost,
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

        SelectTraceByRun = $"""
            SELECT id, trace_id, run_id, tenant_id, started_at, ended_at
            FROM {Schema}.traces
            WHERE run_id = @run_id AND tenant_id = @tenant_id;
            """;

        SelectSpans = $"""
            SELECT id, parent_span_id, span_id, name, kind, started_at, ended_at, attributes, status
            FROM {Schema}.spans
            WHERE trace_id = @trace_id
            ORDER BY started_at, id;
            """;

        // --- Tool approval rules ---

        const string approvalColumns = """
            id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at
            """;

        SelectToolApprovalRules = $"""
            SELECT {approvalColumns}
            FROM {Schema}.tool_approval_rules
            WHERE tenant_id = @tenant_id
            ORDER BY created_at DESC;
            """;

        // A second rule is not opened for the same scope; the existing record
        // is returned. UPDATE deliberately replaces a column with itself: the
        // goal is not to write, but to return the existing row via OUTPUT.
        InsertToolApprovalRule = $"""
            UPDATE {Schema}.tool_approval_rules WITH (UPDLOCK, SERIALIZABLE)
               SET tool_name = tool_name
             OUTPUT inserted.id, inserted.tenant_id, inserted.agent_name, inserted.tool_name,
                    inserted.arguments_hash, inserted.created_by, inserted.created_at
             WHERE tenant_id = @tenant_id
               AND ISNULL(agent_name, N'') = ISNULL(@agent_name, N'')
               AND tool_name = @tool_name
               AND ISNULL(arguments_hash, N'') = ISNULL(@arguments_hash, N'');

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.tool_approval_rules ({approvalColumns})
            OUTPUT inserted.id, inserted.tenant_id, inserted.agent_name, inserted.tool_name,
                   inserted.arguments_hash, inserted.created_by, inserted.created_at
            VALUES (@id, @tenant_id, @agent_name, @tool_name, @arguments_hash, @created_by, @created_at);
            """;

        DeleteToolApprovalRule = $"""
            DELETE FROM {Schema}.tool_approval_rules
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // --- MCP servers ---

        const string mcpServerColumns = """
            id, tenant_id, name, description, endpoint, transport,
            authorization_configuration_key, headers, enabled, requires_approval, created_at, updated_at,
            oauth_enabled, oauth_client_id, oauth_client_secret_configuration_key, oauth_scopes, oauth_authorization_mode
            """;

        const string mcpServerOutput = """
            inserted.id, inserted.tenant_id, inserted.name, inserted.description, inserted.endpoint,
            inserted.transport, inserted.authorization_configuration_key, inserted.headers,
            inserted.enabled, inserted.requires_approval, inserted.created_at, inserted.updated_at,
            inserted.oauth_enabled, inserted.oauth_client_id,
            inserted.oauth_client_secret_configuration_key, inserted.oauth_scopes,
            inserted.oauth_authorization_mode
            """;

        SelectMcpServers = $"""
            SELECT {mcpServerColumns}
            FROM {Schema}.mcp_servers
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectMcpServer = $"""
            SELECT {mcpServerColumns}
            FROM {Schema}.mcp_servers
            WHERE tenant_id = @tenant_id AND name = @name;
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
            INSERT INTO {Schema}.mcp_servers ({mcpServerColumns})
            OUTPUT {mcpServerOutput}
            VALUES (@id, @tenant_id, @name, @description, @endpoint, @transport,
                    @authorization_configuration_key, @headers, @enabled, @requires_approval, @now, @now,
                    @oauth_enabled, @oauth_client_id, @oauth_client_secret_configuration_key,
                    @oauth_scopes, @oauth_authorization_mode);
            """;

        DeleteMcpServer = $"DELETE FROM {Schema}.mcp_servers WHERE tenant_id = @tenant_id AND name = @name;";

        // --- Tenants ---

        SelectTenants = $"""
            SELECT id, slug, display_name, created_at
            FROM {Schema}.tenants
            ORDER BY slug;
            """;

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

        DeleteTenant = $"DELETE FROM {Schema}.tenants WHERE slug = @slug;";

        // --- Attachments ---

        const string attachmentColumns = """
            id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256, created_by, created_at
            """;

        InsertAttachment = $"""
            INSERT INTO {Schema}.attachments
                (id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256,
                 content, external_uri, created_by, created_at)
            VALUES
                (@id, @tenant_id, @session_id, @run_id, @file_name, @media_type, @byte_size, @sha256,
                 @content, @external_uri, @created_by, @created_at);
            """;

        SelectAttachment = $"""
            SELECT {attachmentColumns}
            FROM {Schema}.attachments
            WHERE tenant_id = @tenant_id AND id = @id;
            """;

        SelectAttachmentContent = $"""
            SELECT content, external_uri, media_type
            FROM {Schema}.attachments
            WHERE tenant_id = @tenant_id AND id = @id;
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

        SelectAgentFile = $"""
            SELECT content
            FROM {Schema}.agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;
            """;

        UpsertAgentFile = $"""
            UPDATE {Schema}.agent_files WITH (UPDLOCK, SERIALIZABLE)
               SET content    = @content,
                   updated_at = @now
             WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;

            IF @@ROWCOUNT = 0
            INSERT INTO {Schema}.agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @path, @content, @now, @now);
            """;

        DeleteAgentFile = $"""
            DELETE FROM {Schema}.agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;
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

        SelectWorkflow = $"""
            SELECT definition, version, updated_at
            FROM {Schema}.workflows
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectWorkflows = $"""
            SELECT definition, version, updated_at, name
            FROM {Schema}.workflows
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        DeleteWorkflow = $"DELETE FROM {Schema}.workflows WHERE tenant_id = @tenant_id AND name = @name;";

        InsertWorkflowCheckpoint = $"""
            INSERT INTO {Schema}.workflow_checkpoints
                (id, tenant_id, session_id, checkpoint_id, parent_id, run_id, state, created_at)
            VALUES (@id, @tenant_id, @session_id, @checkpoint_id, @parent_id, @run_id, @state, @created_at);
            """;

        SelectWorkflowCheckpoint = $"""
            SELECT state
            FROM {Schema}.workflow_checkpoints
            WHERE tenant_id = @tenant_id AND session_id = @session_id AND checkpoint_id = @checkpoint_id;
            """;

        SelectWorkflowCheckpoints = $"""
            SELECT id, tenant_id, session_id, checkpoint_id, parent_id, run_id, created_at
            FROM {Schema}.workflow_checkpoints
            WHERE tenant_id = @tenant_id AND session_id = @session_id
            ORDER BY created_at, checkpoint_id;
            """;

        SelectWorkflowCheckpointsByRun = $"""
            SELECT id, tenant_id, session_id, checkpoint_id, parent_id, run_id, created_at
            FROM {Schema}.workflow_checkpoints
            WHERE tenant_id = @tenant_id AND run_id = @run_id
            ORDER BY created_at, checkpoint_id;
            """;

        DeleteWorkflowCheckpoints = $"""
            DELETE FROM {Schema}.workflow_checkpoints
            WHERE tenant_id = @tenant_id AND session_id = @session_id;
            """;

        // --- Audit trail ---

        InsertAuditEntry = $"""
            INSERT INTO {Schema}.audit_log (id, tenant_id, actor, action, entity, before, after, created_at)
            VALUES (@id, @tenant_id, @actor, @action, @entity, @before, @after, @created_at);
            """;

        SelectAuditLog = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at
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

        // --- Scheduling and job queue ---

        const string scheduleColumns = """
            id, tenant_id, name, kind, target_name, cron, time_zone, payload, enabled,
            next_run_at, last_run_at, created_by, created_at, updated_at
            """;

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
            INSERT INTO {Schema}.job_schedules ({scheduleColumns})
            OUTPUT inserted.id, inserted.created_by, inserted.created_at
            VALUES (@id, @tenant_id, @name, @kind, @target_name, @cron, @time_zone, @payload, @enabled,
                    @next_run_at, @last_run_at, @created_by, @created_at, @updated_at);
            """;

        SelectJobSchedule = $"""
            SELECT {scheduleColumns}
            FROM {Schema}.job_schedules
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectJobSchedules = $"""
            SELECT {scheduleColumns}
            FROM {Schema}.job_schedules
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        // Not scoped by tenant: this query belongs to the worker, not to an
        // HTTP request.
        SelectDueJobSchedules = $"""
            SELECT {scheduleColumns}
            FROM {Schema}.job_schedules
            WHERE enabled = 1 AND cron IS NOT NULL AND next_run_at IS NOT NULL AND next_run_at <= @as_of;
            """;

        DeleteJobSchedule = $"DELETE FROM {Schema}.job_schedules WHERE tenant_id = @tenant_id AND name = @name;";

        TryClaimJobScheduleNextRun = $"""
            UPDATE {Schema}.job_schedules
               SET next_run_at = @new_next_run_at, last_run_at = @ran_at
             WHERE id = @id AND next_run_at = @expected_next_run_at;
            """;

        InsertJob = $"""
            INSERT INTO {Schema}.jobs
                (id, tenant_id, schedule_id, kind, target_name, status, payload, total_items,
                 done_items, failed_items, attempt, scheduled_for, created_at, max_attempts)
            VALUES (@id, @tenant_id, @schedule_id, @kind, @target_name, 0, @payload, @total_items,
                    0, 0, 0, @scheduled_for, @created_at, @max_attempts);
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

        const string jobColumns = """
            id, tenant_id, schedule_id, kind, target_name, status, payload, total_items, done_items,
            failed_items, attempt, lease_owner, lease_until, scheduled_for, started_at, completed_at,
            error_message, created_at, max_attempts
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

        RenewJobLease = $"""
            UPDATE {Schema}.jobs SET lease_until = @lease_until WHERE id = @id AND lease_owner = @owner;
            """;

        MarkJobRunning = $"""
            UPDATE {Schema}.jobs SET status = 2 WHERE id = @id AND lease_owner = @owner AND status = 1;
            """;

        CompleteJob = $"""
            UPDATE {Schema}.jobs
               SET status = @status, completed_at = @completed_at, error_message = @error_message,
                   lease_owner = NULL, lease_until = NULL
             WHERE id = @id;
            """;

        ReleaseJobForRetry = $"""
            UPDATE {Schema}.jobs
               SET status = 0, lease_owner = NULL, lease_until = NULL, error_message = @error_message,
                   scheduled_for = COALESCE(@retry_at, scheduled_for)
             WHERE id = @id;
            """;

        CancelJob = $"""
            UPDATE {Schema}.jobs
               SET status = 5, completed_at = @completed_at, lease_owner = NULL, lease_until = NULL
             WHERE id = @id AND tenant_id = @tenant_id AND status IN (0, 1, 2);
            """;

        SelectJob = $"""
            SELECT {jobColumns}
            FROM {Schema}.jobs
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectJobs = $"""
            SELECT {jobColumns}
            FROM {Schema}.jobs
            WHERE (@tenant_id   IS NULL OR tenant_id   = @tenant_id)
              AND (@kind        IS NULL OR kind        = @kind)
              AND (@status      IS NULL OR status      = @status)
              AND (@schedule_id IS NULL OR schedule_id = @schedule_id)
              {TakeGuard}
            ORDER BY created_at DESC
            {Paging}
            """;

        SelectJobItems = $"""
            SELECT id, job_id, seq, input, run_id, status, error
            FROM {Schema}.job_items
            WHERE job_id = @job_id
            ORDER BY seq;
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

        const string suiteColumns = """
            id, tenant_id, name, description, agent_name, checks, created_at, updated_at
            """;

        SelectEvalSuites = $"""
            SELECT {suiteColumns}
            FROM {Schema}.eval_suites
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectEvalSuite = $"""
            SELECT {suiteColumns}
            FROM {Schema}.eval_suites
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

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
            INSERT INTO {Schema}.eval_suites ({suiteColumns})
            OUTPUT inserted.id, inserted.tenant_id, inserted.name, inserted.description,
                   inserted.agent_name, inserted.checks, inserted.created_at, inserted.updated_at
            VALUES (@id, @tenant_id, @name, @description, @agent_name, @checks, @now, @now);
            """;

        DeleteEvalSuite = $"DELETE FROM {Schema}.eval_suites WHERE tenant_id = @tenant_id AND name = @name;";

        const string evalCaseColumns = """
            id, suite_id, seq, query, expected_output, expected_tools, context,
            source_run_id, source_kind, promoted_at
            """;

        SelectEvalCases = $"""
            SELECT {evalCaseColumns}
            FROM {Schema}.eval_cases
            WHERE suite_id = @suite_id
            ORDER BY seq;
            """;

        DeleteEvalCases = $"DELETE FROM {Schema}.eval_cases WHERE suite_id = @suite_id;";

        InsertEvalCase = $"""
            INSERT INTO {Schema}.eval_cases
                (id, suite_id, seq, query, expected_output, expected_tools, context)
            VALUES
                (@id, @suite_id, @seq, @query, @expected_output, @expected_tools, @context);
            """;

        // Same rationale as PostgreSQL's InsertEvalCaseWithComputedSeq
        // (docs/45-URETIMDEN-EVAL-KUMESI.md, section 45.2); MERGE is not used (K-177).
        InsertEvalCaseWithComputedSeq = $"""
            INSERT INTO {Schema}.eval_cases
                (id, suite_id, seq, query, expected_output, expected_tools, context,
                 source_run_id, source_kind, promoted_at)
            OUTPUT inserted.id, inserted.suite_id, inserted.seq, inserted.query,
                   inserted.expected_output, inserted.expected_tools, inserted.context,
                   inserted.source_run_id, inserted.source_kind, inserted.promoted_at
            VALUES
                (@id, @suite_id,
                 ISNULL((SELECT MAX(seq) FROM {Schema}.eval_cases WHERE suite_id = @suite_id), -1) + 1,
                 @query, @expected_output, @expected_tools, @context,
                 @source_run_id, @source_kind, @promoted_at);
            """;

        SelectEvalCaseBySourceRun = $"""
            SELECT {evalCaseColumns}
            FROM {Schema}.eval_cases
            WHERE suite_id = @suite_id AND source_run_id = @source_run_id;
            """;

        const string evalRunColumns = """
            id, tenant_id, suite_id, job_id, agent_version, model_id, status, total, passed, failed,
            input_tokens, output_tokens, started_at, completed_at
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

        MarkEvalRunRunning = $"""
            UPDATE {Schema}.eval_runs
               SET status = 1, agent_version = @agent_version, model_id = @model_id
             WHERE id = @id;
            """;

        CompleteEvalRun = $"""
            UPDATE {Schema}.eval_runs
               SET status = @status, completed_at = @completed_at, total = @total,
                   passed = @passed, failed = @failed, input_tokens = @input_tokens,
                   output_tokens = @output_tokens
             WHERE id = @id;
            """;

        SelectEvalRun = $"""
            SELECT {evalRunColumns}
            FROM {Schema}.eval_runs
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectEvalRunByJobId = $"""
            SELECT {evalRunColumns}
            FROM {Schema}.eval_runs
            WHERE tenant_id = @tenant_id AND job_id = @job_id;
            """;

        SelectEvalRuns = $"""
            SELECT {evalRunColumns}
            FROM {Schema}.eval_runs
            WHERE (@tenant_id IS NULL OR tenant_id = @tenant_id)
              AND (@suite_id  IS NULL OR suite_id  = @suite_id)
              {TakeGuard}
            ORDER BY started_at DESC
            {Paging}
            """;

        InsertEvalCaseResult = $"""
            INSERT INTO {Schema}.eval_case_results
                (id, eval_run_id, case_id, run_id, passed, output, scores, failure_reason)
            VALUES
                (@id, @eval_run_id, @case_id, @run_id, @passed, @output, @scores, @failure_reason);
            """;

        SelectEvalCaseResults = $"""
            SELECT ecr.id, ecr.eval_run_id, ecr.case_id, ecr.run_id, ecr.passed, ecr.output,
                   ecr.scores, ecr.failure_reason
            FROM {Schema}.eval_case_results ecr
            JOIN {Schema}.eval_runs er ON er.id = ecr.eval_run_id
            WHERE er.tenant_id = @tenant_id AND ecr.eval_run_id = @eval_run_id
            ORDER BY ecr.id;
            """;

        // --- Kota ---

        const string quotaColumns = """
            id, tenant_id, agent_name, period, max_runs, max_tokens, max_cost, enabled,
            created_at, updated_at
            """;

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
            INSERT INTO {Schema}.quotas ({quotaColumns})
            OUTPUT {quotaOutput}
            VALUES (@id, @tenant_id, @agent_name, @period, @max_runs, @max_tokens, @max_cost, @enabled,
                    @created_at, @updated_at);
            """;

        SelectQuotas = $"""
            SELECT {quotaColumns}
            FROM {Schema}.quotas
            WHERE tenant_id = @tenant_id
            ORDER BY ISNULL(agent_name, N''), period;
            """;

        SelectQuota = $"""
            SELECT {quotaColumns}
            FROM {Schema}.quotas
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        DeleteQuota = $"""
            DELETE FROM {Schema}.quotas WHERE id = @id AND tenant_id = @tenant_id;
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

        SelectQuotaUsage = $"""
            SELECT tenant_id, agent_name, period, period_start, runs, tokens, cost, updated_at
            FROM {Schema}.quota_usage
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@period     IS NULL OR period     = @period)
            ORDER BY agent_name, period, period_start DESC;
            """;

        // --- Webhook ---

        // 🚨 There is NO secret in the column list: only secret_configuration_key
        // (the NAME of the key) is present (K-059).
        const string webhookSubscriptionColumns = """
            id, tenant_id, name, url, events, secret_configuration_key, headers, enabled,
            consecutive_failures, created_at, updated_at
            """;

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
            INSERT INTO {Schema}.webhook_subscriptions ({webhookSubscriptionColumns})
            OUTPUT {webhookSubscriptionOutput}
            VALUES (@id, @tenant_id, @name, @url, @events, @secret_configuration_key, @headers, @enabled,
                    @consecutive_failures, @created_at, @updated_at);
            """;

        SelectWebhookSubscriptions = $"""
            SELECT {webhookSubscriptionColumns}
            FROM {Schema}.webhook_subscriptions
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectWebhookSubscription = $"""
            SELECT {webhookSubscriptionColumns}
            FROM {Schema}.webhook_subscriptions
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        // 🚨 `events` is a JSON array; the counterpart of PostgreSQL's
        // `@event_type = ANY(events)` is an EXACT match over OPENJSON. A
        // LIKE-based search would wrongly match a 'run.completed.v2'
        // subscription while searching for 'run.completed'.
        SelectWebhookSubscriptionsForEvent = $"""
            SELECT {webhookSubscriptionColumns}
            FROM {Schema}.webhook_subscriptions
            WHERE tenant_id = @tenant_id
              AND enabled = 1
              AND EXISTS (SELECT 1 FROM OPENJSON(events) WHERE value = @event_type)
            ORDER BY name;
            """;

        DeleteWebhookSubscription = $"""
            DELETE FROM {Schema}.webhook_subscriptions WHERE tenant_id = @tenant_id AND name = @name;
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

        const string webhookDeliveryColumns = """
            id, subscription_id, tenant_id, event_type, payload, status, attempt, response_code,
            error, created_at, delivered_at
            """;

        InsertWebhookDelivery = $"""
            INSERT INTO {Schema}.webhook_deliveries
                ({webhookDeliveryColumns})
            VALUES
                (@id, @subscription_id, @tenant_id, @event_type, @payload, @status, @attempt,
                 @response_code, @error, @created_at, @delivered_at);
            """;

        SelectWebhookDelivery = $"""
            SELECT {webhookDeliveryColumns}
            FROM {Schema}.webhook_deliveries
            WHERE id = @id;
            """;

        UpdateWebhookDeliveryResult = $"""
            UPDATE {Schema}.webhook_deliveries
               SET status        = @status,
                   attempt       = @attempt,
                   response_code = @response_code,
                   error         = @error,
                   delivered_at  = CASE WHEN @status = 1 THEN @recorded_at ELSE delivered_at END
             WHERE id = @id;
            """;

        SelectWebhookDeliveries = $"""
            SELECT {webhookDeliveryColumns}
            FROM {Schema}.webhook_deliveries
            WHERE tenant_id = @tenant_id
              AND (@subscription_id IS NULL OR subscription_id = @subscription_id)
              AND (@status          IS NULL OR status          = @status)
              {TakeGuard}
            ORDER BY created_at DESC
            {Paging}
            """;

        // --- Phase 53: tenant-scoped API keys ---

        // 🚨 The column list contains NO RAW VALUE: only the irreversible
        // key_hash digest is present (section 53.2).
        const string apiKeyColumns = """
            id, tenant_id, name, key_hash, key_prefix, scopes, expires_at, revoked_at,
            last_used_at, created_at
            """;

        InsertApiKey = $"""
            INSERT INTO {Schema}.api_keys ({apiKeyColumns})
            VALUES (@id, @tenant_id, @name, @key_hash, @key_prefix, @scopes, @expires_at, @revoked_at,
                    @last_used_at, @created_at);
            """;

        SelectApiKeys = $"""
            SELECT {apiKeyColumns}
            FROM {Schema}.api_keys
            WHERE tenant_id = @tenant_id
            ORDER BY created_at;
            """;

        // The tenant filter is DELIBERATELY absent (section 53.5): the tenant
        // is the output of this query, not its input.
        SelectApiKeyByHash = $"""
            SELECT {apiKeyColumns}
            FROM {Schema}.api_keys
            WHERE key_hash = @key_hash;
            """;

        RevokeApiKey = $"""
            UPDATE {Schema}.api_keys
               SET revoked_at = @revoked_at
             WHERE tenant_id = @tenant_id AND id = @id AND revoked_at IS NULL;
            """;

        TouchApiKeyLastUsed = $"""
            UPDATE {Schema}.api_keys
               SET last_used_at = @last_used_at
             WHERE id = @id;
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

        const string retentionPolicyColumns =
            "id, tenant_id, target, max_age_days, max_rows, archive, enabled, created_at, updated_at";

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
                ({retentionPolicyColumns})
             OUTPUT {retentionPolicyOutput}
            VALUES
                (@id, @tenant_id, @target, @max_age_days, @max_rows, @archive, @enabled, @created_at, @updated_at);
            """;

        SelectRetentionPolicies = $"""
            SELECT {retentionPolicyColumns}
            FROM {Schema}.retention_policies
            WHERE tenant_id = @tenant_id
            ORDER BY target;
            """;

        SelectRetentionPolicy = $"""
            SELECT {retentionPolicyColumns}
            FROM {Schema}.retention_policies
            WHERE tenant_id = @tenant_id
              AND target    = @target;
            """;

        DeleteRetentionPolicy = $"""
            DELETE FROM {Schema}.retention_policies
             WHERE tenant_id = @tenant_id
               AND target    = @target;
            """;

        const string retentionRunColumns =
            "id, tenant_id, target, deleted_rows, archived_rows, started_at, completed_at, error";

        InsertRetentionRun = $"""
            INSERT INTO {Schema}.retention_runs
                ({retentionRunColumns})
            VALUES
                (@id, @tenant_id, @target, 0, 0, @started_at, NULL, NULL);
            """;

        UpdateRetentionRunProgress = $"""
            UPDATE {Schema}.retention_runs
               SET deleted_rows  = deleted_rows + @deleted_delta,
                   archived_rows = archived_rows + @archived_delta
             WHERE id = @id;
            """;

        CompleteRetentionRun = $"""
            UPDATE {Schema}.retention_runs
               SET completed_at = @completed_at,
                   error        = @error
             WHERE id = @id;
            """;

        SelectRetentionRuns = $"""
            SELECT {retentionRunColumns}
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
        const string runScoreColumns =
            "id, tenant_id, run_id, message_id, kind, value, comment, source, author, created_at";

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
            INSERT INTO {Schema}.run_scores ({runScoreColumns})
            OUTPUT {runScoreOutput}
            VALUES (@id, @tenant_id, @run_id, @message_id, @kind, @value, @comment, @source, @author, @created_at);
            """;

        SelectRunScores = $"""
            SELECT {runScoreColumns}
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id AND run_id = @run_id;
            """;

        DeleteRunScore = $"""
            DELETE FROM {Schema}.run_scores WHERE id = @id AND tenant_id = @tenant_id;
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

        RenewSingletonLease = $"""
            UPDATE {Schema}.singleton_leases
               SET expires_at = @expires_at, updated_at = @now
             WHERE name = @name AND owner_id = @owner_id;
            """;

        ReleaseSingletonLease = $"""
            DELETE FROM {Schema}.singleton_leases WHERE name = @name AND owner_id = @owner_id;
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

        InsertPendingApproval = $"""
            INSERT INTO {Schema}.pending_approvals
                (id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                 decided_by, decided_at, expires_at, created_at)
            VALUES
                (@id, @tenant_id, @run_id, @session_id, @request_id, @tool_name, @arguments, @status,
                 @decided_by, @decided_at, @expires_at, @created_at);
            """;

        SelectPendingApprovals = $"""
            SELECT id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                   decided_by, decided_at, expires_at, created_at
              FROM {Schema}.pending_approvals
             WHERE tenant_id = @tenant_id AND status = @status
             ORDER BY created_at ASC;
            """;

        SelectPendingApproval = $"""
            SELECT id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                   decided_by, decided_at, expires_at, created_at
              FROM {Schema}.pending_approvals
             WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // WHERE status = @status_pending: a second decision affects 0 rows,
        // DecideAsync interprets that as false.
        DecidePendingApproval = $"""
            UPDATE {Schema}.pending_approvals
               SET status = @status, decided_by = @decided_by, decided_at = @decided_at
             WHERE id = @id AND tenant_id = @tenant_id AND status = @status_pending;
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
    }
}
