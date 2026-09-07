namespace AgentPrism;

/// <summary>
/// Holds the PostgreSQL texts for the <see cref="SqlQueriesBase"/> surface.
/// </summary>
/// <remarks>
/// Upserts use <c>ON CONFLICT ... DO UPDATE ... RETURNING</c>; a single round
/// trip both writes and returns the result. The same contract is met on the
/// SQL Server side with <c>UPDATE ... OUTPUT</c> + <c>IF ROWCOUNT = 0 INSERT
/// ... OUTPUT</c>.
/// </remarks>
internal sealed class PostgresQueries : SqlQueriesBase
{
    /// <summary>Creates a new query set.</summary>
    /// <param name="schemaName">The schema name to validate.</param>
    /// <exception cref="AgentPrismException">The schema name is not a valid identifier.</exception>
    public PostgresQueries(string schemaName)
        : base(schemaName)
    {
        CreateSchema = $"CREATE SCHEMA IF NOT EXISTS {Schema};";

        // set_name (phase 67): each migration SET numbers its own files from
        // 0001, so id alone is no longer unique — the primary key is
        // (set_name, id). A fresh database gets this shape directly; an
        // existing one (created before phase 67) is upgraded by
        // UpgradeMigrationsTable below — NOT a numbered migration file
        // (K-475: it would collide with InsertMigration's fixed text, see
        // SqlDialect.UpgradeMigrationsTableAsync).
        CreateMigrationsTable = $"""
            CREATE TABLE IF NOT EXISTS {Schema}.__migrations (
                set_name   text        NOT NULL DEFAULT 'core',
                id         integer     NOT NULL,
                name       text        NOT NULL,
                checksum   text        NOT NULL,
                applied_at timestamptz NOT NULL,
                PRIMARY KEY (set_name, id)
            );
            """;

        // Idempotent: a fresh database's __migrations already has this shape
        // (both statements no-op there). An existing pre-phase-67 table gets
        // the column backfilled to 'core' and its single-column primary key
        // widened. Runs as its own command — see SqlDialect.UpgradeMigrationsTableAsync.
        UpgradeMigrationsTable = $"""
            ALTER TABLE {Schema}.__migrations ADD COLUMN IF NOT EXISTS set_name text NOT NULL DEFAULT 'core';

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = '__migrations_pkey'
                      AND conrelid = '{Schema}.__migrations'::regclass
                      AND cardinality(conkey) = 1
                ) THEN
                    ALTER TABLE {Schema}.__migrations DROP CONSTRAINT __migrations_pkey;
                    ALTER TABLE {Schema}.__migrations ADD CONSTRAINT __migrations_pkey PRIMARY KEY (set_name, id);
                END IF;
            END $$;
            """;

        UpsertTenant = $"""
            INSERT INTO {Schema}.tenants (id, slug, display_name, created_at)
            VALUES (@id, @slug, @display_name, @created_at)
            ON CONFLICT (slug) DO NOTHING;
            """;

        // --- Agent definitions ---

        UpsertAgentDefinition = $"""
            INSERT INTO {Schema}.agent_definitions (id, tenant_id, name, version, definition, created_at, updated_at)
            VALUES (@id, @tenant_id, @name, 1, @definition, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET version    = {Schema}.agent_definitions.version + 1,
                    definition = EXCLUDED.definition,
                    updated_at = EXCLUDED.updated_at
            RETURNING id, version;
            """;

        // --- Skills ---

        SelectAgentSkills = $"""
            SELECT id, tenant_id, name, description, instructions, compatibility, license,
                   allowed_tools, metadata, enabled, version, created_at, updated_at
            FROM {Schema}.agent_skills
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectAgentSkill = $"""
            SELECT id, tenant_id, name, description, instructions, compatibility, license,
                   allowed_tools, metadata, enabled, version, created_at, updated_at
            FROM {Schema}.agent_skills
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        UpsertAgentSkill = $"""
            INSERT INTO {Schema}.agent_skills
                (id, tenant_id, name, description, instructions, compatibility, license,
                 allowed_tools, metadata, enabled, version, created_at, updated_at)
            VALUES
                (@id, @tenant_id, @name, @description, @instructions, @compatibility, @license,
                 @allowed_tools, @metadata, @enabled, 1, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET description   = EXCLUDED.description,
                    instructions  = EXCLUDED.instructions,
                    compatibility = EXCLUDED.compatibility,
                    license       = EXCLUDED.license,
                    allowed_tools = EXCLUDED.allowed_tools,
                    metadata      = EXCLUDED.metadata,
                    enabled       = EXCLUDED.enabled,
                    version       = {Schema}.agent_skills.version + 1,
                    updated_at    = EXCLUDED.updated_at
            RETURNING id, tenant_id, name, description, instructions, compatibility, license,
                      allowed_tools, metadata, enabled, version, created_at, updated_at;
            """;

        // --- Skill scripts ---

        // --- Script execution grants ---

        SelectSkillScriptGrants = $"""
            SELECT id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at
            FROM {Schema}.skill_script_grants
            WHERE tenant_id = @tenant_id
            ORDER BY skill_name, COALESCE(script_name, '');
            """;

        // A narrow grant wins over a broad one: rows with script_name IS NOT NULL
        // sort first so the script-specific row comes first, and one row is taken.
        SelectActiveSkillScriptGrant = $"""
            SELECT id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at
            FROM {Schema}.skill_script_grants
            WHERE tenant_id = @tenant_id
              AND skill_name = @skill_name
              AND (script_name IS NULL OR script_name = @script_name)
              AND revoked_at IS NULL
              AND (expires_at IS NULL OR expires_at > @instant)
            ORDER BY (script_name IS NULL)
            LIMIT 1;
            """;

        UpsertSkillScriptGrant = $"""
            INSERT INTO {Schema}.skill_script_grants
                (id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at)
            VALUES
                (@id, @tenant_id, @skill_name, @script_name, @granted_by, @granted_at, @expires_at, NULL)
            ON CONFLICT (tenant_id, skill_name, COALESCE(script_name, '')) DO UPDATE
                SET granted_by = EXCLUDED.granted_by,
                    granted_at = EXCLUDED.granted_at,
                    expires_at = EXCLUDED.expires_at,
                    revoked_at = NULL
            RETURNING id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at;
            """;

        // A grant is NOT DELETED, it is revoked: the question "who granted access
        // when, and when was it withdrawn" must stay answerable outside the audit log.
        RevokeSkillScriptGrant = $"""
            UPDATE {Schema}.skill_script_grants
            SET revoked_at = @revoked_at
            WHERE tenant_id = @tenant_id
              AND skill_name = @skill_name
              AND COALESCE(script_name, '') = COALESCE(@script_name, '')
              AND revoked_at IS NULL;
            """;

        // --- Sessions ---

        // 🚨 `version` ADVANCES here; it is never taken from the incoming
        // row. This statement does not check the caller's generation, so
        // letting the caller's (possibly stale) value land would let a later
        // conditional update match a generation that no longer describes the
        // stored state.
        // 🚨 owner_id is COALESCEd, never assigned from EXCLUDED (K-486's
        // rule): a session's owner is claimed once, and "set -> unset" is
        // never a legitimate transition for it. An unconditional save that
        // carries no owner - a background continuation, a worker with no
        // request behind it - would otherwise clear the column and drop the
        // session out of its owner's listing forever. The in-memory store
        // holds the same rule a second time.
        UpsertSession = $"""
            INSERT INTO {Schema}.sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at, version, state_maf_version, owner_id)
            VALUES (@id, @tenant_id, @agent_name, @state, @state_schema_version, @created_at, @updated_at, 1, @state_maf_version, @owner_id)
            ON CONFLICT (tenant_id, id) DO UPDATE
                SET agent_name           = EXCLUDED.agent_name,
                    state                = EXCLUDED.state,
                    state_schema_version = EXCLUDED.state_schema_version,
                    state_maf_version    = EXCLUDED.state_maf_version,
                    updated_at           = EXCLUDED.updated_at,
                    owner_id             = COALESCE({Schema}.sessions.owner_id, EXCLUDED.owner_id),
                    version              = {Schema}.sessions.version + 1;
            """;

        // 🚨 The owner predicate sits in the WHERE clause, ahead of OFFSET and
        // LIMIT, so paging runs over the ALREADY narrowed set. Filtering after
        // the page was cut would return short pages and let a caller count
        // other users' sessions from the gaps. `owner_id = @owner_id` also
        // matches no unowned row on its own - NULL = anything is unknown in
        // SQL - which is exactly the wanted semantics: an unowned session is
        // nobody's, not everybody's.
        SelectSessions = $"""
            SELECT id, agent_name, state, state_schema_version, created_at, updated_at, tenant_id, version, state_maf_version, owner_id
            FROM {Schema}.sessions
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@owner_id IS NULL OR owner_id = @owner_id)
            ORDER BY updated_at DESC
            OFFSET @skip LIMIT @take;
            """;

        // --- Runs ---

        // 🚨 Phase 46: this is an UPSERT. A queued run is first written as
        // Queued; when the worker actually runs the job it is called a second
        // time with the SAME id and the row is updated in place (no new row
        // is OPENED).
        InsertRun = $"""
            INSERT INTO {Schema}.runs (id, tenant_id, agent_name, session_id, model_id, model_provider, status, started_at, is_streaming, event_count,
                                       parent_run_id, root_run_id, depth, kind, workflow_name, agent_version, experiment_id, variant,
                                       replay_of_run_id, user_id, labels, continued_from_run_id)
            VALUES (@id, @tenant_id, @agent_name, @session_id, @model_id, @model_provider, @status, @started_at, @is_streaming, 0,
                    @parent_run_id, @root_run_id, @depth, @kind, @workflow_name, @agent_version, @experiment_id, @variant,
                    @replay_of_run_id, @user_id, @labels, @continued_from_run_id)
            ON CONFLICT (id) DO UPDATE SET
                tenant_id     = EXCLUDED.tenant_id,
                agent_name    = EXCLUDED.agent_name,
                session_id    = EXCLUDED.session_id,
                model_id      = EXCLUDED.model_id,
                model_provider = EXCLUDED.model_provider,
                status        = EXCLUDED.status,
                started_at    = EXCLUDED.started_at,
                is_streaming  = EXCLUDED.is_streaming,
                parent_run_id = EXCLUDED.parent_run_id,
                root_run_id   = EXCLUDED.root_run_id,
                depth         = EXCLUDED.depth,
                kind          = EXCLUDED.kind,
                workflow_name = EXCLUDED.workflow_name,
                agent_version = EXCLUDED.agent_version,
                experiment_id = EXCLUDED.experiment_id,
                variant       = EXCLUDED.variant,
                replay_of_run_id = EXCLUDED.replay_of_run_id,
                continued_from_run_id = EXCLUDED.continued_from_run_id,
                -- 🚨 COALESCE, not a plain overwrite. This is an UPSERT (phase
                -- 46): a queued run's placeholder row is written during the HTTP
                -- request, where the user IS known, and rewritten later by the
                -- background worker, where it is NOT (an HTTP-bound
                -- IRunAttributionContext has no request to read). A plain
                -- overwrite would ERASE the attribution the first write got
                -- right. Attribution never legitimately goes from set back to
                -- unset for the same run, so preserving is always correct.
                user_id       = COALESCE(EXCLUDED.user_id, {Schema}.runs.user_id),
                labels        = COALESCE(EXCLUDED.labels, {Schema}.runs.labels)
            RETURNING user_id, labels;
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
                input_price_per_mtok = @input_price_per_mtok,
                output_cost    = @output_cost,
                output_price_per_mtok = @output_price_per_mtok,
                cached_input_cost = @cached_input_cost,
                cached_input_price_per_mtok = @cached_input_price_per_mtok,
                cost_currency  = @cost_currency,
                pricing_source = @pricing_source,
                model_id       = COALESCE(@model_id, model_id),
                model_provider = COALESCE(@model_provider, model_provider)
            WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        // Used only by the maintenance endpoint (POST /api/stats/recalculate-costs);
        // in the normal flow the cost is written once by UpdateRunCompletion. Never
        // touches model_provider: recalculation resolves the cost of an EXISTING
        // provider attribution, it does not change it.
        UpdateRunCost = $"""
            UPDATE {Schema}.runs
            SET input_cost     = @input_cost,
                input_price_per_mtok = @input_price_per_mtok,
                output_cost    = @output_cost,
                output_price_per_mtok = @output_price_per_mtok,
                cached_input_cost = @cached_input_cost,
                cached_input_price_per_mtok = @cached_input_price_per_mtok,
                cost_currency  = @cost_currency,
                pricing_source = @pricing_source
            WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        // Orphaned run reconciliation (Phase 54). Affects only Running rows;
        // for an id that does not exist or is in another status it silently
        // updates zero rows (a maintenance signal, not a thrown error).
        TouchRunHeartbeat = $"""
            UPDATE {Schema}.runs
            SET heartbeat_at = @at
            WHERE id = @id AND status = @status_running;
            """;

        // In one statement: candidate selection (falls back to started_at when
        // heartbeat_at is absent), closing the row, AND writing the error
        // fields. error_message is derived from the row's OWN heartbeat_at/
        // started_at value; error_fingerprint is a FIXED string ("orphaned")
        // -- NOT a SHA-256 hash, because every orphaned run is the same
        // failure, and this is how behavioral parity with InMemoryRunStore is
        // achieved (ErrorFingerprint is internal in AgentPrism.Core and cannot
        // be reached from this assembly). Queued rows never match the
        // status=@status_running filter (the Phase 46 ownership split).
        ClaimOrphanedRuns = $"""
            UPDATE {Schema}.runs
            SET status            = @status_failed,
                completed_at      = @now,
                error_type        = 'orphaned',
                error_message     = 'The process running this run is not responding; last heartbeat: '
                                     || COALESCE(heartbeat_at, started_at)::text || '.',
                error_class       = @error_class,
                error_fingerprint = @error_fingerprint,
                event_count       = event_count + 1
            WHERE id IN (
                SELECT id FROM {Schema}.runs
                WHERE status = @status_running
                  AND COALESCE(heartbeat_at, started_at) < @stale_before
                ORDER BY COALESCE(heartbeat_at, started_at) ASC
                LIMIT @max
            )
            RETURNING id, tenant_id, agent_name, session_id, status, started_at, completed_at, is_streaming,
                      model_id, kind, workflow_name, agent_version, experiment_id, variant, replay_of_run_id,
                      parent_run_id, root_run_id, depth, event_count, error_type, error_message, error_class,
                      error_fingerprint, continued_from_run_id;
            """;

        // Tree totals are computed ON READ, not stored. Storing them would
        // force every ancestor record to be updated on each child run's
        // completion, and the write path would grow more expensive with
        // depth. The LATERAL subquery runs over runs_parent_idx and
        // runs_root_idx; it is evaluated for at most `take` rows per page.
        var treeJoin = $"""
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::int AS child_count
                FROM {Schema}.runs AS child
                WHERE child.parent_run_id = r.id
            ) AS children ON TRUE
            LEFT JOIN LATERAL (
                SELECT {TreeSum("input_tokens", "sub", true)}::bigint  AS input_tokens,
                       {TreeSum("output_tokens", "sub", true)}::bigint AS output_tokens,
                       {TreeSum("total_tokens", "sub", true)}::bigint  AS total_tokens,
                       COUNT(sub.total_tokens)::bigint             AS usage_rows,
                       -- 🚨 NOT COALESCE'd to zero, unlike the three above: a
                       -- descendant tree in which nobody reported cache usage
                       -- must read as "not measured", not "measured zero".
                       {TreeSum("cached_input_tokens", "sub", false)}::bigint        AS cached_input_tokens,
                       {TreeSum("reasoning_tokens", "sub", false)}::bigint           AS reasoning_tokens,
                       {TreeSum("audio_input_tokens", "sub", false)}::bigint         AS audio_input_tokens,
                       {TreeSum("audio_output_tokens", "sub", false)}::bigint        AS audio_output_tokens,
                       SUM(sub.input_cost)                         AS cost_input,
                       SUM(sub.output_cost)                        AS cost_output,
                       SUM(sub.cached_input_cost)                  AS cost_cached_input,
                       MAX(sub.cost_currency)                      AS cost_currency,
                       COUNT(*) FILTER (WHERE sub.pricing_source = 2)::bigint AS unknown_pricing_rows,
                       COUNT(sub.pricing_source)::bigint           AS pricing_rows
                FROM {Schema}.runs AS sub
                WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id
            ) AS tree ON TRUE
            """;

        // Phase 68 attribution filter, written ONCE and reused by the run list
        // and by every result set of the statistics query. Repeating it by hand
        // in eight places is how the copies drift apart.
        //
        // 🚨 Both shapes are GIN-servable by the jsonb_ops index on `labels`:
        // jsonb_exists is the function form of `?` (the operator itself is
        // avoided so no driver can mistake it for a parameter placeholder), and
        // `@>` is containment. A NULL labels column makes both return NULL, so a
        // run carrying no labels never matches a label filter.
        static string AttributionFilter(string prefix) => $"""
                      AND (@user_id IS NULL OR {prefix}user_id = @user_id)
                      AND (@label_key IS NULL
                           OR (jsonb_exists({prefix}labels, @label_key)
                               AND (@label_value IS NULL
                                    OR {prefix}labels @> jsonb_build_object(@label_key, @label_value))))
            """;

        var runListAttributionFilter = AttributionFilter("r.");
        var statisticsAttributionFilter = AttributionFilter(string.Empty);

        // 🚨 New columns are ALWAYS appended at the end, never inserted in
        // between: the reader (PostgresRunStore.ReadRun) reads by fixed
        // ordinal position.
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
            r.model_provider, r.input_price_per_mtok, r.output_price_per_mtok, r.cached_input_price_per_mtok
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
                    -- equality combined with "includeChildren=true" would
                    -- match no child run at all -- a row also matches when
                    -- its own tree's ROOT belongs to this session.
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
                    -- DELIBERATELY ignored: the two are logically contradictory,
                    -- and silently returning an empty list is a behavior
                    -- that is hard to debug.
                    (@parent_run_id IS NOT NULL AND r.parent_run_id = @parent_run_id)
                 OR (@parent_run_id IS NULL AND (NOT @only_root_runs OR r.parent_run_id IS NULL))
              )
            ORDER BY r.started_at DESC, r.id DESC
            OFFSET @skip LIMIT @take;
            """;

        // Two result sets come back in a single round trip: first the overall
        // summary, then the per-agent breakdown. Status values are not
        // embedded as bare numbers; they arrive as parameters from the
        // RunStatus enum, so the link between the enum and the SQL stays
        // explicit.
        // kind <> @kind_eval: eval case runs are synthetic test calls, not
        // real traffic; they are excluded so they do not pollute the summary
        // (docs/arsiv/fazlar/18-DEGERLENDIRME.md, open question 4).
        // scored_runs/positive_rate (Phase 31): matched_run_scores repeats the
        // SAME filter as runs (tenant/eval/agent/date) -- it is added as a
        // scalar subquery instead of moving it into a separate CTE so the
        // existing total row is extended with the smallest possible change.
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

        SelectRunStatistics = $"""
            SELECT COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_completed)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_failed)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_canceled)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_running)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_awaiting)::bigint,
                   {TreeSum("input_tokens", null, true)}::bigint,
                   {TreeSum("output_tokens", null, true)}::bigint,
                   {TreeSum("total_tokens", null, true)}::bigint,
                   -- 🚨 cached_input_cost is a THIRD ADDEND, not a subset of
                   -- input_cost: the resolver already subtracted the cached
                   -- tokens out of input_cost. Omitting it under-reports every
                   -- run that hit the prompt cache.
                   CASE WHEN {CountWhereAnyNotNull(CostAddends, null)} = 0
                        THEN NULL ELSE {CostTotal(null)} END,
                   MAX(cost_currency),
                   COUNT(*) FILTER (WHERE pricing_source = @pricing_source_unknown)::bigint,
                   (SELECT COUNT(DISTINCT rs.run_id) FROM {matchedRunScoresFilter})::bigint,
                   -- 🚨 `rs.value IS NOT NULL` is in the DENOMINATOR too, not
                   -- only implied by the numerator: a null value records that
                   -- NO MEASUREMENT was made, not a negative one. Counting it
                   -- below the line would report a run scored only positively
                   -- as half positive.
                   (SELECT CASE WHEN COUNT(*) FILTER (WHERE rs.kind = @kind_binary AND rs.value IS NOT NULL) = 0 THEN NULL
                                ELSE (COUNT(*) FILTER (WHERE rs.kind = @kind_binary AND rs.value = 1))::float8
                                     / COUNT(*) FILTER (WHERE rs.kind = @kind_binary AND rs.value IS NOT NULL)
                           END
                    FROM {matchedRunScoresFilter}),
                   -- Ordinals 14-17, APPENDED so the reader's fixed positions
                   -- above do not move. These four are counted INSIDE the
                   -- input/output totals and are reported beside them.
                   {TreeSum("cached_input_tokens", null, true)}::bigint,
                   {TreeSum("reasoning_tokens", null, true)}::bigint,
                   {TreeSum("audio_input_tokens", null, true)}::bigint,
                   {TreeSum("audio_output_tokens", null, true)}::bigint
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter};

            SELECT agent_name,
                   COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_failed)::bigint,
                   {TreeSum("total_tokens", null, true)}::bigint
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter}
            GROUP BY agent_name
            ORDER BY COUNT(*) DESC, agent_name
            LIMIT @max_agents;

            SELECT model_id,
                   COUNT(*)::bigint,
                   {TreeSum("input_tokens", null, true)}::bigint,
                   {TreeSum("output_tokens", null, true)}::bigint,
                   {TreeSum("total_tokens", null, true)}::bigint,
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
            ORDER BY COUNT(*) DESC, model_id
            LIMIT @max_agents;

            SELECT agent_name,
                   agent_version,
                   COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_failed)::bigint,
                   {TreeSum("total_tokens", null, true)}::bigint
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND agent_version IS NOT NULL
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter}
            GROUP BY agent_name, agent_version
            ORDER BY agent_name, agent_version DESC
            LIMIT @max_agents;

            -- Fifth result set: error class breakdown (Phase 44). Rows where
            -- error_class is NULL (written before the error class column
            -- existed) fall into the Unknown (0) bucket -- K-014 does not
            -- backfill.
            SELECT COALESCE(error_class, 0)::smallint,
                   COUNT(*)::bigint
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND status = @status_failed
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            {statisticsAttributionFilter}
            GROUP BY COALESCE(error_class, 0)
            ORDER BY COUNT(*) DESC;

            -- Sixth result set: the most frequent fingerprint clusters per
            -- class. Two window-function passes: `failed` tags each row with
            -- its cluster's count/last-seen time and marks the newest row to
            -- sample (sample_rank); `ranked` orders the clusters by count
            -- within each class.
            WITH failed AS (
                SELECT COALESCE(error_class, 0)::smallint AS error_class,
                       COALESCE(error_fingerprint, '') AS error_fingerprint,
                       error_message,
                       id,
                       started_at,
                       COUNT(*) OVER (
                           PARTITION BY COALESCE(error_class, 0), COALESCE(error_fingerprint, '')
                       )::bigint AS cluster_count,
                       MAX(started_at) OVER (
                           PARTITION BY COALESCE(error_class, 0), COALESCE(error_fingerprint, '')
                       ) AS last_seen_at,
                       ROW_NUMBER() OVER (
                           PARTITION BY COALESCE(error_class, 0), COALESCE(error_fingerprint, '')
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

            -- Seventh result set: breakdown by user (phase 68). Runs carrying no
            -- user stay OUT of this list but remain in the totals, exactly as
            -- runs with an unknown model do -- otherwise the two figures would
            -- disagree. APPENDED after the existing sets so their positions in
            -- SqlRunStore hold.
            SELECT user_id,
                   COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_failed)::bigint,
                   {TreeSum("total_tokens", null, true)}::bigint,
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
            ORDER BY COUNT(*) DESC, user_id
            LIMIT @max_agents;

            -- Eighth result set: breakdown by label. jsonb_each_text expands the
            -- map, so a run carrying three labels contributes to THREE rows.
            -- 🚨 These rows therefore do NOT sum to TotalRuns, unlike every other
            -- breakdown: a label set is not a partition of the runs.
            SELECT kv.key,
                   kv.value,
                   COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE r.status = @status_failed)::bigint,
                   {TreeSum("total_tokens", "r", true)}::bigint,
                   CASE WHEN {CountWhereAnyNotNull(CostAddends, "r")} = 0
                        THEN NULL ELSE {CostTotal("r")} END
            FROM {Schema}.runs AS r
            CROSS JOIN LATERAL jsonb_each_text(r.labels) AS kv(key, value)
            WHERE r.tenant_id = @tenant_id
              AND r.kind <> @kind_eval
              AND r.labels IS NOT NULL
              AND (@agent_name IS NULL OR r.agent_name = @agent_name)
              AND (@started_after IS NULL OR r.started_at > @started_after)
            {runListAttributionFilter}
            GROUP BY kv.key, kv.value
            ORDER BY COUNT(*) DESC, kv.key, kv.value
            LIMIT @max_agents;
            """;

        // --- Conversations (chat history) ---

        // ON CONFLICT DO UPDATE locks the conversation row, so two
        // transactions writing to the same conversation concurrently wait in
        // turn for the sequence number. The metadata column is not written;
        // the schema default is an empty jsonb object.
        UpsertConversation = $"""
            INSERT INTO {Schema}.conversations (id, tenant_id, agent_name, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @now, @now)
            ON CONFLICT (id) DO UPDATE
                SET updated_at = EXCLUDED.updated_at;
            """;

        // --- Conversation branching (Phase 47) ---

        // --- Run inputs (Phase 47) ---

        // 🚨 The `messages` column is `json`, NOT `jsonb`: ChatMessage
        // contents are polymorphic and the `$type` discriminator must be the
        // FIRST property of the object (K-027). A second write is ignored: a
        // queued run (Phase 46) starts twice with the same id, and its input
        // must not change.
        InsertRunInput = $"""
            INSERT INTO {Schema}.run_inputs (run_id, tenant_id, messages, created_at)
            VALUES (@run_id, @tenant_id, @messages, @created_at)
            ON CONFLICT (run_id) DO NOTHING;
            """;

        // --- Tool invocations (Phase 6) ---

        // Average duration is computed only over calls that carry a
        // duration: AVG already skips NULL values, so the denominator is correct.
        SelectToolUsage = $"""
            SELECT t.tool_name,
                   COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE t.error IS NOT NULL)::bigint,
                   AVG(t.duration_ms)::double precision,
                   MAX(t.created_at)
            FROM {Schema}.tool_invocations t
            JOIN {Schema}.runs r ON r.id = t.run_id
            WHERE r.tenant_id = @tenant_id
              AND (@started_after IS NULL OR r.started_at > @started_after)
            GROUP BY t.tool_name
            ORDER BY COUNT(*) DESC, t.tool_name
            LIMIT @max_tools;
            """;

        // --- Experiments (Phase 19) ---

        SelectExperiments = $"""
            SELECT id, tenant_id, name, agent_name, variants, status, assignment_key, started_at, ended_at, updated_at,
                   canary_policy, rollback_reason
            FROM {Schema}.experiments
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectExperiment = $"""
            SELECT id, tenant_id, name, agent_name, variants, status, assignment_key, started_at, ended_at, updated_at,
                   canary_policy, rollback_reason
            FROM {Schema}.experiments
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        SelectRunningExperiment = $"""
            SELECT id, tenant_id, name, agent_name, variants, status, assignment_key, started_at, ended_at, updated_at,
                   canary_policy, rollback_reason
            FROM {Schema}.experiments
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND status = 1;
            """;

        SelectRunningExperimentsWithCanary = $"""
            SELECT id, tenant_id, name, agent_name, variants, status, assignment_key, started_at, ended_at, updated_at,
                   canary_policy, rollback_reason
            FROM {Schema}.experiments
            WHERE status = 1 AND canary_policy IS NOT NULL;
            """;

        // An attempt to update a non-Draft experiment returns 0 rows; the
        // caller distinguishes this beforehand with GetAsync and raises a
        // meaningful error. canary_policy/rollback_reason are DELIBERATELY
        // absent from the SET list: a Draft edit (SaveAsync) must not erase a
        // rule already defined by SetCanaryPolicyAsync.
        UpsertExperiment = $"""
            INSERT INTO {Schema}.experiments (id, tenant_id, name, agent_name, variants, status, assignment_key, updated_at)
            VALUES (@id, @tenant_id, @name, @agent_name, @variants, 0, @assignment_key, @updated_at)
            ON CONFLICT (tenant_id, name) DO UPDATE SET
                agent_name     = EXCLUDED.agent_name,
                variants       = EXCLUDED.variants,
                assignment_key = EXCLUDED.assignment_key,
                updated_at     = EXCLUDED.updated_at
            WHERE {Schema}.experiments.status = 0
            RETURNING id;
            """;

        StartExperiment = $"""
            UPDATE {Schema}.experiments
            SET status = 1, started_at = @now, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name AND status = 0
            RETURNING id;
            """;

        StopExperiment = $"""
            UPDATE {Schema}.experiments
            SET status = 2, ended_at = @now, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name AND status = 1
            RETURNING id;
            """;

        // Runs INDEPENDENTLY of status (Draft or Running) — unlike SaveAsync.
        SetExperimentCanaryPolicy = $"""
            UPDATE {Schema}.experiments
            SET canary_policy = @canary_policy, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name
            RETURNING id;
            """;

        AdvanceExperimentCanaryRamp = $"""
            UPDATE {Schema}.experiments
            SET variants = @variants, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name AND status = 1
            RETURNING id;
            """;

        RollbackExperimentCanary = $"""
            UPDATE {Schema}.experiments
            SET variants = @variants, status = 2, ended_at = @now, rollback_reason = @rollback_reason, updated_at = @now
            WHERE tenant_id = @tenant_id AND name = @name AND status = 1
            RETURNING id;
            """;

        // run_avg_scores: FIRST the per-run average (independent of the
        // number of authors/judges), THEN the average of these averages per
        // variant. Joining run_scores directly (a run can have more than one
        // score row) would also DUPLICATE every other total in the GROUP BY
        // variant (tokens, cost, counts) — so it is first reduced to the
        // run level in a SEPARATE CTE.
        SelectExperimentResults = $"""
            WITH run_avg_scores AS (
                SELECT run_id, AVG(value) AS avg_score
                FROM {Schema}.run_scores
                WHERE tenant_id = @tenant_id AND kind = @score_kind_numeric AND message_id IS NULL
                GROUP BY run_id
            )
            SELECT r.variant,
                   MAX(r.agent_version)::int,
                   COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE r.status = @status_completed)::bigint,
                   COUNT(*) FILTER (WHERE r.status = @status_failed)::bigint,
                   COUNT(*) FILTER (WHERE r.status = @status_canceled)::bigint,
                   {TreeSum("input_tokens", "r", true)}::bigint,
                   {TreeSum("output_tokens", "r", true)}::bigint,
                   {TreeSum("total_tokens", "r", true)}::bigint,
                   AVG(EXTRACT(EPOCH FROM (r.completed_at - r.started_at)) * 1000) FILTER (WHERE r.completed_at IS NOT NULL),
                   CASE WHEN {CountWhereAnyNotNull(CostAddends, "r")} = 0
                        THEN NULL ELSE {CostTotal("r")} END,
                   MAX(r.cost_currency),
                   AVG(s.avg_score)
            FROM {Schema}.runs r
            LEFT JOIN run_avg_scores s ON s.run_id = r.id
            WHERE r.tenant_id = @tenant_id AND r.experiment_id = @experiment_id AND r.variant IS NOT NULL
            GROUP BY r.variant;
            """;

        // 🚨 date_trunc(unit, timestamptz) depends on the SESSION time zone --
        // a non-UTC `TimeZone` setting would silently shift every bucket
        // boundary relative to SQLite/SQL Server/the in-memory store, all of
        // which truncate in UTC unconditionally (measured, Phase 154). The
        // "AT TIME ZONE 'UTC'" round trip forces UTC regardless of the
        // session setting: the first leg reads the instant as UTC wall-clock
        // time (timestamptz -> timestamp, no zone left to apply), date_trunc
        // truncates that wall-clock value, and the second leg re-attaches UTC
        // (timestamp -> timestamptz) so the result stays comparable to every
        // other timestamptz column and parameter.
        static string TruncateUtc(string bucketUnitParam, string column)
            => $"(date_trunc({bucketUnitParam}, {column} AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')";

        // Empty buckets are also returned (generate_series + LEFT JOIN):
        // otherwise a gap in the chart would look like "zero" instead of "no
        // data". Eval/Workflow runs are DELIBERATELY NOT excluded — unlike
        // SelectRunStatistics (see docs/KARARLAR.md K-152); they are filtered
        // if @kind is given.
        SelectRunTimeSeries = $"""
            WITH buckets AS (
                -- The upper bound is inclusive (Truncate(to_ts) included), then
                -- filtered by bucket < to_ts: if to_ts lands exactly on a bucket
                -- boundary, that bucket (which can never contain a row where
                -- `started_at < to_ts`) is dropped. The same rule, exactly, as
                -- the `cursor < To` loop in InMemoryRunStore.
                SELECT bucket
                FROM generate_series(
                    {TruncateUtc("@bucket_unit", "@from_ts")},
                    {TruncateUtc("@bucket_unit", "@to_ts")},
                    @bucket_step) AS bucket
                WHERE bucket < @to_ts
            ),
            matched AS (
                SELECT {TruncateUtc("@bucket_unit", "started_at")} AS bucket,
                       COUNT(*)::bigint AS runs,
                       COUNT(*) FILTER (WHERE status = @status_failed)::bigint AS failed_runs,
                       {TreeSum("input_tokens", null, true)}::bigint AS input_tokens,
                       {TreeSum("output_tokens", null, true)}::bigint AS output_tokens,
                       CASE WHEN {CountWhereAnyNotNull(CostAddends, null)} = 0
                            THEN NULL ELSE {CostTotal(null)} END AS cost,
                       AVG(EXTRACT(EPOCH FROM (completed_at - started_at)) * 1000)
                           FILTER (WHERE completed_at IS NOT NULL) AS avg_duration_ms
                FROM {Schema}.runs
                WHERE tenant_id = @tenant_id
                  AND started_at >= @from_ts AND started_at < @to_ts
                  AND (@agent_name IS NULL OR agent_name = @agent_name)
                  AND (@model_id   IS NULL OR model_id   = @model_id)
                  AND (@kind       IS NULL OR kind       = @kind)
                GROUP BY {TruncateUtc("@bucket_unit", "started_at")}
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
            ORDER BY buckets.bucket;
            """;

        // --- Spans (Phase 6) ---

        // A trace header is unique on the (tenant, W3C id) pair; a second
        // write for the same run just updates the header.
        UpsertTrace = $"""
            INSERT INTO {Schema}.traces (id, tenant_id, trace_id, run_id, started_at, ended_at)
            VALUES (@id, @tenant_id, @trace_id, @run_id, @started_at, @ended_at)
            ON CONFLICT (tenant_id, trace_id) DO UPDATE
                SET run_id     = COALESCE(EXCLUDED.run_id, {Schema}.traces.run_id),
                    started_at = LEAST({Schema}.traces.started_at, EXCLUDED.started_at),
                    ended_at   = GREATEST({Schema}.traces.ended_at, EXCLUDED.ended_at)
            RETURNING id;
            """;

        // The span id is derived from W3C ids, so writing the same span twice
        // conflicts and updates the row; no duplicate record is created.
        UpsertSpan = $"""
            INSERT INTO {Schema}.spans
                (id, trace_id, parent_span_id, span_id, name, kind, started_at, ended_at, attributes, status)
            VALUES
                (@id, @trace_id, @parent_span_id, @span_id, @name, @kind, @started_at, @ended_at, @attributes, @status)
            ON CONFLICT (id) DO UPDATE
                SET ended_at   = EXCLUDED.ended_at,
                    attributes = EXCLUDED.attributes,
                    status     = EXCLUDED.status;
            """;

        // --- Tool approval rules (Phase 6) ---

        // A second rule for the same scope is not opened; the existing
        // record is returned. The constraint is a COALESCE'd expression
        // index because NULLs are not considered equal to each other in
        // PostgreSQL, and a plain UNIQUE constraint would not prevent the duplicate.
        // conditions_hash (Phase 63) joined the key so a data-rule condition set
        // is bound by the same "no unbounded duplicates" rule as arguments_hash.
        InsertToolApprovalRule = $"""
            INSERT INTO {Schema}.tool_approval_rules
                (id, tenant_id, agent_name, tool_name, arguments_hash, argument_conditions, conditions_hash, created_by, created_at)
            VALUES (@id, @tenant_id, @agent_name, @tool_name, @arguments_hash, @argument_conditions, @conditions_hash, @created_by, @created_at)
            ON CONFLICT (tenant_id, COALESCE(agent_name, ''), tool_name, COALESCE(arguments_hash, ''), COALESCE(conditions_hash, ''))
                DO UPDATE SET tool_name = {Schema}.tool_approval_rules.tool_name
            RETURNING id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at, argument_conditions;
            """;

        // --- MCP servers (Phase 6) ---

        UpsertMcpServer = $"""
            INSERT INTO {Schema}.mcp_servers
                (id, tenant_id, name, description, endpoint, transport,
                 authorization_configuration_key, headers, enabled, requires_approval, created_at, updated_at,
                 oauth_enabled, oauth_client_id, oauth_client_secret_configuration_key, oauth_scopes, oauth_authorization_mode)
            VALUES
                (@id, @tenant_id, @name, @description, @endpoint, @transport,
                 @authorization_configuration_key, @headers, @enabled, @requires_approval, @now, @now,
                 @oauth_enabled, @oauth_client_id, @oauth_client_secret_configuration_key, @oauth_scopes, @oauth_authorization_mode)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET description                          = EXCLUDED.description,
                    endpoint                             = EXCLUDED.endpoint,
                    transport                             = EXCLUDED.transport,
                    authorization_configuration_key       = EXCLUDED.authorization_configuration_key,
                    headers                                = EXCLUDED.headers,
                    enabled                                = EXCLUDED.enabled,
                    requires_approval                      = EXCLUDED.requires_approval,
                    updated_at                              = EXCLUDED.updated_at,
                    oauth_enabled                           = EXCLUDED.oauth_enabled,
                    oauth_client_id                         = EXCLUDED.oauth_client_id,
                    oauth_client_secret_configuration_key   = EXCLUDED.oauth_client_secret_configuration_key,
                    oauth_scopes                            = EXCLUDED.oauth_scopes,
                    oauth_authorization_mode                = EXCLUDED.oauth_authorization_mode
            RETURNING {McpServerColumns};
            """;

        // --- Tenants (Phase 6) ---

        UpsertTenantDescriptor = $"""
            INSERT INTO {Schema}.tenants (id, slug, display_name, created_at)
            VALUES (@id, @slug, @display_name, @created_at)
            ON CONFLICT (slug) DO UPDATE
                SET display_name = EXCLUDED.display_name
            RETURNING id, slug, display_name, created_at;
            """;

        // --- Attachments (Phase 14) ---

        SelectAttachments = $"""
            SELECT id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256, created_by, created_at
            FROM {Schema}.attachments
            WHERE tenant_id = @tenant_id
              AND (@session_id IS NULL OR session_id = @session_id)
            ORDER BY created_at DESC
            OFFSET @skip LIMIT @take;
            """;

        DeleteAttachment = $"""
            DELETE FROM {Schema}.attachments
            WHERE tenant_id = @tenant_id AND id = @id
            RETURNING external_uri;
            """;

        DeleteAttachmentsBySession = $"""
            DELETE FROM {Schema}.attachments
            WHERE tenant_id = @tenant_id AND session_id = @session_id
            RETURNING external_uri;
            """;

        // --- Persistent agent file memory ---

        UpsertAgentFile = $"""
            INSERT INTO {Schema}.agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @path, @content, @now, @now)
            ON CONFLICT (tenant_id, agent_name, path) DO UPDATE
                SET content    = EXCLUDED.content,
                    updated_at = EXCLUDED.updated_at;
            """;

        // Phase 51, Work Item A: the prefix, depth limit, and glob go down to
        // SQL; PostgreSQL also pushes the regex down as a pre-filter with the
        // `~` operator (the final match is always done client-side with .NET
        // Regex, unchanged).
        SelectAgentFilesFiltered = $"""
            SELECT path, content
            FROM {Schema}.agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name
              AND path LIKE @prefix_like ESCAPE '\'
              AND (@prefix_deep_like IS NULL OR path NOT LIKE @prefix_deep_like ESCAPE '\')
              AND (@name_like IS NULL OR path LIKE @name_like ESCAPE '\')
              AND (@regex_pattern IS NULL OR content ~ @regex_pattern)
            ORDER BY path;
            """;

        // --- Workflows (Phase 15) ---

        UpsertWorkflow = $"""
            INSERT INTO {Schema}.workflows (id, tenant_id, name, version, definition, created_at, updated_at)
            VALUES (@id, @tenant_id, @name, 1, @definition, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET version    = {Schema}.workflows.version + 1,
                    definition = EXCLUDED.definition,
                    updated_at = EXCLUDED.updated_at
            RETURNING version, updated_at;
            """;

        // --- Audit log (Phase 9) ---

        SelectAuditLog = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash
            FROM {Schema}.audit_log
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

        SelectLastAuditHash = $"""
            SELECT hash
            FROM {Schema}.audit_log
            WHERE tenant_id = @tenant_id
            ORDER BY chain_seq DESC
            LIMIT 1;
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

        // --- Scheduling and job queue (Phase 17) ---

        UpsertJobSchedule = $"""
            INSERT INTO {Schema}.job_schedules
                (id, tenant_id, name, handler_key, target_name, cron, time_zone, payload, enabled,
                 next_run_at, last_run_at, created_by, created_at, updated_at, lane)
            VALUES (@id, @tenant_id, @name, @handler_key, @target_name, @cron, @time_zone, @payload, @enabled,
                    @next_run_at, @last_run_at, @created_by, @created_at, @updated_at, @lane)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET handler_key = EXCLUDED.handler_key,
                    target_name = EXCLUDED.target_name,
                    cron        = EXCLUDED.cron,
                    time_zone   = EXCLUDED.time_zone,
                    payload     = EXCLUDED.payload,
                    enabled     = EXCLUDED.enabled,
                    next_run_at = EXCLUDED.next_run_at,
                    last_run_at = EXCLUDED.last_run_at,
                    updated_at  = EXCLUDED.updated_at,
                    lane        = EXCLUDED.lane
            RETURNING id, created_by, created_at;
            """;

        // Not scoped by tenant: this query belongs to the worker, not to an
        // HTTP request.
        SelectDueJobSchedules = $"""
            SELECT {ScheduleColumns}
            FROM {Schema}.job_schedules
            WHERE enabled = TRUE AND cron IS NOT NULL AND next_run_at IS NOT NULL AND next_run_at <= @as_of;
            """;

        // --- Inbound triggers (Phase 66) ---

        UpsertInboundTrigger = $"""
            INSERT INTO {Schema}.inbound_triggers
                (id, tenant_id, name, target_kind, target_name, signing_secret_configuration_name,
                 payload_mode, payload_path, enabled, created_at, updated_at)
            VALUES (@id, @tenant_id, @name, @target_kind, @target_name, @signing_secret_configuration_name,
                    @payload_mode, @payload_path, @enabled, @created_at, @updated_at)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET target_kind                       = EXCLUDED.target_kind,
                    target_name                        = EXCLUDED.target_name,
                    signing_secret_configuration_name  = EXCLUDED.signing_secret_configuration_name,
                    payload_mode                       = EXCLUDED.payload_mode,
                    payload_path                        = EXCLUDED.payload_path,
                    enabled                             = EXCLUDED.enabled,
                    updated_at                          = EXCLUDED.updated_at
            RETURNING id, created_at;
            """;

        // Ids are generated on the C# side (gen_random_uuid() is a dependency
        // that varies by server version); the two arrays are matched via
        // UNNEST using the ordinal position (1-based ord).
        InsertJobItems = $"""
            INSERT INTO {Schema}.job_items (id, job_id, seq, input, status)
            SELECT t.id, @job_id, (t.ord - 1)::int, t.input, 0
            FROM UNNEST(@ids, @inputs) WITH ORDINALITY AS t(id, input, ord);
            """;

        // FOR UPDATE SKIP LOCKED: even if multiple workers connect to the
        // same database, a job is claimed by only one worker. An expired
        // lease (status IN (1, 2) AND lease_until < @now) can also be
        // reclaimed.
        LeaseJob = $"""
            UPDATE {Schema}.jobs
               SET status = 1, lease_owner = @owner, lease_until = @lease_until, attempt = attempt + 1,
                   started_at = COALESCE(started_at, @now)
             WHERE id = (
                   SELECT id FROM {Schema}.jobs
                    WHERE ((status = 0 AND scheduled_for <= @now)
                       OR (status IN (1, 2) AND lease_until < @now))
                      AND (@lanes IS NULL OR lane = ANY(@lanes))
                    ORDER BY scheduled_for
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1)
            RETURNING {JobColumns};
            """;

        SelectJobs = $"""
            SELECT {JobColumns}
            FROM {Schema}.jobs
            WHERE (@tenant_id   IS NULL OR tenant_id   = @tenant_id)
              AND (@handler_key IS NULL OR handler_key = @handler_key)
              AND (@status      IS NULL OR status      = @status)
              AND (@schedule_id IS NULL OR schedule_id = @schedule_id)
              AND (@lane        IS NULL OR lane         = @lane)
            ORDER BY created_at DESC
            OFFSET @skip LIMIT @take;
            """;

        // Idempotent reporting: the `updated` CTE returns a row only if the
        // item is still Pending (0); if the lease expires and the same item
        // is reported twice, the second call does NOT increment the counters
        // AGAIN.
        ReportJobItem = $"""
            WITH updated AS (
                UPDATE {Schema}.job_items
                   SET status = @status, run_id = @run_id, error = @error
                 WHERE job_id = @job_id AND seq = @seq AND status = 0
                RETURNING status
            )
            UPDATE {Schema}.jobs
               SET done_items   = done_items   + (SELECT COUNT(*) FROM updated WHERE status = 1),
                   failed_items = failed_items + (SELECT COUNT(*) FROM updated WHERE status = 2)
             WHERE id = @job_id;
            """;

        // --- Evaluation / eval (Phase 18) ---

        UpsertEvalSuite = $"""
            INSERT INTO {Schema}.eval_suites
                (id, tenant_id, name, description, agent_name, checks, created_at, updated_at)
            VALUES
                (@id, @tenant_id, @name, @description, @agent_name, @checks, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET description = EXCLUDED.description,
                    agent_name  = EXCLUDED.agent_name,
                    checks      = EXCLUDED.checks,
                    updated_at  = EXCLUDED.updated_at
            RETURNING {SuiteColumns};
            """;

        // 🚨 `seq` is generated atomically here BY THE STORE (a MAX+1
        // subquery); the caller does not compute it
        // (docs/arsiv/fazlar/45-URETIMDEN-EVAL-KUMESI.md, section 45.2). Two concurrent
        // promotions can compute the same seq; in that case the
        // eval_cases_suite_seq_uq violation is caught by
        // SqlDialect.IsUniqueViolation and SqlEvalStore retries. A
        // source_run_id conflict (the same run promoted twice) hits the same
        // catch but is interpreted differently: SqlEvalStore reads the
        // existing case with SelectEvalCaseBySourceRun and returns it.
        InsertEvalCaseWithComputedSeq = $"""
            INSERT INTO {Schema}.eval_cases
                (id, suite_id, seq, query, expected_output, expected_tools, context,
                 source_run_id, source_kind, promoted_at, parameters)
            VALUES
                (@id, @suite_id,
                 COALESCE((SELECT MAX(seq) FROM {Schema}.eval_cases WHERE suite_id = @suite_id), -1) + 1,
                 @query, @expected_output, @expected_tools, @context,
                 @source_run_id, @source_kind, @promoted_at, @parameters)
            RETURNING {EvalCaseColumns};
            """;

        InsertEvalRun = $"""
            INSERT INTO {Schema}.eval_runs
                (id, tenant_id, suite_id, job_id, status, total, passed, failed, started_at)
            VALUES (@id, @tenant_id, @suite_id, @job_id, 0, @total, 0, 0, @started_at)
            RETURNING {EvalRunColumns};
            """;

        SelectEvalRuns = $"""
            SELECT {EvalRunColumns}
            FROM {Schema}.eval_runs
            WHERE (@tenant_id IS NULL OR tenant_id = @tenant_id)
              AND (@suite_id  IS NULL OR suite_id  = @suite_id)
            ORDER BY started_at DESC
            OFFSET @skip LIMIT @take;
            """;

        // -------------------------------------------------------------------
        // Phase 21 -- quota
        // -------------------------------------------------------------------

        // 🚨 The conflict target is the expression COALESCE(agent_name, ''),
        // not the column list: PostgreSQL does not count NULLs as equal to
        // each other, and a plain (tenant_id, agent_name, period) target
        // would allow the same rule with agent_name NULL to be added an
        // unlimited number of times. The unique index is also built with the
        // same expression (migration 0012).
        UpsertQuota = $"""
            INSERT INTO {Schema}.quotas
                ({QuotaColumns})
            VALUES
                (@id, @tenant_id, @agent_name, @period, @max_runs, @max_tokens, @max_cost, @enabled,
                 @created_at, @updated_at)
            ON CONFLICT (tenant_id, COALESCE(agent_name, ''), period) DO UPDATE
               SET max_runs   = EXCLUDED.max_runs,
                   max_tokens = EXCLUDED.max_tokens,
                   max_cost   = EXCLUDED.max_cost,
                   enabled    = EXCLUDED.enabled,
                   updated_at = EXCLUDED.updated_at
            RETURNING {QuotaColumns};
            """;

        SelectQuotas = $"""
            SELECT {QuotaColumns}
            FROM {Schema}.quotas
            WHERE tenant_id = @tenant_id
            ORDER BY COALESCE(agent_name, ''), period;
            """;

        // Consumption is incremented ATOMICALLY. Concurrent runs increment
        // the same row and no increment is lost; with a read-modify-write
        // sequence instead, one of two runs finishing at the same time would
        // be silently swallowed.
        AddQuotaUsage = $"""
            INSERT INTO {Schema}.quota_usage
                (tenant_id, agent_name, period, period_start, runs, tokens, cost, updated_at)
            VALUES
                (@tenant_id, @agent_name, @period, @period_start, @runs, @tokens, @cost, @updated_at)
            ON CONFLICT (tenant_id, agent_name, period, period_start) DO UPDATE
               SET runs       = {Schema}.quota_usage.runs   + EXCLUDED.runs,
                   tokens     = {Schema}.quota_usage.tokens + EXCLUDED.tokens,
                   cost       = {Schema}.quota_usage.cost   + EXCLUDED.cost,
                   updated_at = EXCLUDED.updated_at;
            """;

        // 🚨 notified_thresholds is a ',key1,key2,'-delimited list -- comma on
        // BOTH sides of every entry, so a LIKE membership check can never
        // false-positive on a digit-prefix collision. Affected rows = 0 means
        // either the row does not exist yet, or the key is already present --
        // the caller cannot tell which and does not need to.
        TryClaimQuotaThresholdNotification = $"""
            UPDATE {Schema}.quota_usage
               SET notified_thresholds = COALESCE(notified_thresholds, ',') || @key || ','
             WHERE tenant_id = @tenant_id AND agent_name = @agent_name
               AND period = @period AND period_start = @period_start
               AND (notified_thresholds IS NULL OR notified_thresholds NOT LIKE '%,' || @key || ',%');
            """;

        // -------------------------------------------------------------------
        // Phase 21 -- webhook
        // -------------------------------------------------------------------

        UpsertWebhookSubscription = $"""
            INSERT INTO {Schema}.webhook_subscriptions
                ({WebhookSubscriptionColumns})
            VALUES
                (@id, @tenant_id, @name, @url, @events, @secret_configuration_key, @headers, @enabled,
                 @consecutive_failures, @created_at, @updated_at)
            ON CONFLICT (tenant_id, name) DO UPDATE
               SET url                      = EXCLUDED.url,
                   events                   = EXCLUDED.events,
                   secret_configuration_key = EXCLUDED.secret_configuration_key,
                   headers                  = EXCLUDED.headers,
                   enabled                  = EXCLUDED.enabled,
                   updated_at               = EXCLUDED.updated_at
            RETURNING {WebhookSubscriptionColumns};
            """;

        // events is a text[] column; = ANY(...) looks for an exact match and
        // can use an index. A LIKE-based search would wrongly match the
        // 'run.completed.v2' subscription too when looking for
        // 'run.completed'.
        SelectWebhookSubscriptionsForEvent = $"""
            SELECT {WebhookSubscriptionColumns}
            FROM {Schema}.webhook_subscriptions
            WHERE tenant_id = @tenant_id AND enabled = true AND @event_type = ANY (events)
            ORDER BY name;
            """;

        // The consecutive-failure counter and auto-disable are done in a
        // SINGLE statement; a read-modify-write sequence would race with
        // concurrent deliveries. The returned row answers the question "did
        // this call disable the subscription".
        UpdateWebhookSubscriptionOutcome = $"""
            UPDATE {Schema}.webhook_subscriptions
               SET consecutive_failures = CASE WHEN @succeeded THEN 0 ELSE consecutive_failures + 1 END,
                   enabled = CASE
                       WHEN @succeeded THEN enabled
                       WHEN @threshold > 0 AND consecutive_failures + 1 >= @threshold THEN false
                       ELSE enabled
                   END,
                   updated_at = @updated_at
             WHERE id = @id
            RETURNING (NOT enabled) AND @succeeded = false;
            """;

        SelectWebhookDeliveries = $"""
            SELECT {WebhookDeliveryColumns}
            FROM {Schema}.webhook_deliveries
            WHERE tenant_id = @tenant_id
              AND (@subscription_id IS NULL OR subscription_id = @subscription_id)
              AND (@status          IS NULL OR status          = @status)
            ORDER BY created_at DESC
            OFFSET @skip LIMIT @take;
            """;

        // -------------------------------------------------------------------
        // Phase 53 -- tenant-scoped API keys
        // -------------------------------------------------------------------

        InsertApiKey = $"""
            INSERT INTO {Schema}.api_keys
                ({ApiKeyColumns})
            VALUES
                (@id, @tenant_id, @name, @key_hash, @key_prefix, @scopes, @expires_at, @revoked_at,
                 @last_used_at, @created_at);
            """;

        // Setup health check (ExternalSurfaceGuard, section 53.4): the tenant
        // filter is DELIBERATELY absent.
        HasApiKeyWithScope = $"""
            SELECT EXISTS (
                SELECT 1 FROM {Schema}.api_keys
                WHERE revoked_at IS NULL
                  AND (expires_at IS NULL OR expires_at > @now)
                  AND @scope = ANY (scopes)
            );
            """;

        UpsertRetentionPolicy = $"""
            INSERT INTO {Schema}.retention_policies
                ({RetentionPolicyColumns})
            VALUES
                (@id, @tenant_id, @target, @max_age_days, @max_rows, @archive, @enabled, @created_at, @updated_at)
            ON CONFLICT (tenant_id, target) DO UPDATE
               SET max_age_days = EXCLUDED.max_age_days,
                   max_rows     = EXCLUDED.max_rows,
                   archive      = EXCLUDED.archive,
                   enabled      = EXCLUDED.enabled,
                   updated_at   = EXCLUDED.updated_at
            RETURNING {RetentionPolicyColumns};
            """;

        SelectRetentionRuns = $"""
            SELECT {RetentionRunColumns}
            FROM {Schema}.retention_runs
            WHERE tenant_id = @tenant_id
              AND (@target IS NULL OR target = @target)
            ORDER BY started_at DESC
            OFFSET @skip LIMIT @take;
            """;
        const string voiceSessionColumns =
            "id, tenant_id, session_id, agent_name, started_at, ended_at, turns, input_seconds, output_chars, end_reason, created_by";

        UpsertVoiceSession = $"""
            INSERT INTO {Schema}.voice_sessions
                ({voiceSessionColumns})
            VALUES
                (@id, @tenant_id, @session_id, @agent_name, @started_at, @ended_at, @turns, @input_seconds, @output_chars, @end_reason, @created_by)
            ON CONFLICT (id) DO UPDATE
               SET ended_at      = EXCLUDED.ended_at,
                   turns         = EXCLUDED.turns,
                   input_seconds = EXCLUDED.input_seconds,
                   output_chars  = EXCLUDED.output_chars,
                   end_reason    = EXCLUDED.end_reason;
            """;

        SelectVoiceSessions = $"""
            SELECT {voiceSessionColumns}
            FROM {Schema}.voice_sessions
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@session_id IS NULL OR session_id = @session_id)
            ORDER BY started_at DESC
            OFFSET @skip LIMIT @take;
            """;

        // -------------------------------------------------------------------
        // Phase 31 -- run/message score
        // -------------------------------------------------------------------

        // 🚨 The conflict target is the expression COALESCE(message_id, '')
        // (must be IDENTICAL to the uniqueness index in migration 0017);
        // author is a plain column -- when it is NULL no conflict occurs at
        // all and every call opens a new row (K1: no silent uniqueness
        // mechanism is introduced for an identity-less setup).
        UpsertRunScore = $"""
            INSERT INTO {Schema}.run_scores
                ({RunScoreColumns})
            VALUES
                (@id, @tenant_id, @run_id, @message_id, @kind, @value, @comment, @source, @author, @created_at, @name, @text_value)
            ON CONFLICT (tenant_id, run_id, COALESCE(message_id, ''), author, name) DO UPDATE
               SET kind       = EXCLUDED.kind,
                   value      = EXCLUDED.value,
                   text_value = EXCLUDED.text_value,
                   comment    = EXCLUDED.comment,
                   source     = EXCLUDED.source,
                   created_at = EXCLUDED.created_at
            RETURNING {RunScoreColumns};
            """;

        // -------------------------------------------------------------------
        // Phase 154 -- score summary (a query, not a counter; 154.1)
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
                   OR (@target = 1 AND ({prefix}message_id IS NULL OR {prefix}message_id = ''))
                   OR (@target = 2 AND {prefix}message_id IS NOT NULL AND {prefix}message_id <> ''))
            """;

        // The ByAgent query joins `runs` directly, so its agent filter is a
        // plain column comparison; the other four have no such join and reach
        // the agent name through a subquery instead.
        var scoreAgentFilterViaSubquery = $"""
              AND (@agent_name IS NULL OR run_id IN (SELECT id FROM {Schema}.runs WHERE tenant_id = @tenant_id AND agent_name = @agent_name))
            """;

        SelectRunScoreSummary = $"""
            SELECT name,
                   kind,
                   COUNT(*)::bigint,
                   (COUNT(*) - COUNT(value))::bigint,
                   AVG(value),
                   MIN(value),
                   MAX(value)
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id
            {ScoreFilter(string.Empty)}
            {scoreAgentFilterViaSubquery}
            GROUP BY name, kind
            ORDER BY COUNT(*) DESC, name
            LIMIT @max_rows;

            -- Second result set: this breakdown's category counts. Fetched
            -- UNBOUNDED (no @max_rows here) and capped per name in code,
            -- because the cap is PER GROUP, not over the whole result set.
            SELECT name,
                   text_value,
                   COUNT(*)::bigint
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
            SELECT author,
                   kind,
                   COUNT(*)::bigint,
                   (COUNT(*) - COUNT(value))::bigint,
                   AVG(value),
                   MIN(value),
                   MAX(value)
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id
              AND author IS NOT NULL AND author <> ''
            {ScoreFilter(string.Empty)}
            {scoreAgentFilterViaSubquery}
            GROUP BY author, kind
            ORDER BY COUNT(*) DESC, author
            LIMIT @max_rows;

            -- Fourth result set: breakdown by source.
            SELECT source,
                   kind,
                   COUNT(*)::bigint,
                   (COUNT(*) - COUNT(value))::bigint,
                   AVG(value),
                   MIN(value),
                   MAX(value)
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id
            {ScoreFilter(string.Empty)}
            {scoreAgentFilterViaSubquery}
            GROUP BY source, kind
            ORDER BY COUNT(*) DESC, source
            LIMIT @max_rows;

            -- Fifth result set: breakdown by the agent that produced the
            -- scored run (a score itself carries no agent name).
            SELECT r.agent_name,
                   s.kind,
                   COUNT(*)::bigint,
                   (COUNT(*) - COUNT(s.value))::bigint,
                   AVG(s.value),
                   MIN(s.value),
                   MAX(s.value)
            FROM {Schema}.run_scores AS s
            JOIN {Schema}.runs AS r ON r.tenant_id = s.tenant_id AND r.id = s.run_id
            WHERE s.tenant_id = @tenant_id
            {ScoreFilter("s.")}
              AND (@agent_name IS NULL OR r.agent_name = @agent_name)
            GROUP BY r.agent_name, s.kind
            ORDER BY COUNT(*) DESC, r.agent_name
            LIMIT @max_rows;
            """;

        // A single result set, run only when RunScoreQuery.Bucket is given
        // (154, open question 5: no cost when it is not). Buckets with no
        // matching score are NOT produced -- a sparse series, unlike
        // SelectRunTimeSeries. date_trunc('week', ...) truncates to the
        // preceding Monday (ISO 8601), matching RunScoreBucketing.Truncate.
        // TruncateUtc (declared above, by SelectRunTimeSeries) keeps this
        // independent of the server's session time zone.
        SelectRunScoreSeries = $"""
            SELECT {TruncateUtc("@bucket_unit", "created_at")} AS bucket,
                   name,
                   kind,
                   COUNT(*)::bigint,
                   (COUNT(*) - COUNT(value))::bigint,
                   AVG(value),
                   MIN(value),
                   MAX(value)
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id
            {ScoreFilter(string.Empty)}
            {scoreAgentFilterViaSubquery}
            GROUP BY {TruncateUtc("@bucket_unit", "created_at")}, name, kind
            ORDER BY bucket, name;
            """;

        // If the lease is held by someone else and has not expired, WHERE
        // stays false; the conflict row is NOT UPDATED and RETURNING
        // produces no row (the PostgreSQL side of K-177: a single-statement
        // upsert, no second result set).
        AcquireSingletonLease = $"""
            INSERT INTO {Schema}.singleton_leases (name, owner_id, expires_at, updated_at)
            VALUES (@name, @owner_id, @expires_at, @now)
            ON CONFLICT (name) DO UPDATE SET
                owner_id   = EXCLUDED.owner_id,
                expires_at = EXCLUDED.expires_at,
                updated_at = EXCLUDED.updated_at
            WHERE {Schema}.singleton_leases.owner_id = EXCLUDED.owner_id
               OR {Schema}.singleton_leases.expires_at < @now
            RETURNING name;
            """;

        // A PLAIN INSERT, not a DO NOTHING/UPDATE conflict clause: the caller
        // catches the violation with SqlDialect.IsUniqueViolation and reads
        // the existing record with SelectIdempotencyKey (DIFFERENT from
        // K-177's two-branch upsert pattern -- here "already exists" is not
        // an error, it is a normal flow branch).
        InsertIdempotencyKey = $"""
            INSERT INTO {Schema}.idempotency_keys (tenant_id, "key", fingerprint, state, created_at)
            VALUES (@tenant_id, @key, @fingerprint, 0, @created_at);
            """;

        SelectIdempotencyKey = $"""
            SELECT state, fingerprint, status_code, content_type, body, run_id, headers
              FROM {Schema}.idempotency_keys
             WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        CompleteIdempotencyKey = $"""
            UPDATE {Schema}.idempotency_keys
               SET state = 2, status_code = @status_code, content_type = @content_type,
                   body = @body, run_id = @run_id, headers = @headers, completed_at = @completed_at
             WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        DeleteIdempotencyKey = $"""
            DELETE FROM {Schema}.idempotency_keys WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        // The SAME pattern as ClaimOrphanedRuns: candidate selection + closing
        // in a SINGLE statement, closed rows are read via RETURNING. There is
        // NO tenant filter -- this is a maintenance operation.
        ExpirePendingApprovals = $"""
            UPDATE {Schema}.pending_approvals
               SET status = @status_expired
             WHERE id IN (
                 SELECT id FROM {Schema}.pending_approvals
                 WHERE status = @status_pending AND expires_at < @older_than
                 ORDER BY expires_at ASC
                 LIMIT @max
             )
             RETURNING id, tenant_id, run_id, session_id, request_id, tool_name, arguments, status,
                       decided_by, decided_at, expires_at, created_at, presentation;
            """;

        // -------------------------------------------------------------------
        // Phase 65 -- tenant provider bindings (BYOK) and egress policy
        // -------------------------------------------------------------------

        UpsertTenantProviderBinding = $"""
            INSERT INTO {Schema}.tenant_provider_bindings
                ({TenantProviderBindingColumns})
            VALUES
                (@tenant_id, @provider_name, @api_key_configuration_name, @endpoint, @updated_at)
            ON CONFLICT (tenant_id, provider_name) DO UPDATE
                SET api_key_configuration_name = EXCLUDED.api_key_configuration_name,
                    endpoint = EXCLUDED.endpoint,
                    updated_at = EXCLUDED.updated_at;
            """;

        UpsertTenantEgressPolicy = $"""
            INSERT INTO {Schema}.tenant_egress_policies
                (tenant_id, allowed_providers, updated_at)
            VALUES
                (@tenant_id, @allowed_providers, @updated_at)
            ON CONFLICT (tenant_id) DO UPDATE
                SET allowed_providers = EXCLUDED.allowed_providers,
                    updated_at = EXCLUDED.updated_at;
            """;

    }
}
