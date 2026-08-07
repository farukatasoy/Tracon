namespace AgentPrism;

/// <summary>
/// <see cref="SqlQueriesBase"/> yuzeyinin SQLite metinleri.
/// </summary>
/// <remarks>
/// <para>
/// Sorgu <em>adlari</em> ve dondurdukleri sutun sirasi PostgreSQL ile birebir
/// aynidir; paylasilan depo kodu ucunu de ayirt etmez. Farklar yalnizca metnin
/// icindedir.
/// </para>
/// <para>
/// SQLite'ta sema kavrami yoktur; <see cref="SqlQueriesBase.Schema"/> burada
/// dogrulanmis bir TABLO ONEKI tasir (varsayilan <c>agentprism_</c>) ve dogrudan
/// tablo adinin basina eklenir — nokta YOKTUR (<c>{Schema}.tablo</c> degil,
/// <c>{Schema}tablo</c>).
/// </para>
/// <para>Uygulanan ceviri kurallari (olculdu, Faz 24 acilisi):</para>
/// <list type="bullet">
///   <item><description>
///     <strong>Upsert'ler PostgreSQL ile AYNI desendedir:</strong>
///     <c>INSERT ... ON CONFLICT (cols) DO UPDATE ... RETURNING</c>. SQLite 3.35+
///     bunu tek ifadede, PostgreSQL'in <c>ON CONFLICT ... RETURNING</c>'iyle
///     BIREBIR ayni semantikte destekler — SQL Server'in iki-dalli
///     <c>UPDATE ... OUTPUT</c> + <c>IF @@ROWCOUNT = 0 INSERT ... OUTPUT</c>
///     deseni (K-177) burada GEREKMEZ. <c>ON CONFLICT</c> hedefinde
///     <c>COALESCE(col, '')</c> gibi ifadeler de PostgreSQL gibi calisir: SQLite
///     benzersiz kisitta NULL'lari PostgreSQL gibi birbirinden AYIRT EDER (SQL
///     Server'in tersi, K-184'un SQLite'ta gecerli olmadigi anlamina gelir).
///   </description></item>
///   <item><description>
///     <c>COUNT(*) FILTER (WHERE p)</c> SQLite'ta DOGRUDAN calisir (3.30+);
///     SQL Server'in <c>COALESCE(SUM(CASE...))</c> donusumu GEREKMEZ.
///   </description></item>
///   <item><description>
///     🚨 <c>LEFT JOIN LATERAL ... ON TRUE</c> SQLite'ta YOKTUR (denendi:
///     "near SELECT: syntax error"). Agac toplamlari (<c>SelectRun</c>,
///     <c>SelectRuns</c>) SELECT listesinde ayri KORELE SKALER ALT SORGULARLA
///     yazilir; her biri kendi <c>WHERE</c> kosulunu tasir.
///   </description></item>
///   <item><description>
///     🚨 <c>generate_series</c> yoktur; ozyinelemeli bir CTE kullanilir (SQL
///     Server ile ayni desen). <c>date_trunc(@unit, x)</c> yerine
///     <c>strftime(bicim, x)</c> kullanilir; zaman damgalari zaten
///     <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c> metni oldugu icin <c>strftime</c>
///     dogrudan calisir.
///   </description></item>
///   <item><description>
///     <c>EXTRACT(EPOCH FROM (a-b)) * 1000</c> -> <c>(julianday(a) - julianday(b)) * 86400000.0</c>.
///   </description></item>
///   <item><description>
///     <c>UNNEST(@ids, @inputs) WITH ORDINALITY</c> -> iki <c>json_each()</c>
///     cagrisi <c>key</c> (0 tabanli sira) uzerinden birlestirilir.
///   </description></item>
///   <item><description>
///     <c>= ANY(dizi)</c> -> <c>EXISTS (SELECT 1 FROM json_each(dizi) WHERE value = @p)</c>.
///   </description></item>
///   <item><description>
///     <c>FOR UPDATE SKIP LOCKED</c> GEREKMEZ: SQLite tek yazicidir, ayni anda
///     yalniz bir yazma islemi surer. Duz bir <c>WHERE id = (SELECT ... LIMIT 1)</c>
///     alt sorgusu ayni sonucu verir.
///   </description></item>
///   <item><description>
///     🚨 <c>LEAST</c>/<c>GREATEST</c> SQLite'ta cok-argumanli <c>min()</c>/<c>max()</c>
///     ile karsilanabilirdi AMA NULL davranisi TERSTIR: SQLite'in <c>max(a,b)</c>'si
///     herhangi bir arguman NULL ise NULL doner (denendi), PostgreSQL'in
///     <c>GREATEST</c>'i NULL'lari ATLAR. Bu yuzden SQL Server'daki gibi bir
///     <c>CASE</c> zinciri kullanilir.
///   </description></item>
///   <item><description>
///     🚨 Veri degistiren CTE (<c>WITH updated AS (UPDATE ... RETURNING) UPDATE ...</c>)
///     SQLite'ta YOKTUR (denendi: "near UPDATE: syntax error", SQL Server ile
///     ayni sinir). <c>ReportJobItem</c> iki ayri ifadeye bolunur; ikinci ifade
///     SQLite'in <c>changes()</c> islevini kullanir — bu islev, AYNI baglantida
///     EN SON tamamlanan INSERT/UPDATE/DELETE'in etkiledigi satir sayisini
///     dondurur. Idempotentligi dogal olarak korur: ilk UPDATE 0 satir
///     etkilerse (oge zaten raporlanmis) <c>changes()</c> sifir doner.
///   </description></item>
///   <item><description>
///     Sayfalama <c>LIMIT @take OFFSET @skip</c> PostgreSQL ile BIREBIR aynidir;
///     SQL Server'in <c>@take = 0</c> tuzagi (FETCH hata verir) burada YOKTUR —
///     SQLite'in <c>LIMIT 0</c> PostgreSQL gibi bos liste dondurur.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class SqliteQueries : SqlQueriesBase
{
    /// <summary>Yeni bir sorgu kumesi olusturur.</summary>
    /// <param name="tablePrefix">Dogrulanacak tablo oneki.</param>
    /// <exception cref="AgentPrismException">Onek gecerli bir tanimlayici degilse.</exception>
    public SqliteQueries(string tablePrefix)
    {
        Schema = SqlIdentifier.RequireSchemaName(tablePrefix);

        // SQLite'ta sema olusturma kavrami yoktur; MigrationRunner yine de bu
        // adimi cagirir, bu yuzden zararsiz bir no-op verilir.
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

        // --- Agent tanimlari ---

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

        // --- Skill'ler ---

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

        // --- Skill script'leri ---

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

        // --- Script calistirma izinleri ---

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

        // --- Oturumlar ---

        UpsertSession = $"""
            INSERT INTO {Schema}sessions (id, tenant_id, agent_name, state, schema_version, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @state, @schema_version, @created_at, @updated_at)
            ON CONFLICT (tenant_id, id) DO UPDATE
                SET agent_name     = excluded.agent_name,
                    state          = excluded.state,
                    schema_version = excluded.schema_version,
                    updated_at     = excluded.updated_at;
            """;

        SelectSession = $"""
            SELECT agent_name, state, schema_version, created_at, updated_at, tenant_id
            FROM {Schema}sessions
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        DeleteSession = $"DELETE FROM {Schema}sessions WHERE id = @id AND tenant_id = @tenant_id;";

        SelectSessions = $"""
            SELECT id, agent_name, state, schema_version, created_at, updated_at, tenant_id
            FROM {Schema}sessions
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
            ORDER BY updated_at DESC
            LIMIT @take OFFSET @skip;
            """;

        // --- Calistirmalar ---

        // 🚨 Faz 46: UPSERT'tir. Kuyruga alinan bir calistirma once Queued
        // olarak yazilir; isci is'i gercekten calistirdiginda AYNI id ile
        // ikinci kez cagrilir ve satir yerinde guncellenir (yeni satir ACILMAZ).
        InsertRun = $"""
            INSERT INTO {Schema}runs (id, tenant_id, agent_name, session_id, model_id, status, started_at, is_streaming, event_count,
                                       parent_run_id, root_run_id, depth, kind, workflow_name, agent_version, experiment_id, variant)
            VALUES (@id, @tenant_id, @agent_name, @session_id, @model_id, @status, @started_at, @is_streaming, 0,
                    @parent_run_id, @root_run_id, @depth, @kind, @workflow_name, @agent_version, @experiment_id, @variant)
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
                variant       = excluded.variant;
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
                pricing_source = @pricing_source
            WHERE id = @id;
            """;

        UpdateRunCost = $"""
            UPDATE {Schema}runs
            SET input_cost     = @input_cost,
                output_cost    = @output_cost,
                cost_currency  = @cost_currency,
                pricing_source = @pricing_source
            WHERE id = @id;
            """;

        // 🚨 SQLite'ta LATERAL JOIN yoktur. Agac toplamlari SELECT listesinde
        // korele skaler alt sorgularla hesaplanir; her biri ayri bir tarama
        // yapar ama okuma yolu, saklanan bir toplamin her alt calistirma
        // tamamlaninca guncellenmesinden ucuzdur.
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
            r.error_class, r.error_fingerprint
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
              AND (@session_id IS NULL OR r.session_id = @session_id)
              AND (@started_after IS NULL OR r.started_at > @started_after)
              AND (@root_run_id IS NULL OR r.root_run_id = @root_run_id OR r.id = @root_run_id)
              AND (
                    (@parent_run_id IS NOT NULL AND r.parent_run_id = @parent_run_id)
                 OR (@parent_run_id IS NULL AND (@only_root_runs = 0 OR r.parent_run_id IS NULL))
              )
            ORDER BY r.started_at DESC, r.id DESC
            LIMIT @take OFFSET @skip;
            """;

        // scored_runs/positive_rate (Faz 31): matchedRunScoresFilter runs'in
        // AYNI filtresini (kiraci/eval/agent/tarih) tekrarlar -- ayri bir CTE
        // yerine skaler alt sorgu olarak eklenmesinin sebebi mevcut toplam
        // satirini degistirmeden en kucuk degisiklikle genisletmektir.
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

            -- Besinci sonuc kumesi: hata sinifi kirilimi (Faz 44). error_class
            -- NULL olan (hata sinifi eklenmeden once yazilmis) satirlar Unknown
            -- (0) kovasina duser -- K-014 geriye donuk doldurma yapmaz.
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

            -- Altinci sonuc kumesi: sinif basina en sik uc parmak izi kumesi.
            -- Gerekce PostgreSQL 0021_error_classification.sql'e bakin.
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

        InsertRunEvent = $"""
            INSERT INTO {Schema}run_events (run_id, seq, type, text, tool_name, tool_call_id, payload, created_at)
            VALUES (@run_id, @seq, @type, @text, @tool_name, @tool_call_id, @payload, @created_at);
            """;

        SelectRunEvents = $"""
            SELECT e.run_id, e.seq, e.type, e.text, e.tool_name, e.tool_call_id, e.payload, e.created_at
            FROM {Schema}run_events e
            JOIN {Schema}runs r ON r.id = e.run_id
            WHERE e.run_id = @run_id AND e.seq >= @from_sequence AND r.tenant_id = @tenant_id
            ORDER BY e.seq;
            """;

        // --- Konusmalar (sohbet gecmisi) ---

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

        // --- Tool cagrilari ---

        InsertToolInvocation = $"""
            INSERT INTO {Schema}tool_invocations
                (id, run_id, tool_name, tool_call_id, source, arguments, result, duration_ms, error, created_at,
                 usage_unit, usage_quantity, usage_estimated, cost, cost_currency)
            VALUES
                (@id, @run_id, @tool_name, @tool_call_id, @source, @arguments, @result, @duration_ms, @error, @created_at,
                 @usage_unit, @usage_quantity, @usage_estimated, @cost, @cost_currency);
            """;

        // 🚨 Yeni sutunlar HER ZAMAN sona eklenir; mevcut sabit-indeks okuyucular
        // (ReadToolInvocation) yeniden numaralandirilmaz. Faz 20 dersi.
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

        // --- Deneyler ---

        const string experimentColumns = """
            id, tenant_id, name, agent_name, variants, status, assignment_key, started_at, ended_at, updated_at
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

        SelectExperimentResults = $"""
            SELECT variant,
                   MAX(agent_version),
                   COUNT(*),
                   COUNT(*) FILTER (WHERE status = @status_completed),
                   COUNT(*) FILTER (WHERE status = @status_failed),
                   COUNT(*) FILTER (WHERE status = @status_canceled),
                   COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0),
                   COALESCE(SUM(total_tokens), 0),
                   AVG(CASE WHEN completed_at IS NOT NULL
                            THEN (julianday(completed_at) - julianday(started_at)) * 86400000.0 END),
                   CASE WHEN COUNT(*) FILTER (WHERE input_cost IS NOT NULL OR output_cost IS NOT NULL) = 0
                        THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END,
                   MAX(cost_currency)
            FROM {Schema}runs
            WHERE tenant_id = @tenant_id AND experiment_id = @experiment_id AND variant IS NOT NULL
            GROUP BY variant;
            """;

        // 🚨 generate_series yoktur; ozyinelemeli CTE kullanilir. Zaman
        // damgalari zaten yyyy-MM-ddTHH:mm:ss.fffffffZ metni oldugu icin
        // strftime/datetime dogrudan calisir (SqliteDialect.AddTimestamp).
        // Bos kovalar da doner: aksi halde grafikte kesinti "veri yok" degil
        // "sifir" gibi gorunur.
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

        // --- Span'ler ---

        // 🚨 Cok-argumanli min()/max() SQLite'ta herhangi bir arguman NULL ise
        // NULL doner (PostgreSQL'in GREATEST/LEAST'inin tersi); bu yuzden SQL
        // Server ile ayni CASE zinciri kullanilir.
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

        // --- Tool onay kurallari ---

        const string approvalColumns = """
            id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at
            """;

        SelectToolApprovalRules = $"""
            SELECT {approvalColumns}
            FROM {Schema}tool_approval_rules
            WHERE tenant_id = @tenant_id
            ORDER BY created_at DESC;
            """;

        // Ayni kapsam icin ikinci bir kural acilmaz; mevcut kayit dondurulur.
        // Kisit COALESCE'li bir ifade indeksidir: SQLite PostgreSQL gibi
        // NULL'lari birbirine esit SAYMAZ.
        InsertToolApprovalRule = $"""
            INSERT INTO {Schema}tool_approval_rules ({approvalColumns})
            VALUES (@id, @tenant_id, @agent_name, @tool_name, @arguments_hash, @created_by, @created_at)
            ON CONFLICT (tenant_id, COALESCE(agent_name, ''), tool_name, COALESCE(arguments_hash, ''))
                DO UPDATE SET tool_name = {Schema}tool_approval_rules.tool_name
            RETURNING {approvalColumns};
            """;

        DeleteToolApprovalRule = $"""
            DELETE FROM {Schema}tool_approval_rules
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // --- MCP sunuculari ---

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

        // --- Kiracilar ---

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

        // --- Ekler ---

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

        // --- Kalici agent dosya bellegi ---

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

        SelectAgentFiles = $"""
            SELECT path, content
            FROM {Schema}agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name
            ORDER BY path;
            """;

        // --- Workflow'lar ---

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

        // --- Denetim izi ---

        InsertAuditEntry = $"""
            INSERT INTO {Schema}audit_log (id, tenant_id, actor, action, entity, before, after, created_at)
            VALUES (@id, @tenant_id, @actor, @action, @entity, @before, @after, @created_at);
            """;

        SelectAuditLog = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at
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

        // --- Zamanlama ve is kuyrugu ---

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

        // Kimlikler C# tarafinda uretilir; iki JSON dizisi json_each ile acilip
        // key (0 tabanli sira) uzerinden eslenir.
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

        // 🚨 FOR UPDATE SKIP LOCKED GEREKMEZ: SQLite tek yazicidir, ikinci bir
        // isci yazma islemi zaten ic ice giremez.
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

        // 🚨 Veri degistiren CTE SQLite'ta yoktur (SQL Server ile ayni sinir).
        // Iki ayri ifade tek round-trip'te (bir CommandText, bir ExecuteNonQuery)
        // yurutulur; ikinci ifade changes() ile ilk ifadenin etkiledigi satir
        // sayisini okur. Idempotenttir: oge zaten raporlanmissa (status <> 0)
        // ilk UPDATE hicbir satir etkilemez ve changes() sifir doner.
        ReportJobItem = $"""
            UPDATE {Schema}job_items
               SET status = @status, run_id = @run_id, error = @error
             WHERE job_id = @job_id AND seq = @seq AND status = 0;

            UPDATE {Schema}jobs
               SET done_items   = done_items   + (CASE WHEN @status = 1 THEN changes() ELSE 0 END),
                   failed_items = failed_items + (CASE WHEN @status = 2 THEN changes() ELSE 0 END)
             WHERE id = @job_id;
            """;

        // --- Degerlendirme (eval) ---

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

        // Gerekce PostgreSQL InsertEvalCaseWithComputedSeq ile aynidir
        // (docs/45-URETIMDEN-EVAL-KUMESI.md, bolum 45.2). SQLite 3.35+ RETURNING
        // destekler (diger eval sorgularinda zaten kullaniliyor).
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

        // --- Kota ---

        const string quotaColumns = """
            id, tenant_id, agent_name, period, max_runs, max_tokens, max_cost, enabled,
            created_at, updated_at
            """;

        // 🚨 Catisma hedefi COALESCE(agent_name, '') ifadesidir: SQLite
        // PostgreSQL gibi NULL'lari birbirine esit SAYMAZ; duz bir
        // (tenant_id, agent_name, period) hedefi agent_name NULL olan ayni
        // kuralin sinirsiz kez eklenmesine izin verirdi.
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

        // Tuketim ATOMIK olarak artirilir.
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

        // 🚨 Sutun listesinde SIR YOKTUR: yalnizca secret_configuration_key
        // (anahtarin ADI) vardir (K-059).
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

        // 🚨 events bir JSON dizisidir; PostgreSQL'in = ANY(events) ifadesinin
        // karsiligi json_each uzerinde TAM eslesmedir.
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

        // Ust uste basarisizlik sayaci ve otomatik kapatma TEK ifadede yapilir.
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

        const string retentionPolicyColumns =
            "id, tenant_id, target, max_age_days, max_rows, archive, enabled, created_at, updated_at";

        // Upsert PostgreSQL ile birebir aynidir (K-194): tek ifadelik
        // INSERT ... ON CONFLICT ... RETURNING, iki dalli SQL Server deseni
        // (K-177) burada yoktur.
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
        // Faz 31 -- calistirma/mesaj puani
        // -------------------------------------------------------------------
        const string runScoreColumns =
            "id, tenant_id, run_id, message_id, kind, value, comment, source, author, created_at";

        // Upsert PostgreSQL ile birebir aynidir (K-194): message_id
        // COALESCE(…, '') ile esitlenir, author BILEREK COALESCE EDILMEZ --
        // SQLite de NULL'lari birbirine esit SAYMAZ, author bos oldugunda
        // (kimliksiz kurulum) her cagri yeni bir satir acar.
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

        // Upsert PostgreSQL ile birebir aynidir (K-194).
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

        // Duz INSERT: benzersizlik ihlali SqlDialect.IsUniqueViolation ile
        // yakalanir, cagiran taraf mevcut kaydi SelectIdempotencyKey ile okur.
        // 🚨 `key` AYRILMIS bir sozcuktur; "key" ile cift tirnaklanir.
        InsertIdempotencyKey = $"""
            INSERT INTO {Schema}idempotency_keys (tenant_id, "key", fingerprint, state, created_at)
            VALUES (@tenant_id, @key, @fingerprint, 0, @created_at);
            """;

        SelectIdempotencyKey = $"""
            SELECT state, fingerprint, status_code, content_type, body, run_id
              FROM {Schema}idempotency_keys
             WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        CompleteIdempotencyKey = $"""
            UPDATE {Schema}idempotency_keys
               SET state = 2, status_code = @status_code, content_type = @content_type,
                   body = @body, run_id = @run_id, completed_at = @completed_at
             WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        DeleteIdempotencyKey = $"""
            DELETE FROM {Schema}idempotency_keys WHERE tenant_id = @tenant_id AND "key" = @key;
            """;
    }
}
