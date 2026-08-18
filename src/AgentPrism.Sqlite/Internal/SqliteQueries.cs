namespace AgentPrism;

/// <summary>
/// The SQLite text of the <see cref="SqlQueriesBase"/> surface.
/// </summary>
/// <remarks>
/// <para>
/// Query <em>names</em> and the column order they return are identical to
/// PostgreSQL; the shared store code does not distinguish the two providers
/// either. The differences are only in the text itself.
/// </para>
/// <para>
/// SQLite has no schema concept; <see cref="SqlQueriesBase.Schema"/> here
/// carries a validated TABLE PREFIX (default <c>agentprism_</c>) and is
/// prepended directly to the table name — there is NO dot
/// (not <c>{Schema}.table</c>, but <c>{Schema}table</c>).
/// </para>
/// <para>Translation rules applied (measured, Phase 24 opening):</para>
/// <list type="bullet">
///   <item><description>
///     <strong>Upserts follow the SAME pattern as PostgreSQL:</strong>
///     <c>INSERT ... ON CONFLICT (cols) DO UPDATE ... RETURNING</c>. SQLite 3.35+
///     supports this in a single statement, with IDENTICAL semantics to
///     PostgreSQL's <c>ON CONFLICT ... RETURNING</c> — SQL Server's two-branch
///     <c>UPDATE ... OUTPUT</c> + <c>IF @@ROWCOUNT = 0 INSERT ... OUTPUT</c>
///     pattern (K-177) is NOT NEEDED here. Expressions like <c>COALESCE(col, '')</c>
///     in the <c>ON CONFLICT</c> target also behave like PostgreSQL: SQLite's
///     unique constraint treats NULLs as DISTINCT from each other, same as
///     PostgreSQL (the opposite of SQL Server, meaning K-184 does not apply to
///     SQLite).
///   </description></item>
///   <item><description>
///     <c>COUNT(*) FILTER (WHERE p)</c> works DIRECTLY in SQLite (3.30+);
///     SQL Server's <c>COALESCE(SUM(CASE...))</c> conversion is NOT NEEDED.
///   </description></item>
///   <item><description>
///     🚨 <c>LEFT JOIN LATERAL ... ON TRUE</c> DOES NOT EXIST in SQLite (tried:
///     "near SELECT: syntax error"). Tree aggregates (<c>SelectRun</c>,
///     <c>SelectRuns</c>) are written as separate CORRELATED SCALAR SUBQUERIES
///     in the SELECT list; each carries its own <c>WHERE</c> condition.
///   </description></item>
///   <item><description>
///     🚨 There is no <c>generate_series</c>; a recursive CTE is used instead
///     (same pattern as SQL Server). <c>strftime(format, x)</c> is used instead
///     of <c>date_trunc(@unit, x)</c>; since timestamps are already
///     <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c> text, <c>strftime</c> works directly.
///   </description></item>
///   <item><description>
///     <c>EXTRACT(EPOCH FROM (a-b)) * 1000</c> -> <c>(julianday(a) - julianday(b)) * 86400000.0</c>.
///   </description></item>
///   <item><description>
///     <c>UNNEST(@ids, @inputs) WITH ORDINALITY</c> -> two <c>json_each()</c>
///     calls joined on <c>key</c> (0-based index).
///   </description></item>
///   <item><description>
///     <c>= ANY(array)</c> -> <c>EXISTS (SELECT 1 FROM json_each(array) WHERE value = @p)</c>.
///   </description></item>
///   <item><description>
///     <c>FOR UPDATE SKIP LOCKED</c> is NOT NEEDED: SQLite is a single writer,
///     only one write operation runs at a time. A plain
///     <c>WHERE id = (SELECT ... LIMIT 1)</c> subquery gives the same result.
///   </description></item>
///   <item><description>
///     🚨 <c>LEAST</c>/<c>GREATEST</c> could be met by SQLite's multi-argument
///     <c>min()</c>/<c>max()</c>, BUT the NULL behavior is REVERSED: SQLite's
///     <c>max(a,b)</c> returns NULL if any argument is NULL (tried), while
///     PostgreSQL's <c>GREATEST</c> SKIPS NULLs. This is why a <c>CASE</c>
///     chain is used, same as on SQL Server.
///   </description></item>
///   <item><description>
///     🚨 A data-modifying CTE (<c>WITH updated AS (UPDATE ... RETURNING) UPDATE ...</c>)
///     DOES NOT EXIST in SQLite (tried: "near UPDATE: syntax error", same limit
///     as SQL Server). <c>ReportJobItem</c> is split into two separate
///     statements; the second uses SQLite's <c>changes()</c> function — this
///     function returns the number of rows affected by the MOST RECENTLY
///     completed INSERT/UPDATE/DELETE on the SAME connection. It naturally
///     preserves idempotency: if the first UPDATE affects 0 rows (item already
///     reported), <c>changes()</c> returns zero.
///   </description></item>
///   <item><description>
///     Pagination <c>LIMIT @take OFFSET @skip</c> is IDENTICAL to PostgreSQL;
///     SQL Server's <c>@take = 0</c> trap (FETCH errors) DOES NOT EXIST here —
///     SQLite's <c>LIMIT 0</c> returns an empty list, like PostgreSQL.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class SqliteQueries : SqlQueriesBase
{
    /// <summary>Creates a new query set.</summary>
    /// <param name="tablePrefix">The table prefix to validate.</param>
    /// <exception cref="AgentPrismException">The prefix is not a valid identifier.</exception>
    public SqliteQueries(string tablePrefix)
    {
        Schema = SqlIdentifier.RequireSchemaName(tablePrefix);

        // SQLite has no schema-creation concept; MigrationRunner still calls
        // this step, so a harmless no-op is given.
        CreateSchema = "SELECT 1;";

        CreateMigrationsTable = $"""
            CREATE TABLE IF NOT EXISTS {Schema}__migrations (
                id         INTEGER NOT NULL PRIMARY KEY,
                name       TEXT    NOT NULL,
                checksum   TEXT    NOT NULL,
                applied_at TEXT    NOT NULL
            );
            """;

        SelectAppliedMigrations = $"SELECT id, name, checksum FROM {Schema}__migrations ORDER BY id;";

        InsertMigration = $"""
            INSERT INTO {Schema}__migrations (id, name, checksum, applied_at)
            VALUES (@id, @name, @checksum, @applied_at);
            """;

        UpsertTenant = $"""
            INSERT INTO {Schema}tenants (id, slug, display_name, created_at)
            VALUES (@id, @slug, @display_name, @created_at)
            ON CONFLICT (slug) DO NOTHING;
            """;

        // --- Agent definitions ---

        UpsertAgentDefinition = $"""
            INSERT INTO {Schema}agent_definitions (id, tenant_id, name, version, definition, created_at, updated_at)
            VALUES (@id, @tenant_id, @name, 1, @definition, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET version    = {Schema}agent_definitions.version + 1,
                    definition = excluded.definition,
                    updated_at = excluded.updated_at
            RETURNING id, version;
            """;

        InsertAgentDefinitionVersion = $"""
            INSERT INTO {Schema}agent_definition_versions (id, agent_id, version, definition, created_by, created_at)
            VALUES (@id, @agent_id, @version, @definition, @created_by, @created_at);
            """;

        SelectAgentDefinition = $"""
            SELECT definition, version, updated_at
            FROM {Schema}agent_definitions
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectAgentDefinitions = $"""
            SELECT name, definition, version, updated_at
            FROM {Schema}agent_definitions
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        DeleteAgentDefinition = $"DELETE FROM {Schema}agent_definitions WHERE tenant_id = @tenant_id AND name = @name;";

        SelectAgentDefinitionVersions = $"""
            SELECT v.definition, v.version, v.created_at
            FROM {Schema}agent_definition_versions v
            JOIN {Schema}agent_definitions d ON d.id = v.agent_id
            WHERE d.tenant_id = @tenant_id AND d.name = @name
            ORDER BY v.version DESC;
            """;

        SelectAgentDefinitionVersion = $"""
            SELECT v.definition, v.created_at
            FROM {Schema}agent_definition_versions v
            JOIN {Schema}agent_definitions d ON d.id = v.agent_id
            WHERE d.tenant_id = @tenant_id AND d.name = @name AND v.version = @version;
            """;

        // --- Skills ---

        const string skillColumns = """
            id, tenant_id, name, description, instructions, compatibility, license,
            allowed_tools, metadata, enabled, version, created_at, updated_at
            """;

        SelectAgentSkills = $"""
            SELECT {skillColumns}
            FROM {Schema}agent_skills
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectAgentSkill = $"""
            SELECT {skillColumns}
            FROM {Schema}agent_skills
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        UpsertAgentSkill = $"""
            INSERT INTO {Schema}agent_skills ({skillColumns})
            VALUES
                (@id, @tenant_id, @name, @description, @instructions, @compatibility, @license,
                 @allowed_tools, @metadata, @enabled, 1, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET description   = excluded.description,
                    instructions  = excluded.instructions,
                    compatibility = excluded.compatibility,
                    license       = excluded.license,
                    allowed_tools = excluded.allowed_tools,
                    metadata      = excluded.metadata,
                    enabled       = excluded.enabled,
                    version       = {Schema}agent_skills.version + 1,
                    updated_at    = excluded.updated_at
            RETURNING {skillColumns};
            """;

        DeleteAgentSkill = $"DELETE FROM {Schema}agent_skills WHERE tenant_id = @tenant_id AND name = @name;";

        DeleteAgentSkillResources = $"DELETE FROM {Schema}agent_skill_resources WHERE skill_id = @skill_id;";

        InsertAgentSkillResource = $"""
            INSERT INTO {Schema}agent_skill_resources
                (id, skill_id, name, description, media_type, content, created_at)
            VALUES
                (@id, @skill_id, @name, @description, @media_type, @content, @created_at);
            """;

        SelectAgentSkillResources = $"""
            SELECT name, description, media_type, content
            FROM {Schema}agent_skill_resources
            WHERE skill_id = @skill_id
            ORDER BY name;
            """;

        // --- Skill scripts ---

        DeleteAgentSkillScripts = $"DELETE FROM {Schema}agent_skill_scripts WHERE skill_id = @skill_id;";

        InsertAgentSkillScript = $"""
            INSERT INTO {Schema}agent_skill_scripts
                (id, skill_id, name, description, extension, content, parameters_schema, created_at)
            VALUES
                (@id, @skill_id, @name, @description, @extension, @content, @parameters_schema, @created_at);
            """;

        SelectAgentSkillScripts = $"""
            SELECT name, description, extension, content, parameters_schema
            FROM {Schema}agent_skill_scripts
            WHERE skill_id = @skill_id
            ORDER BY name;
            """;

        // --- Script execution grants ---

        const string grantColumns = """
            id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at
            """;

        SelectSkillScriptGrants = $"""
            SELECT {grantColumns}
            FROM {Schema}skill_script_grants
            WHERE tenant_id = @tenant_id
            ORDER BY skill_name, COALESCE(script_name, '');
            """;

        SelectActiveSkillScriptGrant = $"""
            SELECT {grantColumns}
            FROM {Schema}skill_script_grants
            WHERE tenant_id = @tenant_id
              AND skill_name = @skill_name
              AND (script_name IS NULL OR script_name = @script_name)
              AND revoked_at IS NULL
              AND (expires_at IS NULL OR expires_at > @instant)
            ORDER BY (script_name IS NULL)
            LIMIT 1;
            """;

        UpsertSkillScriptGrant = $"""
            INSERT INTO {Schema}skill_script_grants
                (id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at)
            VALUES
                (@id, @tenant_id, @skill_name, @script_name, @granted_by, @granted_at, @expires_at, NULL)
            ON CONFLICT (tenant_id, skill_name, COALESCE(script_name, '')) DO UPDATE
                SET granted_by = excluded.granted_by,
                    granted_at = excluded.granted_at,
                    expires_at = excluded.expires_at,
                    revoked_at = NULL
            RETURNING {grantColumns};
            """;

        RevokeSkillScriptGrant = $"""
            UPDATE {Schema}skill_script_grants
            SET revoked_at = @revoked_at
            WHERE tenant_id = @tenant_id
              AND skill_name = @skill_name
              AND COALESCE(script_name, '') = COALESCE(@script_name, '')
              AND revoked_at IS NULL;
            """;

        // --- Sessions ---

        UpsertSession = $"""
            INSERT INTO {Schema}sessions (id, tenant_id, agent_name, state, schema_version, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @state, @schema_version, @created_at, @updated_at)
            ON CONFLICT (tenant_id, id) DO UPDATE
                SET agent_name     = excluded.agent_name,
                    state          = excluded.state,
                    schema_version = excluded.schema_version,
                    updated_at     = excluded.updated_at;
            """;

        InsertSession = $"""
            INSERT INTO {Schema}sessions (id, tenant_id, agent_name, state, schema_version, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @state, @schema_version, @created_at, @updated_at);
            """;

        SelectSession = $"""
            SELECT agent_name, state, schema_version, created_at, updated_at, tenant_id
            FROM {Schema}sessions
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectSessionOwner = $"SELECT tenant_id FROM {Schema}sessions WHERE id = @id;";

        DeleteSession = $"DELETE FROM {Schema}sessions WHERE id = @id AND tenant_id = @tenant_id;";

        SelectSessions = $"""
            SELECT id, agent_name, state, schema_version, created_at, updated_at, tenant_id
            FROM {Schema}sessions
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
            ORDER BY updated_at DESC
            LIMIT @take OFFSET @skip;
            """;

        // --- Runs ---

        // 🚨 Phase 46: this is an UPSERT. A queued run is first written as
        // Queued; when the worker actually runs the job, it is called a
        // second time with the SAME id and the row is updated in place (no
        // new row is OPENED).
        InsertRun = $"""
            INSERT INTO {Schema}runs (id, tenant_id, agent_name, session_id, model_id, status, started_at, is_streaming, event_count,
                                       parent_run_id, root_run_id, depth, kind, workflow_name, agent_version, experiment_id, variant,
                                       replay_of_run_id)
            VALUES (@id, @tenant_id, @agent_name, @session_id, @model_id, @status, @started_at, @is_streaming, 0,
                    @parent_run_id, @root_run_id, @depth, @kind, @workflow_name, @agent_version, @experiment_id, @variant,
                    @replay_of_run_id)
            ON CONFLICT (id) DO UPDATE SET
                tenant_id     = excluded.tenant_id,
                agent_name    = excluded.agent_name,
                session_id    = excluded.session_id,
                model_id      = excluded.model_id,
                status        = excluded.status,
                started_at    = excluded.started_at,
                is_streaming  = excluded.is_streaming,
                parent_run_id = excluded.parent_run_id,
                root_run_id   = excluded.root_run_id,
                depth         = excluded.depth,
                kind          = excluded.kind,
                workflow_name = excluded.workflow_name,
                agent_version = excluded.agent_version,
                experiment_id = excluded.experiment_id,
                variant       = excluded.variant,
                replay_of_run_id = excluded.replay_of_run_id;
            """;

        UpdateRunCompletion = $"""
            UPDATE {Schema}runs
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
            UPDATE {Schema}runs
            SET input_cost     = @input_cost,
                output_cost    = @output_cost,
                cost_currency  = @cost_currency,
                pricing_source = @pricing_source
            WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        // Orphaned run reconciliation (Phase 54). Affects only Running rows;
        // for a nonexistent id or one in a different status it silently
        // updates zero rows.
        TouchRunHeartbeat = $"""
            UPDATE {Schema}runs
            SET heartbeat_at = @at
            WHERE id = @id AND status = @status_running;
            """;

        // heartbeat_at/started_at are already ISO 8601 TEXT (K-191's adjacent
        // rule: timestamps are written by hand), so they can be concatenated
        // directly with `||` -- no need for the `::text` cast used in
        // PostgreSQL. error_fingerprint is a CONSTANT string ("orphaned") --
        // the rationale is the same as in the PostgreSQL version.
        ClaimOrphanedRuns = $"""
            UPDATE {Schema}runs
            SET status            = @status_failed,
                completed_at      = @now,
                error_type        = 'orphaned',
                error_message     = 'The process running the run is not responding; last heartbeat: '
                                     || COALESCE(heartbeat_at, started_at) || '.',
                error_class       = @error_class,
                error_fingerprint = @error_fingerprint,
                event_count       = event_count + 1
            WHERE id IN (
                SELECT id FROM {Schema}runs
                WHERE status = @status_running
                  AND COALESCE(heartbeat_at, started_at) < @stale_before
                ORDER BY COALESCE(heartbeat_at, started_at) ASC
                LIMIT @max
            )
            RETURNING id, tenant_id, agent_name, session_id, status, started_at, completed_at, is_streaming,
                      model_id, kind, workflow_name, agent_version, experiment_id, variant, replay_of_run_id,
                      parent_run_id, root_run_id, depth, event_count, error_type, error_message, error_class,
                      error_fingerprint;
            """;

        InsertOrphanRunEvent = $"""
            INSERT INTO {Schema}run_events (run_id, seq, type, text, created_at)
            VALUES (@run_id,
                    COALESCE((SELECT MAX(seq) FROM {Schema}run_events WHERE run_id = @run_id), -1) + 1,
                    @type, @text, @created_at);
            """;

        // 🚨 SQLite has no LATERAL JOIN. Tree aggregates are computed with
        // correlated scalar subqueries in the SELECT list; each does a
        // separate scan, but the read path is cheaper than updating a
        // stored total on every child-run completion.
        var runColumns = $"""
            r.id, r.tenant_id, r.agent_name, r.session_id, r.status, r.started_at, r.completed_at, r.is_streaming,
            r.input_tokens, r.output_tokens, r.total_tokens, r.event_count, r.error_type, r.error_message, r.model_id,
            r.parent_run_id, r.root_run_id, r.depth,
            (SELECT COUNT(*) FROM {Schema}runs child WHERE child.parent_run_id = r.id),
            (SELECT COALESCE(SUM(sub.input_tokens), 0)  FROM {Schema}runs sub WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id),
            (SELECT COALESCE(SUM(sub.output_tokens), 0) FROM {Schema}runs sub WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id),
            (SELECT COALESCE(SUM(sub.total_tokens), 0)  FROM {Schema}runs sub WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id),
            (SELECT COUNT(sub.total_tokens)              FROM {Schema}runs sub WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id),
            r.kind, r.workflow_name, r.agent_version, r.experiment_id, r.variant,
            r.input_cost, r.output_cost, r.cost_currency, r.pricing_source,
            (SELECT SUM(sub.input_cost)  FROM {Schema}runs sub WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id),
            (SELECT SUM(sub.output_cost) FROM {Schema}runs sub WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id),
            (SELECT MAX(sub.cost_currency) FROM {Schema}runs sub WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id),
            (SELECT COUNT(*) FILTER (WHERE sub.pricing_source = 2) FROM {Schema}runs sub WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id),
            (SELECT COUNT(sub.pricing_source) FROM {Schema}runs sub WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id),
            r.error_class, r.error_fingerprint,
            r.replay_of_run_id
            """;

        SelectRun = $"""
            SELECT {runColumns}
            FROM {Schema}runs AS r
            WHERE r.id = @id AND r.tenant_id = @tenant_id;
            """;

        SelectRuns = $"""
            SELECT {runColumns}
            FROM {Schema}runs AS r
            WHERE r.tenant_id = @tenant_id
              AND (@agent_name IS NULL OR r.agent_name = @agent_name)
              AND (@status     IS NULL OR r.status     = @status)
              AND (@kind       IS NULL OR r.kind       = @kind)
              AND (@error_type IS NULL OR r.error_type = @error_type)
              AND (
                    -- HATA-S2-001: session_id is set only on the ROOT run
                    -- (K-217); a child run's own session_id is NULL. Direct
                    -- equality combined with "includeChildren=true" would
                    -- match no child run -- it also matches if the ROOT of
                    -- the record's own tree belongs to this session.
                    @session_id IS NULL
                 OR r.session_id = @session_id
                 OR EXISTS (
                        SELECT 1 FROM {Schema}runs AS session_root
                        WHERE session_root.tenant_id = r.tenant_id
                          AND session_root.id = COALESCE(r.root_run_id, r.id)
                          AND session_root.session_id = @session_id
                    )
              )
              AND (@started_after IS NULL OR r.started_at > @started_after)
              AND (@root_run_id IS NULL OR r.root_run_id = @root_run_id OR r.id = @root_run_id)
              AND (
                    (@parent_run_id IS NOT NULL AND r.parent_run_id = @parent_run_id)
                 OR (@parent_run_id IS NULL AND (@only_root_runs = 0 OR r.parent_run_id IS NULL))
              )
            ORDER BY r.started_at DESC, r.id DESC
            LIMIT @take OFFSET @skip;
            """;

        // scored_runs/positive_rate (Phase 31): matchedRunScoresFilter repeats
        // the SAME filter as runs (tenant/eval/agent/date) -- it is added as a
        // scalar subquery instead of a separate CTE so that the existing
        // totals row can be extended with the smallest possible change.
        var matchedRunScoresFilter = $"""
            {Schema}run_scores rs
            JOIN {Schema}runs r2 ON r2.id = rs.run_id
            WHERE rs.tenant_id = @tenant_id
              AND r2.tenant_id = @tenant_id
              AND r2.kind <> @kind_eval
              AND (@agent_name IS NULL OR r2.agent_name = @agent_name)
              AND (@started_after IS NULL OR r2.started_at > @started_after)
            """;

        SelectRunStatistics = $"""
            SELECT COUNT(*),
                   COUNT(*) FILTER (WHERE status = @status_completed),
                   COUNT(*) FILTER (WHERE status = @status_failed),
                   COUNT(*) FILTER (WHERE status = @status_canceled),
                   COUNT(*) FILTER (WHERE status = @status_running),
                   COUNT(*) FILTER (WHERE status = @status_awaiting),
                   COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0),
                   COALESCE(SUM(total_tokens), 0),
                   CASE WHEN COUNT(*) FILTER (WHERE input_cost IS NOT NULL OR output_cost IS NOT NULL) = 0
                        THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END,
                   MAX(cost_currency),
                   COUNT(*) FILTER (WHERE pricing_source = @pricing_source_unknown),
                   (SELECT COUNT(DISTINCT rs.run_id) FROM {matchedRunScoresFilter}),
                   (SELECT CASE WHEN COUNT(*) FILTER (WHERE rs.kind = @kind_binary) = 0 THEN NULL
                                ELSE CAST(COUNT(*) FILTER (WHERE rs.kind = @kind_binary AND rs.value = 1) AS REAL)
                                     / COUNT(*) FILTER (WHERE rs.kind = @kind_binary)
                           END
                    FROM {matchedRunScoresFilter})
            FROM {Schema}runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after);

            SELECT agent_name,
                   COUNT(*),
                   COUNT(*) FILTER (WHERE status = @status_failed),
                   COALESCE(SUM(total_tokens), 0)
            FROM {Schema}runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY agent_name
            ORDER BY COUNT(*) DESC, agent_name
            LIMIT @max_agents;

            SELECT model_id,
                   COUNT(*),
                   COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0),
                   COALESCE(SUM(total_tokens), 0),
                   CASE WHEN COUNT(*) FILTER (WHERE input_cost IS NOT NULL OR output_cost IS NOT NULL) = 0
                        THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END
            FROM {Schema}runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND model_id IS NOT NULL
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY model_id
            ORDER BY COUNT(*) DESC, model_id
            LIMIT @max_agents;

            SELECT agent_name,
                   agent_version,
                   COUNT(*),
                   COUNT(*) FILTER (WHERE status = @status_failed),
                   COALESCE(SUM(total_tokens), 0)
            FROM {Schema}runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND agent_version IS NOT NULL
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY agent_name, agent_version
            ORDER BY agent_name, agent_version DESC
            LIMIT @max_agents;

            -- Fifth result set: error class breakdown (Phase 44). Rows with
            -- error_class NULL (written before error classification was
            -- added) fall into the Unknown (0) bucket -- K-014 does not
            -- backfill.
            SELECT COALESCE(error_class, 0),
                   COUNT(*)
            FROM {Schema}runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND status = @status_failed
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY COALESCE(error_class, 0)
            ORDER BY COUNT(*) DESC;

            -- Sixth result set: the top three fingerprint clusters per class.
            -- See PostgreSQL 0021_error_classification.sql for the rationale.
            WITH failed AS (
                SELECT COALESCE(error_class, 0) AS error_class,
                       COALESCE(error_fingerprint, '') AS error_fingerprint,
                       error_message,
                       id,
                       started_at,
                       COUNT(*) OVER (
                           PARTITION BY COALESCE(error_class, 0), COALESCE(error_fingerprint, '')
                       ) AS cluster_count,
                       MAX(started_at) OVER (
                           PARTITION BY COALESCE(error_class, 0), COALESCE(error_fingerprint, '')
                       ) AS last_seen_at,
                       ROW_NUMBER() OVER (
                           PARTITION BY COALESCE(error_class, 0), COALESCE(error_fingerprint, '')
                           ORDER BY started_at DESC
                       ) AS sample_rank
                FROM {Schema}runs
                WHERE tenant_id = @tenant_id
                  AND kind <> @kind_eval
                  AND status = @status_failed
                  AND (@agent_name IS NULL OR agent_name = @agent_name)
                  AND (@started_after IS NULL OR started_at > @started_after)
            ),
            samples AS (
                SELECT error_class, error_fingerprint, cluster_count, last_seen_at,
                       id AS sample_run_id, COALESCE(error_message, '') AS sample_message
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

        // 🚨 SELECT ... WHERE EXISTS instead of VALUES: the write applies only
        // if the target run belongs to the EXPECTED tenant (K-355). If
        // @tenant_id is NULL, no check is done.
        InsertRunEvent = $"""
            INSERT INTO {Schema}run_events (run_id, seq, type, text, tool_name, tool_call_id, payload, created_at)
            SELECT @run_id, @seq, @type, @text, @tool_name, @tool_call_id, @payload, @created_at
            WHERE EXISTS (
                SELECT 1 FROM {Schema}runs r
                WHERE r.id = @run_id AND (@tenant_id IS NULL OR r.tenant_id = @tenant_id));
            """;

        SelectRunEvents = $"""
            SELECT e.run_id, e.seq, e.type, e.text, e.tool_name, e.tool_call_id, e.payload, e.created_at
            FROM {Schema}run_events e
            JOIN {Schema}runs r ON r.id = e.run_id
            WHERE e.run_id = @run_id AND e.seq >= @from_sequence AND r.tenant_id = @tenant_id
            ORDER BY e.seq;
            """;

        // --- Conversations (chat history) ---

        UpsertConversation = $"""
            INSERT INTO {Schema}conversations (id, tenant_id, agent_name, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @now, @now)
            ON CONFLICT (id) DO UPDATE
                SET updated_at = excluded.updated_at;
            """;

        SelectNextConversationSequence = $"""
            SELECT COALESCE(MAX(seq), -1) + 1
            FROM {Schema}conversation_items
            WHERE conversation_id = @conversation_id;
            """;

        InsertConversationItem = $"""
            INSERT INTO {Schema}conversation_items (id, conversation_id, seq, item, created_at)
            VALUES (@id, @conversation_id, @seq, @item, @created_at);
            """;

        SelectConversationItems = $"""
            SELECT i.item
            FROM {Schema}conversation_items i
            JOIN {Schema}conversations c ON c.id = i.conversation_id
            WHERE i.conversation_id = @conversation_id AND c.tenant_id = @tenant_id
            ORDER BY i.seq;
            """;

        // --- Conversation branching (Phase 47) ---
        // See PostgresQueries for the rationale and column meanings.
        // 🚨 Table names carry a PREFIX (K-193): {Schema} is not a schema, it
        // is a prefix.

        SelectConversationBranchPoint = $"""
            SELECT COALESCE(MAX(seq), -1), COUNT(*)
            FROM {Schema}conversation_items
            WHERE conversation_id = @conversation_id
              AND (@up_to_sequence IS NULL OR seq <= @up_to_sequence);
            """;

        InsertBranchConversation = $"""
            INSERT INTO {Schema}conversations
                (id, tenant_id, agent_name, metadata, created_at, updated_at,
                 parent_conversation_id, branch_from_seq)
            SELECT @id, c.tenant_id, c.agent_name, c.metadata, @now, @now,
                   c.id, @branch_from_seq
            FROM {Schema}conversations c
            WHERE c.id = @parent_conversation_id AND c.tenant_id = @tenant_id;
            """;

        SelectConversationItemsForBranch = $"""
            SELECT i.seq, i.item, i.created_at
            FROM {Schema}conversation_items i
            WHERE i.conversation_id = @conversation_id
              AND (@up_to_sequence IS NULL OR i.seq <= @up_to_sequence)
            ORDER BY i.seq;
            """;

        // --- Run inputs (Phase 47) ---

        InsertRunInput = $"""
            INSERT INTO {Schema}run_inputs (run_id, tenant_id, messages, created_at)
            VALUES (@run_id, @tenant_id, @messages, @created_at)
            ON CONFLICT (run_id) DO NOTHING;
            """;

        SelectRunInput = $"""
            SELECT messages, created_at
            FROM {Schema}run_inputs
            WHERE run_id = @run_id AND tenant_id = @tenant_id;
            """;

        // --- Tool invocations ---

        // Same tenant guard as InsertRunEvent (K-355).
        InsertToolInvocation = $"""
            INSERT INTO {Schema}tool_invocations
                (id, run_id, tool_name, tool_call_id, source, arguments, result, duration_ms, error, created_at,
                 usage_unit, usage_quantity, usage_estimated, cost, cost_currency)
            SELECT @id, @run_id, @tool_name, @tool_call_id, @source, @arguments, @result, @duration_ms, @error, @created_at,
                   @usage_unit, @usage_quantity, @usage_estimated, @cost, @cost_currency
            WHERE EXISTS (
                SELECT 1 FROM {Schema}runs r
                WHERE r.id = @run_id AND (@tenant_id IS NULL OR r.tenant_id = @tenant_id));
            """;

        // 🚨 New columns are ALWAYS appended at the end; existing fixed-index
        // readers (ReadToolInvocation) are never renumbered. Lesson from
        // Phase 20.
        SelectToolInvocations = $"""
            SELECT t.id, t.run_id, t.tool_name, t.tool_call_id, t.source, t.arguments, t.result,
                   t.duration_ms, t.error, t.created_at,
                   t.usage_unit, t.usage_quantity, t.usage_estimated, t.cost, t.cost_currency
            FROM {Schema}tool_invocations t
            JOIN {Schema}runs r ON r.id = t.run_id
            WHERE t.run_id = @run_id AND r.tenant_id = @tenant_id
            ORDER BY t.created_at, t.id;
            """;

        SelectToolUsage = $"""
            SELECT t.tool_name,
                   COUNT(*),
                   COUNT(*) FILTER (WHERE t.error IS NOT NULL),
                   AVG(t.duration_ms),
                   MAX(t.created_at)
            FROM {Schema}tool_invocations t
            JOIN {Schema}runs r ON r.id = t.run_id
            WHERE r.tenant_id = @tenant_id
              AND (@started_after IS NULL OR r.started_at > @started_after)
            GROUP BY t.tool_name
            ORDER BY COUNT(*) DESC, t.tool_name
            LIMIT @max_tools;
            """;

        // --- Experiments ---

        const string experimentColumns = """
            id, tenant_id, name, agent_name, variants, status, assignment_key, started_at, ended_at, updated_at,
            canary_policy, rollback_reason
            """;

        SelectExperiments = $"""
            SELECT {experimentColumns}
            FROM {Schema}experiments
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectExperiment = $"""
            SELECT {experimentColumns}
            FROM {Schema}experiments
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectRunningExperiment = $"""
            SELECT {experimentColumns}
            FROM {Schema}experiments
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND status = 1;
            """;

        SelectRunningExperimentsWithCanary = $"""
            SELECT {experimentColumns}
            FROM {Schema}experiments
            WHERE status = 1 AND canary_policy IS NOT NULL;
            """;

        // canary_policy/rollback_reason are DELIBERATELY NOT in the SET list:
        // a Draft edit (SaveAsync) must not erase a rule already defined by
        // SetCanaryPolicyAsync.
        UpsertExperiment = $"""
            INSERT INTO {Schema}experiments (id, tenant_id, name, agent_name, variants, status, assignment_key, updated_at)
            VALUES (@id, @tenant_id, @name, @agent_name, @variants, 0, @assignment_key, @updated_at)
            ON CONFLICT (tenant_id, name) DO UPDATE SET
                agent_name     = excluded.agent_name,
                variants       = excluded.variants,
                assignment_key = excluded.assignment_key,
                updated_at     = excluded.updated_at
            WHERE {Schema}experiments.status = 0
            RETURNING id;
            """;

        DeleteExperiment = $"""
            DELETE FROM {Schema}experiments
            WHERE tenant_id = @tenant_id AND name = @name AND status <> 1;
            """;

        StartExperiment = $"""
            UPDATE {Schema}experiments
            SET status = 1, started_at = @now, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name AND status = 0
            RETURNING id;
            """;

        StopExperiment = $"""
            UPDATE {Schema}experiments
            SET status = 2, ended_at = @now, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name AND status = 1
            RETURNING id;
            """;

        // Runs INDEPENDENTLY of status (Draft or Running) -- unlike SaveAsync.
        SetExperimentCanaryPolicy = $"""
            UPDATE {Schema}experiments
            SET canary_policy = @canary_policy, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name
            RETURNING id;
            """;

        AdvanceExperimentCanaryRamp = $"""
            UPDATE {Schema}experiments
            SET variants = @variants, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name AND status = 1
            RETURNING id;
            """;

        RollbackExperimentCanary = $"""
            UPDATE {Schema}experiments
            SET variants = @variants, status = 2, ended_at = @now, rollback_reason = @rollback_reason, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name AND status = 1
            RETURNING id;
            """;

        // run_avg_scores: FIRST the average per run, THEN the average of those
        // averages per variant -- the SAME rationale as PostgreSQL's identical
        // CTE.
        SelectExperimentResults = $"""
            WITH run_avg_scores AS (
                SELECT run_id, AVG(value) AS avg_score
                FROM {Schema}run_scores
                WHERE tenant_id = @tenant_id AND kind = @score_kind_numeric AND message_id IS NULL
                GROUP BY run_id
            )
            SELECT r.variant,
                   MAX(r.agent_version),
                   COUNT(*),
                   COUNT(*) FILTER (WHERE r.status = @status_completed),
                   COUNT(*) FILTER (WHERE r.status = @status_failed),
                   COUNT(*) FILTER (WHERE r.status = @status_canceled),
                   COALESCE(SUM(r.input_tokens), 0),
                   COALESCE(SUM(r.output_tokens), 0),
                   COALESCE(SUM(r.total_tokens), 0),
                   AVG(CASE WHEN r.completed_at IS NOT NULL
                            THEN (julianday(r.completed_at) - julianday(r.started_at)) * 86400000.0 END),
                   CASE WHEN COUNT(*) FILTER (WHERE r.input_cost IS NOT NULL OR r.output_cost IS NOT NULL) = 0
                        THEN NULL ELSE COALESCE(SUM(r.input_cost), 0) + COALESCE(SUM(r.output_cost), 0) END,
                   MAX(r.cost_currency),
                   AVG(s.avg_score)
            FROM {Schema}runs r
            LEFT JOIN run_avg_scores s ON s.run_id = r.id
            WHERE r.tenant_id = @tenant_id AND r.experiment_id = @experiment_id AND r.variant IS NOT NULL
            GROUP BY r.variant;
            """;

        // 🚨 There is no generate_series; a recursive CTE is used instead.
        // Since timestamps are already yyyy-MM-ddTHH:mm:ss.fffffffZ text,
        // strftime/datetime work directly (SqliteDialect.AddTimestamp). Empty
        // buckets are also returned: otherwise a gap in the chart would look
        // like "zero" instead of "no data".
        SelectRunTimeSeries = $"""
            WITH RECURSIVE buckets(bucket) AS (
                SELECT CASE WHEN @bucket_unit = 'hour'
                            THEN strftime('%Y-%m-%dT%H:00:00.0000000Z', @from_ts)
                            ELSE strftime('%Y-%m-%dT00:00:00.0000000Z', @from_ts)
                       END
                UNION ALL
                SELECT CASE WHEN @bucket_unit = 'hour'
                            THEN strftime('%Y-%m-%dT%H:00:00.0000000Z', datetime(bucket, '+1 hour'))
                            ELSE strftime('%Y-%m-%dT00:00:00.0000000Z', datetime(bucket, '+1 day'))
                       END
                FROM buckets
                WHERE (CASE WHEN @bucket_unit = 'hour' THEN datetime(bucket, '+1 hour') ELSE datetime(bucket, '+1 day') END)
                      < datetime(@to_ts)
            ),
            matched AS (
                SELECT (CASE WHEN @bucket_unit = 'hour'
                             THEN strftime('%Y-%m-%dT%H:00:00.0000000Z', started_at)
                             ELSE strftime('%Y-%m-%dT00:00:00.0000000Z', started_at)
                        END) AS bucket,
                       COUNT(*) AS runs,
                       COUNT(*) FILTER (WHERE status = @status_failed) AS failed_runs,
                       COALESCE(SUM(input_tokens), 0) AS input_tokens,
                       COALESCE(SUM(output_tokens), 0) AS output_tokens,
                       CASE WHEN COUNT(*) FILTER (WHERE input_cost IS NOT NULL OR output_cost IS NOT NULL) = 0
                            THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END AS cost,
                       AVG(CASE WHEN completed_at IS NOT NULL
                                THEN (julianday(completed_at) - julianday(started_at)) * 86400000.0 END) AS avg_duration_ms
                FROM {Schema}runs
                WHERE tenant_id = @tenant_id
                  AND started_at >= @from_ts AND started_at < @to_ts
                  AND (@agent_name IS NULL OR agent_name = @agent_name)
                  AND (@model_id   IS NULL OR model_id   = @model_id)
                  AND (@kind       IS NULL OR kind       = @kind)
                GROUP BY bucket
            )
            SELECT buckets.bucket,
                   COALESCE(matched.runs, 0),
                   COALESCE(matched.failed_runs, 0),
                   COALESCE(matched.input_tokens, 0),
                   COALESCE(matched.output_tokens, 0),
                   matched.cost,
                   matched.avg_duration_ms
            FROM buckets
            LEFT JOIN matched ON matched.bucket = buckets.bucket
            WHERE buckets.bucket < @to_ts
              AND @bucket_step >= 0
            ORDER BY buckets.bucket;
            """;

        // --- Spans ---

        // 🚨 SQLite's multi-argument min()/max() returns NULL if any argument
        // is NULL (the opposite of PostgreSQL's GREATEST/LEAST); this is why
        // the same CASE chain as SQL Server is used.
        UpsertTrace = $"""
            INSERT INTO {Schema}traces (id, tenant_id, trace_id, run_id, started_at, ended_at)
            VALUES (@id, @tenant_id, @trace_id, @run_id, @started_at, @ended_at)
            ON CONFLICT (tenant_id, trace_id) DO UPDATE
                SET run_id     = COALESCE(excluded.run_id, {Schema}traces.run_id),
                    started_at = CASE WHEN excluded.started_at IS NULL THEN {Schema}traces.started_at
                                      WHEN {Schema}traces.started_at IS NULL THEN excluded.started_at
                                      WHEN excluded.started_at < {Schema}traces.started_at THEN excluded.started_at
                                      ELSE {Schema}traces.started_at END,
                    ended_at   = CASE WHEN excluded.ended_at IS NULL THEN {Schema}traces.ended_at
                                      WHEN {Schema}traces.ended_at IS NULL THEN excluded.ended_at
                                      WHEN excluded.ended_at > {Schema}traces.ended_at THEN excluded.ended_at
                                      ELSE {Schema}traces.ended_at END
            RETURNING id;
            """;

        UpsertSpan = $"""
            INSERT INTO {Schema}spans
                (id, trace_id, parent_span_id, span_id, name, kind, started_at, ended_at, attributes, status)
            VALUES
                (@id, @trace_id, @parent_span_id, @span_id, @name, @kind, @started_at, @ended_at, @attributes, @status)
            ON CONFLICT (id) DO UPDATE
                SET ended_at   = excluded.ended_at,
                    attributes = excluded.attributes,
                    status     = excluded.status;
            """;

        SelectTraceByRun = $"""
            SELECT id, trace_id, run_id, tenant_id, started_at, ended_at
            FROM {Schema}traces
            WHERE run_id = @run_id AND tenant_id = @tenant_id;
            """;

        SelectSpans = $"""
            SELECT id, parent_span_id, span_id, name, kind, started_at, ended_at, attributes, status
            FROM {Schema}spans
            WHERE trace_id = @trace_id
            ORDER BY started_at, id;
            """;

        // --- Tool approval rules ---

        // 🚨 New columns are ALWAYS appended at the end: SqlToolApprovalRuleStore.ReadRule
        // reads argument_conditions by fixed ordinal 7.
        const string approvalColumns = """
            id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at, argument_conditions
            """;

        SelectToolApprovalRules = $"""
            SELECT {approvalColumns}
            FROM {Schema}tool_approval_rules
            WHERE tenant_id = @tenant_id
            ORDER BY created_at DESC;
            """;

        // A second rule for the same scope is not opened; the existing record
        // is returned. The constraint is an expression index with COALESCE:
        // like PostgreSQL, SQLite does NOT count NULLs as equal to each
        // other. conditions_hash (Phase 63) joined the key so a data-rule
        // condition set is bound by the same rule as arguments_hash.
        InsertToolApprovalRule = $"""
            INSERT INTO {Schema}tool_approval_rules ({approvalColumns}, conditions_hash)
            VALUES (@id, @tenant_id, @agent_name, @tool_name, @arguments_hash, @created_by, @created_at, @argument_conditions, @conditions_hash)
            ON CONFLICT (tenant_id, COALESCE(agent_name, ''), tool_name, COALESCE(arguments_hash, ''), COALESCE(conditions_hash, ''))
                DO UPDATE SET tool_name = {Schema}tool_approval_rules.tool_name
            RETURNING {approvalColumns};
            """;

        DeleteToolApprovalRule = $"""
            DELETE FROM {Schema}tool_approval_rules
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // --- MCP servers ---

        const string mcpServerColumns = """
            id, tenant_id, name, description, endpoint, transport,
            authorization_configuration_key, headers, enabled, requires_approval, created_at, updated_at,
            oauth_enabled, oauth_client_id, oauth_client_secret_configuration_key, oauth_scopes, oauth_authorization_mode
            """;

        SelectMcpServers = $"""
            SELECT {mcpServerColumns}
            FROM {Schema}mcp_servers
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectMcpServer = $"""
            SELECT {mcpServerColumns}
            FROM {Schema}mcp_servers
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        UpsertMcpServer = $"""
            INSERT INTO {Schema}mcp_servers ({mcpServerColumns})
            VALUES
                (@id, @tenant_id, @name, @description, @endpoint, @transport,
                 @authorization_configuration_key, @headers, @enabled, @requires_approval, @now, @now,
                 @oauth_enabled, @oauth_client_id, @oauth_client_secret_configuration_key, @oauth_scopes, @oauth_authorization_mode)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET description                          = excluded.description,
                    endpoint                              = excluded.endpoint,
                    transport                              = excluded.transport,
                    authorization_configuration_key        = excluded.authorization_configuration_key,
                    headers                                = excluded.headers,
                    enabled                                = excluded.enabled,
                    requires_approval                      = excluded.requires_approval,
                    updated_at                              = excluded.updated_at,
                    oauth_enabled                           = excluded.oauth_enabled,
                    oauth_client_id                         = excluded.oauth_client_id,
                    oauth_client_secret_configuration_key   = excluded.oauth_client_secret_configuration_key,
                    oauth_scopes                            = excluded.oauth_scopes,
                    oauth_authorization_mode                = excluded.oauth_authorization_mode
            RETURNING {mcpServerColumns};
            """;

        DeleteMcpServer = $"DELETE FROM {Schema}mcp_servers WHERE tenant_id = @tenant_id AND name = @name;";

        // --- Tenants ---

        SelectTenants = $"""
            SELECT id, slug, display_name, created_at
            FROM {Schema}tenants
            ORDER BY slug;
            """;

        UpsertTenantDescriptor = $"""
            INSERT INTO {Schema}tenants (id, slug, display_name, created_at)
            VALUES (@id, @slug, @display_name, @created_at)
            ON CONFLICT (slug) DO UPDATE
                SET display_name = excluded.display_name
            RETURNING id, slug, display_name, created_at;
            """;

        DeleteTenant = $"DELETE FROM {Schema}tenants WHERE slug = @slug;";

        // --- Attachments ---

        const string attachmentColumns = """
            id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256, created_by, created_at
            """;

        InsertAttachment = $"""
            INSERT INTO {Schema}attachments
                (id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256,
                 content, external_uri, created_by, created_at)
            VALUES
                (@id, @tenant_id, @session_id, @run_id, @file_name, @media_type, @byte_size, @sha256,
                 @content, @external_uri, @created_by, @created_at);
            """;

        SelectAttachment = $"""
            SELECT {attachmentColumns}
            FROM {Schema}attachments
            WHERE tenant_id = @tenant_id AND id = @id;
            """;

        SelectAttachmentContent = $"""
            SELECT content, external_uri, media_type
            FROM {Schema}attachments
            WHERE tenant_id = @tenant_id AND id = @id;
            """;

        SelectAttachments = $"""
            SELECT {attachmentColumns}
            FROM {Schema}attachments
            WHERE tenant_id = @tenant_id
              AND (@session_id IS NULL OR session_id = @session_id)
            ORDER BY created_at DESC
            LIMIT @take OFFSET @skip;
            """;

        DeleteAttachment = $"""
            DELETE FROM {Schema}attachments
            WHERE tenant_id = @tenant_id AND id = @id
            RETURNING external_uri;
            """;

        DeleteAttachmentsBySession = $"""
            DELETE FROM {Schema}attachments
            WHERE tenant_id = @tenant_id AND session_id = @session_id
            RETURNING external_uri;
            """;

        // --- Persistent agent file memory ---

        SelectAgentFile = $"""
            SELECT content
            FROM {Schema}agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;
            """;

        UpsertAgentFile = $"""
            INSERT INTO {Schema}agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @path, @content, @now, @now)
            ON CONFLICT (tenant_id, agent_name, path) DO UPDATE
                SET content    = excluded.content,
                    updated_at = excluded.updated_at;
            """;

        DeleteAgentFile = $"""
            DELETE FROM {Schema}agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;
            """;

        // Phase 51, Work Item A: the prefix, depth limit, and glob go down to
        // SQL. SQLite carries no native regex; the regex_pattern parameter is
        // sent to keep the same call shape as PostgresQueries but is NOT USED
        // here -- the final match is always done client-side with .NET Regex.
        // LIKE is case-sensitive (the `case_sensitive_like` pragma opened by
        // SqliteDataSource); otherwise it would diverge from the other two
        // providers' (Ordinal) behavior.
        SelectAgentFilesFiltered = $"""
            SELECT path, content
            FROM {Schema}agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name
              AND path LIKE @prefix_like ESCAPE '\'
              AND (@prefix_deep_like IS NULL OR path NOT LIKE @prefix_deep_like ESCAPE '\')
              AND (@name_like IS NULL OR path LIKE @name_like ESCAPE '\')
            ORDER BY path;
            """;

        // --- Workflows ---

        UpsertWorkflow = $"""
            INSERT INTO {Schema}workflows (id, tenant_id, name, version, definition, created_at, updated_at)
            VALUES (@id, @tenant_id, @name, 1, @definition, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET version    = {Schema}workflows.version + 1,
                    definition = excluded.definition,
                    updated_at = excluded.updated_at
            RETURNING version, updated_at;
            """;

        SelectWorkflow = $"""
            SELECT definition, version, updated_at
            FROM {Schema}workflows
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectWorkflows = $"""
            SELECT definition, version, updated_at, name
            FROM {Schema}workflows
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        DeleteWorkflow = $"DELETE FROM {Schema}workflows WHERE tenant_id = @tenant_id AND name = @name;";

        InsertWorkflowCheckpoint = $"""
            INSERT INTO {Schema}workflow_checkpoints
                (id, tenant_id, session_id, checkpoint_id, parent_id, run_id, state, created_at)
            VALUES (@id, @tenant_id, @session_id, @checkpoint_id, @parent_id, @run_id, @state, @created_at);
            """;

        SelectWorkflowCheckpoint = $"""
            SELECT state
            FROM {Schema}workflow_checkpoints
            WHERE tenant_id = @tenant_id AND session_id = @session_id AND checkpoint_id = @checkpoint_id;
            """;

        SelectWorkflowCheckpoints = $"""
            SELECT id, tenant_id, session_id, checkpoint_id, parent_id, run_id, created_at
            FROM {Schema}workflow_checkpoints
            WHERE tenant_id = @tenant_id AND session_id = @session_id
            ORDER BY created_at, checkpoint_id;
            """;

        SelectWorkflowCheckpointsByRun = $"""
            SELECT id, tenant_id, session_id, checkpoint_id, parent_id, run_id, created_at
            FROM {Schema}workflow_checkpoints
            WHERE tenant_id = @tenant_id AND run_id = @run_id
            ORDER BY created_at, checkpoint_id;
            """;

        DeleteWorkflowCheckpoints = $"""
            DELETE FROM {Schema}workflow_checkpoints
            WHERE tenant_id = @tenant_id AND session_id = @session_id;
            """;

        // --- Audit log ---

        InsertAuditEntry = $"""
            INSERT INTO {Schema}audit_log (id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash)
            VALUES (@id, @tenant_id, @actor, @action, @entity, @before, @after, @created_at, @prev_hash, @hash);
            """;

        SelectAuditLog = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash
            FROM {Schema}audit_log
            WHERE tenant_id = @tenant_id
              AND (@actor      IS NULL OR actor  = @actor)
              AND (@action     IS NULL OR action = @action)
              AND (@entity     IS NULL OR entity = @entity)
              AND (@started_after  IS NULL OR created_at > @started_after)
              AND (@started_before IS NULL OR created_at < @started_before)
            ORDER BY created_at DESC
            LIMIT @take;
            """;

        // --- Audit hash chain (Phase 64) ---

        // 🚨 Ordered by `rowid`, NOT `(created_at, id)` — see PostgreSQL
        // 0031_audit_chain.sql for why timestamp/uuid ordering is wrong here.
        // SQLite's `audit_log` keeps its implicit rowid (the primary key is
        // `id TEXT`, not `INTEGER PRIMARY KEY`, so it does not alias rowid and
        // the table is not `WITHOUT ROWID`); rowid already assigns strictly
        // increasing values in insertion order with no schema change needed.
        SelectLastAuditHash = $"""
            SELECT hash
            FROM {Schema}audit_log
            WHERE tenant_id = @tenant_id
            ORDER BY rowid DESC
            LIMIT 1;
            """;

        SelectAuditChain = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash
            FROM {Schema}audit_log
            WHERE tenant_id = @tenant_id
              AND hash IS NOT NULL
              AND (@started_after  IS NULL OR created_at >= @started_after)
              AND (@started_before IS NULL OR created_at <= @started_before)
            ORDER BY rowid;
            """;

        // --- Scheduling and job queue ---

        const string scheduleColumns = """
            id, tenant_id, name, kind, target_name, cron, time_zone, payload, enabled,
            next_run_at, last_run_at, created_by, created_at, updated_at
            """;

        UpsertJobSchedule = $"""
            INSERT INTO {Schema}job_schedules ({scheduleColumns})
            VALUES (@id, @tenant_id, @name, @kind, @target_name, @cron, @time_zone, @payload, @enabled,
                    @next_run_at, @last_run_at, @created_by, @created_at, @updated_at)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET kind        = excluded.kind,
                    target_name = excluded.target_name,
                    cron        = excluded.cron,
                    time_zone   = excluded.time_zone,
                    payload     = excluded.payload,
                    enabled     = excluded.enabled,
                    next_run_at = excluded.next_run_at,
                    last_run_at = excluded.last_run_at,
                    updated_at  = excluded.updated_at
            RETURNING id, created_by, created_at;
            """;

        SelectJobSchedule = $"""
            SELECT {scheduleColumns}
            FROM {Schema}job_schedules
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectJobSchedules = $"""
            SELECT {scheduleColumns}
            FROM {Schema}job_schedules
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectDueJobSchedules = $"""
            SELECT {scheduleColumns}
            FROM {Schema}job_schedules
            WHERE enabled = 1 AND cron IS NOT NULL AND next_run_at IS NOT NULL AND next_run_at <= @as_of;
            """;

        DeleteJobSchedule = $"DELETE FROM {Schema}job_schedules WHERE tenant_id = @tenant_id AND name = @name;";

        TryClaimJobScheduleNextRun = $"""
            UPDATE {Schema}job_schedules
               SET next_run_at = @new_next_run_at, last_run_at = @ran_at
             WHERE id = @id AND next_run_at = @expected_next_run_at;
            """;

        InsertJob = $"""
            INSERT INTO {Schema}jobs
                (id, tenant_id, schedule_id, kind, target_name, status, payload, total_items,
                 done_items, failed_items, attempt, scheduled_for, created_at, max_attempts)
            VALUES (@id, @tenant_id, @schedule_id, @kind, @target_name, 0, @payload, @total_items,
                    0, 0, 0, @scheduled_for, @created_at, @max_attempts);
            """;

        // Ids are generated on the C# side; the two JSON arrays are opened
        // with json_each and matched on key (0-based index).
        InsertJobItems = $"""
            INSERT INTO {Schema}job_items (id, job_id, seq, input, status)
            SELECT ids.value, @job_id, ids.key, inputs.value, 0
            FROM json_each(@ids) AS ids
            JOIN json_each(@inputs) AS inputs ON inputs.key = ids.key;
            """;

        const string jobColumns = """
            id, tenant_id, schedule_id, kind, target_name, status, payload, total_items, done_items,
            failed_items, attempt, lease_owner, lease_until, scheduled_for, started_at, completed_at,
            error_message, created_at, max_attempts
            """;

        // 🚨 FOR UPDATE SKIP LOCKED IS NOT NEEDED: SQLite is a single writer,
        // a second worker's write can never nest inside another anyway.
        LeaseJob = $"""
            UPDATE {Schema}jobs
               SET status = 1, lease_owner = @owner, lease_until = @lease_until, attempt = attempt + 1,
                   started_at = COALESCE(started_at, @now)
             WHERE id = (
                   SELECT id FROM {Schema}jobs
                    WHERE (status = 0 AND scheduled_for <= @now)
                       OR (status IN (1, 2) AND lease_until < @now)
                    ORDER BY scheduled_for
                    LIMIT 1)
            RETURNING {jobColumns};
            """;

        RenewJobLease = $"""
            UPDATE {Schema}jobs SET lease_until = @lease_until WHERE id = @id AND lease_owner = @owner;
            """;

        MarkJobRunning = $"""
            UPDATE {Schema}jobs SET status = 2 WHERE id = @id AND lease_owner = @owner AND status = 1;
            """;

        CompleteJob = $"""
            UPDATE {Schema}jobs
               SET status = @status, completed_at = @completed_at, error_message = @error_message,
                   lease_owner = NULL, lease_until = NULL
             WHERE id = @id;
            """;

        ReleaseJobForRetry = $"""
            UPDATE {Schema}jobs
               SET status = 0, lease_owner = NULL, lease_until = NULL, error_message = @error_message,
                   scheduled_for = COALESCE(@retry_at, scheduled_for)
             WHERE id = @id;
            """;

        CancelJob = $"""
            UPDATE {Schema}jobs
               SET status = 5, completed_at = @completed_at, lease_owner = NULL, lease_until = NULL
             WHERE id = @id AND tenant_id = @tenant_id AND status IN (0, 1, 2);
            """;

        SelectJob = $"""
            SELECT {jobColumns}
            FROM {Schema}jobs
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectJobs = $"""
            SELECT {jobColumns}
            FROM {Schema}jobs
            WHERE (@tenant_id   IS NULL OR tenant_id   = @tenant_id)
              AND (@kind        IS NULL OR kind        = @kind)
              AND (@status      IS NULL OR status      = @status)
              AND (@schedule_id IS NULL OR schedule_id = @schedule_id)
            ORDER BY created_at DESC
            LIMIT @take OFFSET @skip;
            """;

        SelectJobItems = $"""
            SELECT id, job_id, seq, input, run_id, status, error
            FROM {Schema}job_items
            WHERE job_id = @job_id
            ORDER BY seq;
            """;

        // 🚨 A data-modifying CTE does not exist in SQLite (same limit as SQL
        // Server). Two separate statements run in a single round trip (one
        // CommandText, one ExecuteNonQuery); the second statement reads the
        // row count affected by the first with changes(). It is idempotent:
        // if the item was already reported (status <> 0), the first UPDATE
        // affects no rows and changes() returns zero.
        ReportJobItem = $"""
            UPDATE {Schema}job_items
               SET status = @status, run_id = @run_id, error = @error
             WHERE job_id = @job_id AND seq = @seq AND status = 0;

            UPDATE {Schema}jobs
               SET done_items   = done_items   + (CASE WHEN @status = 1 THEN changes() ELSE 0 END),
                   failed_items = failed_items + (CASE WHEN @status = 2 THEN changes() ELSE 0 END)
             WHERE id = @job_id;
            """;

        // --- Evaluation (eval) ---

        const string suiteColumns = """
            id, tenant_id, name, description, agent_name, checks, created_at, updated_at
            """;

        SelectEvalSuites = $"""
            SELECT {suiteColumns}
            FROM {Schema}eval_suites
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectEvalSuite = $"""
            SELECT {suiteColumns}
            FROM {Schema}eval_suites
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        UpsertEvalSuite = $"""
            INSERT INTO {Schema}eval_suites
                (id, tenant_id, name, description, agent_name, checks, created_at, updated_at)
            VALUES
                (@id, @tenant_id, @name, @description, @agent_name, @checks, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET description = excluded.description,
                    agent_name  = excluded.agent_name,
                    checks      = excluded.checks,
                    updated_at  = excluded.updated_at
            RETURNING {suiteColumns};
            """;

        DeleteEvalSuite = $"DELETE FROM {Schema}eval_suites WHERE tenant_id = @tenant_id AND name = @name;";

        const string evalCaseColumns = """
            id, suite_id, seq, query, expected_output, expected_tools, context,
            source_run_id, source_kind, promoted_at
            """;

        SelectEvalCases = $"""
            SELECT {evalCaseColumns}
            FROM {Schema}eval_cases
            WHERE suite_id = @suite_id
            ORDER BY seq;
            """;

        DeleteEvalCases = $"DELETE FROM {Schema}eval_cases WHERE suite_id = @suite_id;";

        InsertEvalCase = $"""
            INSERT INTO {Schema}eval_cases
                (id, suite_id, seq, query, expected_output, expected_tools, context)
            VALUES
                (@id, @suite_id, @seq, @query, @expected_output, @expected_tools, @context);
            """;

        // Same rationale as PostgreSQL's InsertEvalCaseWithComputedSeq
        // (docs/45-URETIMDEN-EVAL-KUMESI.md, section 45.2). SQLite 3.35+
        // supports RETURNING (already used in the other eval queries).
        InsertEvalCaseWithComputedSeq = $"""
            INSERT INTO {Schema}eval_cases
                (id, suite_id, seq, query, expected_output, expected_tools, context,
                 source_run_id, source_kind, promoted_at)
            VALUES
                (@id, @suite_id,
                 COALESCE((SELECT MAX(seq) FROM {Schema}eval_cases WHERE suite_id = @suite_id), -1) + 1,
                 @query, @expected_output, @expected_tools, @context,
                 @source_run_id, @source_kind, @promoted_at)
            RETURNING {evalCaseColumns};
            """;

        SelectEvalCaseBySourceRun = $"""
            SELECT {evalCaseColumns}
            FROM {Schema}eval_cases
            WHERE suite_id = @suite_id AND source_run_id = @source_run_id;
            """;

        const string evalRunColumns = """
            id, tenant_id, suite_id, job_id, agent_version, model_id, status, total, passed, failed,
            input_tokens, output_tokens, started_at, completed_at
            """;

        InsertEvalRun = $"""
            INSERT INTO {Schema}eval_runs
                (id, tenant_id, suite_id, job_id, status, total, passed, failed, started_at)
            VALUES (@id, @tenant_id, @suite_id, @job_id, 0, @total, 0, 0, @started_at)
            RETURNING {evalRunColumns};
            """;

        MarkEvalRunRunning = $"""
            UPDATE {Schema}eval_runs
               SET status = 1, agent_version = @agent_version, model_id = @model_id
             WHERE id = @id;
            """;

        CompleteEvalRun = $"""
            UPDATE {Schema}eval_runs
               SET status = @status, completed_at = @completed_at, total = @total,
                   passed = @passed, failed = @failed, input_tokens = @input_tokens,
                   output_tokens = @output_tokens
             WHERE id = @id;
            """;

        SelectEvalRun = $"""
            SELECT {evalRunColumns}
            FROM {Schema}eval_runs
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectEvalRunByJobId = $"""
            SELECT {evalRunColumns}
            FROM {Schema}eval_runs
            WHERE tenant_id = @tenant_id AND job_id = @job_id;
            """;

        SelectEvalRuns = $"""
            SELECT {evalRunColumns}
            FROM {Schema}eval_runs
            WHERE (@tenant_id IS NULL OR tenant_id = @tenant_id)
              AND (@suite_id  IS NULL OR suite_id  = @suite_id)
            ORDER BY started_at DESC
            LIMIT @take OFFSET @skip;
            """;

        InsertEvalCaseResult = $"""
            INSERT INTO {Schema}eval_case_results
                (id, eval_run_id, case_id, run_id, passed, output, scores, failure_reason)
            VALUES
                (@id, @eval_run_id, @case_id, @run_id, @passed, @output, @scores, @failure_reason);
            """;

        SelectEvalCaseResults = $"""
            SELECT ecr.id, ecr.eval_run_id, ecr.case_id, ecr.run_id, ecr.passed, ecr.output,
                   ecr.scores, ecr.failure_reason
            FROM {Schema}eval_case_results ecr
            JOIN {Schema}eval_runs er ON er.id = ecr.eval_run_id
            WHERE er.tenant_id = @tenant_id AND ecr.eval_run_id = @eval_run_id
            ORDER BY ecr.id;
            """;

        // --- Quota ---

        const string quotaColumns = """
            id, tenant_id, agent_name, period, max_runs, max_tokens, max_cost, enabled,
            created_at, updated_at
            """;

        // 🚨 The conflict target is the expression COALESCE(agent_name, ''):
        // like PostgreSQL, SQLite does NOT count NULLs as equal to each other;
        // a plain (tenant_id, agent_name, period) target would allow the same
        // rule with agent_name NULL to be added an unlimited number of times.
        UpsertQuota = $"""
            INSERT INTO {Schema}quotas ({quotaColumns})
            VALUES
                (@id, @tenant_id, @agent_name, @period, @max_runs, @max_tokens, @max_cost, @enabled,
                 @created_at, @updated_at)
            ON CONFLICT (tenant_id, COALESCE(agent_name, ''), period) DO UPDATE
               SET max_runs   = excluded.max_runs,
                   max_tokens = excluded.max_tokens,
                   max_cost   = excluded.max_cost,
                   enabled    = excluded.enabled,
                   updated_at = excluded.updated_at
            RETURNING {quotaColumns};
            """;

        SelectQuotas = $"""
            SELECT {quotaColumns}
            FROM {Schema}quotas
            WHERE tenant_id = @tenant_id
            ORDER BY COALESCE(agent_name, ''), period;
            """;

        SelectQuota = $"""
            SELECT {quotaColumns}
            FROM {Schema}quotas
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        DeleteQuota = $"""
            DELETE FROM {Schema}quotas WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // Consumption is incremented ATOMICALLY.
        AddQuotaUsage = $"""
            INSERT INTO {Schema}quota_usage
                (tenant_id, agent_name, period, period_start, runs, tokens, cost, updated_at)
            VALUES
                (@tenant_id, @agent_name, @period, @period_start, @runs, @tokens, @cost, @updated_at)
            ON CONFLICT (tenant_id, agent_name, period, period_start) DO UPDATE
               SET runs       = {Schema}quota_usage.runs   + excluded.runs,
                   tokens     = {Schema}quota_usage.tokens + excluded.tokens,
                   cost       = {Schema}quota_usage.cost   + excluded.cost,
                   updated_at = excluded.updated_at;
            """;

        SelectQuotaUsage = $"""
            SELECT tenant_id, agent_name, period, period_start, runs, tokens, cost, updated_at
            FROM {Schema}quota_usage
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@period     IS NULL OR period     = @period)
            ORDER BY agent_name, period, period_start DESC;
            """;

        // --- Webhook ---

        // 🚨 The column list contains NO SECRET: only secret_configuration_key
        // (the NAME of the key) is present (K-059).
        const string webhookSubscriptionColumns = """
            id, tenant_id, name, url, events, secret_configuration_key, headers, enabled,
            consecutive_failures, created_at, updated_at
            """;

        UpsertWebhookSubscription = $"""
            INSERT INTO {Schema}webhook_subscriptions ({webhookSubscriptionColumns})
            VALUES
                (@id, @tenant_id, @name, @url, @events, @secret_configuration_key, @headers, @enabled,
                 @consecutive_failures, @created_at, @updated_at)
            ON CONFLICT (tenant_id, name) DO UPDATE
               SET url                      = excluded.url,
                   events                   = excluded.events,
                   secret_configuration_key = excluded.secret_configuration_key,
                   headers                  = excluded.headers,
                   enabled                  = excluded.enabled,
                   updated_at               = excluded.updated_at
            RETURNING {webhookSubscriptionColumns};
            """;

        SelectWebhookSubscriptions = $"""
            SELECT {webhookSubscriptionColumns}
            FROM {Schema}webhook_subscriptions
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectWebhookSubscription = $"""
            SELECT {webhookSubscriptionColumns}
            FROM {Schema}webhook_subscriptions
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        // 🚨 events is a JSON array; the counterpart of PostgreSQL's
        // = ANY(events) expression is an EXACT match over json_each.
        SelectWebhookSubscriptionsForEvent = $"""
            SELECT {webhookSubscriptionColumns}
            FROM {Schema}webhook_subscriptions
            WHERE tenant_id = @tenant_id
              AND enabled = 1
              AND EXISTS (SELECT 1 FROM json_each(events) WHERE value = @event_type)
            ORDER BY name;
            """;

        DeleteWebhookSubscription = $"""
            DELETE FROM {Schema}webhook_subscriptions WHERE tenant_id = @tenant_id AND name = @name;
            """;

        // The consecutive-failure counter and auto-disable are done in a
        // SINGLE statement.
        UpdateWebhookSubscriptionOutcome = $"""
            UPDATE {Schema}webhook_subscriptions
               SET consecutive_failures = CASE WHEN @succeeded = 1 THEN 0 ELSE consecutive_failures + 1 END,
                   enabled = CASE
                       WHEN @succeeded = 1 THEN enabled
                       WHEN @threshold > 0 AND consecutive_failures + 1 >= @threshold THEN 0
                       ELSE enabled
                   END,
                   updated_at = @updated_at
             WHERE id = @id
            RETURNING (NOT enabled) AND (@succeeded = 0);
            """;

        const string webhookDeliveryColumns = """
            id, subscription_id, tenant_id, event_type, payload, status, attempt, response_code,
            error, created_at, delivered_at
            """;

        InsertWebhookDelivery = $"""
            INSERT INTO {Schema}webhook_deliveries
                ({webhookDeliveryColumns})
            VALUES
                (@id, @subscription_id, @tenant_id, @event_type, @payload, @status, @attempt,
                 @response_code, @error, @created_at, @delivered_at);
            """;

        SelectWebhookDelivery = $"""
            SELECT {webhookDeliveryColumns}
            FROM {Schema}webhook_deliveries
            WHERE id = @id;
            """;

        UpdateWebhookDeliveryResult = $"""
            UPDATE {Schema}webhook_deliveries
               SET status        = @status,
                   attempt       = @attempt,
                   response_code = @response_code,
                   error         = @error,
                   delivered_at  = CASE WHEN @status = 1 THEN @recorded_at ELSE delivered_at END
             WHERE id = @id;
            """;

        SelectWebhookDeliveries = $"""
            SELECT {webhookDeliveryColumns}
            FROM {Schema}webhook_deliveries
            WHERE tenant_id = @tenant_id
              AND (@subscription_id IS NULL OR subscription_id = @subscription_id)
              AND (@status          IS NULL OR status          = @status)
            ORDER BY created_at DESC
            LIMIT @take OFFSET @skip;
            """;

        // --- Phase 53: tenant-scoped API keys ---

        // 🚨 The column list contains NO RAW VALUE: only the irreversible
        // key_hash digest is present (section 53.2).
        const string apiKeyColumns = """
            id, tenant_id, name, key_hash, key_prefix, scopes, expires_at, revoked_at,
            last_used_at, created_at
            """;

        InsertApiKey = $"""
            INSERT INTO {Schema}api_keys ({apiKeyColumns})
            VALUES (@id, @tenant_id, @name, @key_hash, @key_prefix, @scopes, @expires_at, @revoked_at,
                    @last_used_at, @created_at);
            """;

        SelectApiKeys = $"""
            SELECT {apiKeyColumns}
            FROM {Schema}api_keys
            WHERE tenant_id = @tenant_id
            ORDER BY created_at;
            """;

        // The tenant filter is DELIBERATELY absent (section 53.5): the tenant
        // is the output of this query, not its input.
        SelectApiKeyByHash = $"""
            SELECT {apiKeyColumns}
            FROM {Schema}api_keys
            WHERE key_hash = @key_hash;
            """;

        RevokeApiKey = $"""
            UPDATE {Schema}api_keys
               SET revoked_at = @revoked_at
             WHERE tenant_id = @tenant_id AND id = @id AND revoked_at IS NULL;
            """;

        TouchApiKeyLastUsed = $"""
            UPDATE {Schema}api_keys
               SET last_used_at = @last_used_at
             WHERE id = @id;
            """;

        // Setup health check (ExternalSurfaceGuard, section 53.4): the tenant
        // filter is DELIBERATELY absent. scopes is a JSON array.
        HasApiKeyWithScope = $"""
            SELECT EXISTS (
                SELECT 1 FROM {Schema}api_keys
                WHERE revoked_at IS NULL
                  AND (expires_at IS NULL OR expires_at > @now)
                  AND EXISTS (SELECT 1 FROM json_each(scopes) WHERE value = @scope)
            );
            """;

        const string retentionPolicyColumns =
            "id, tenant_id, target, max_age_days, max_rows, archive, enabled, created_at, updated_at";

        // Upsert is identical to PostgreSQL (K-194): a single-statement
        // INSERT ... ON CONFLICT ... RETURNING; SQL Server's two-branch
        // pattern (K-177) is absent here.
        UpsertRetentionPolicy = $"""
            INSERT INTO {Schema}retention_policies
                ({retentionPolicyColumns})
            VALUES
                (@id, @tenant_id, @target, @max_age_days, @max_rows, @archive, @enabled, @created_at, @updated_at)
            ON CONFLICT (tenant_id, target) DO UPDATE
               SET max_age_days = excluded.max_age_days,
                   max_rows     = excluded.max_rows,
                   archive      = excluded.archive,
                   enabled      = excluded.enabled,
                   updated_at   = excluded.updated_at
            RETURNING {retentionPolicyColumns};
            """;

        SelectRetentionPolicies = $"""
            SELECT {retentionPolicyColumns}
            FROM {Schema}retention_policies
            WHERE tenant_id = @tenant_id
            ORDER BY target;
            """;

        SelectRetentionPolicy = $"""
            SELECT {retentionPolicyColumns}
            FROM {Schema}retention_policies
            WHERE tenant_id = @tenant_id
              AND target    = @target;
            """;

        DeleteRetentionPolicy = $"""
            DELETE FROM {Schema}retention_policies
             WHERE tenant_id = @tenant_id
               AND target    = @target;
            """;

        const string retentionRunColumns =
            "id, tenant_id, target, deleted_rows, archived_rows, started_at, completed_at, error";

        InsertRetentionRun = $"""
            INSERT INTO {Schema}retention_runs
                ({retentionRunColumns})
            VALUES
                (@id, @tenant_id, @target, 0, 0, @started_at, NULL, NULL);
            """;

        UpdateRetentionRunProgress = $"""
            UPDATE {Schema}retention_runs
               SET deleted_rows  = deleted_rows + @deleted_delta,
                   archived_rows = archived_rows + @archived_delta
             WHERE id = @id;
            """;

        CompleteRetentionRun = $"""
            UPDATE {Schema}retention_runs
               SET completed_at = @completed_at,
                   error        = @error
             WHERE id = @id;
            """;

        SelectRetentionRuns = $"""
            SELECT {retentionRunColumns}
            FROM {Schema}retention_runs
            WHERE tenant_id = @tenant_id
              AND (@target IS NULL OR target = @target)
            ORDER BY started_at DESC
            LIMIT @take OFFSET @skip;
            """;
        const string voiceSessionColumns =
            "id, tenant_id, session_id, agent_name, started_at, ended_at, turns, input_seconds, output_chars, end_reason, created_by";

        UpsertVoiceSession = $"""
            INSERT INTO {Schema}voice_sessions
                ({voiceSessionColumns})
            VALUES
                (@id, @tenant_id, @session_id, @agent_name, @started_at, @ended_at, @turns, @input_seconds, @output_chars, @end_reason, @created_by)
            ON CONFLICT (id) DO UPDATE
               SET ended_at      = excluded.ended_at,
                   turns         = excluded.turns,
                   input_seconds = excluded.input_seconds,
                   output_chars  = excluded.output_chars,
                   end_reason    = excluded.end_reason;
            """;

        SelectVoiceSessions = $"""
            SELECT {voiceSessionColumns}
            FROM {Schema}voice_sessions
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@session_id IS NULL OR session_id = @session_id)
            ORDER BY started_at DESC
            LIMIT @take OFFSET @skip;
            """;

        // -------------------------------------------------------------------
        // Phase 31 -- run/message score
        // -------------------------------------------------------------------
        const string runScoreColumns =
            "id, tenant_id, run_id, message_id, kind, value, comment, source, author, created_at";

        // Upsert is identical to PostgreSQL (K-194): message_id is equalized
        // with COALESCE(…, ''), author is DELIBERATELY NOT COALESCED --
        // SQLite also does not count NULLs as equal to each other, so when
        // author is empty (an identity-less setup) each call opens a new row.
        UpsertRunScore = $"""
            INSERT INTO {Schema}run_scores
                ({runScoreColumns})
            VALUES
                (@id, @tenant_id, @run_id, @message_id, @kind, @value, @comment, @source, @author, @created_at)
            ON CONFLICT (tenant_id, run_id, COALESCE(message_id, ''), author) DO UPDATE
               SET kind       = excluded.kind,
                   value      = excluded.value,
                   comment    = excluded.comment,
                   source     = excluded.source,
                   created_at = excluded.created_at
            RETURNING {runScoreColumns};
            """;

        SelectRunScores = $"""
            SELECT {runScoreColumns}
            FROM {Schema}run_scores
            WHERE tenant_id = @tenant_id AND run_id = @run_id;
            """;

        DeleteRunScore = $"""
            DELETE FROM {Schema}run_scores WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // Upsert is identical to PostgreSQL (K-194).
        AcquireSingletonLease = $"""
            INSERT INTO {Schema}singleton_leases (name, owner_id, expires_at, updated_at)
            VALUES (@name, @owner_id, @expires_at, @now)
            ON CONFLICT (name) DO UPDATE SET
                owner_id   = excluded.owner_id,
                expires_at = excluded.expires_at,
                updated_at = excluded.updated_at
            WHERE {Schema}singleton_leases.owner_id = excluded.owner_id
               OR {Schema}singleton_leases.expires_at < @now
            RETURNING name;
            """;

        RenewSingletonLease = $"""
            UPDATE {Schema}singleton_leases
               SET expires_at = @expires_at, updated_at = @now
             WHERE name = @name AND owner_id = @owner_id;
            """;

        ReleaseSingletonLease = $"""
            DELETE FROM {Schema}singleton_leases WHERE name = @name AND owner_id = @owner_id;
            """;

        // Plain INSERT: a uniqueness violation is caught by
        // SqlDialect.IsUniqueViolation, the caller reads the existing record
        // with SelectIdempotencyKey.
        // 🚨 `key` is a RESERVED word; it is double-quoted as "key".
        InsertIdempotencyKey = $"""
            INSERT INTO {Schema}idempotency_keys (tenant_id, "key", fingerprint, state, created_at)
            VALUES (@tenant_id, @key, @fingerprint, 0, @created_at);
            """;

        SelectIdempotencyKey = $"""
            SELECT state, fingerprint, status_code, content_type, body, run_id, headers
              FROM {Schema}idempotency_keys
             WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        CompleteIdempotencyKey = $"""
            UPDATE {Schema}idempotency_keys
               SET state = 2, status_code = @status_code, content_type = @content_type,
                   body = @body, run_id = @run_id, headers = @headers, completed_at = @completed_at
             WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        DeleteIdempotencyKey = $"""
            DELETE FROM {Schema}idempotency_keys WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        InsertPendingApproval = $"""
            INSERT INTO {Schema}pending_approvals
                (id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                 decided_by, decided_at, expires_at, created_at)
            VALUES
                (@id, @tenant_id, @run_id, @session_id, @request_id, @tool_name, @arguments, @status,
                 @decided_by, @decided_at, @expires_at, @created_at);
            """;

        SelectPendingApprovals = $"""
            SELECT id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                   decided_by, decided_at, expires_at, created_at
              FROM {Schema}pending_approvals
             WHERE tenant_id = @tenant_id AND status = @status
             ORDER BY created_at ASC;
            """;

        SelectPendingApproval = $"""
            SELECT id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                   decided_by, decided_at, expires_at, created_at
              FROM {Schema}pending_approvals
             WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // WHERE status = @status_pending: a second decision affects 0 rows,
        // DecideAsync interprets that as false.
        DecidePendingApproval = $"""
            UPDATE {Schema}pending_approvals
               SET status = @status, decided_by = @decided_by, decided_at = @decided_at
             WHERE id = @id AND tenant_id = @tenant_id AND status = @status_pending;
            """;

        // The SAME pattern as ClaimOrphanedRuns: closed rows are read via
        // RETURNING. There is NO tenant filter -- this is a maintenance
        // operation.
        ExpirePendingApprovals = $"""
            UPDATE {Schema}pending_approvals
               SET status = @status_expired
             WHERE id IN (
                 SELECT id FROM {Schema}pending_approvals
                 WHERE status = @status_pending AND expires_at < @older_than
                 ORDER BY expires_at ASC
                 LIMIT @max
             )
             RETURNING id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                       decided_by, decided_at, expires_at, created_at;
            """;
    }
}
