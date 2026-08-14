namespace AgentPrism;

/// <summary>
/// <see cref="SqlQueriesBase"/> yuzeyinin PostgreSQL metinleri.
/// </summary>
/// <remarks>
/// Upsert'ler <c>ON CONFLICT ... DO UPDATE ... RETURNING</c> kullanir; tek gidis
/// donusle hem yazar hem sonucu dondurur. Ayni sozlesme SQL Server tarafinda
/// <c>UPDATE ... OUTPUT</c> + <c>IF ROWCOUNT = 0 INSERT ... OUTPUT</c> ile
/// karsilanir (karar K-177).
/// </remarks>
internal sealed class PostgresQueries : SqlQueriesBase
{
    /// <summary>Yeni bir sorgu kumesi olusturur.</summary>
    /// <param name="schemaName">Dogrulanacak sema adi.</param>
    /// <exception cref="AgentPrismException">Sema adi gecerli bir tanimlayici degilse.</exception>
    public PostgresQueries(string schemaName)
    {
        Schema = SqlIdentifier.RequireSchemaName(schemaName);

        CreateSchema = $"CREATE SCHEMA IF NOT EXISTS {Schema};";

        CreateMigrationsTable = $"""
            CREATE TABLE IF NOT EXISTS {Schema}.__migrations (
                id         integer     NOT NULL PRIMARY KEY,
                name       text        NOT NULL,
                checksum   text        NOT NULL,
                applied_at timestamptz NOT NULL
            );
            """;

        SelectAppliedMigrations = $"SELECT id, name, checksum FROM {Schema}.__migrations ORDER BY id;";

        InsertMigration = $"""
            INSERT INTO {Schema}.__migrations (id, name, checksum, applied_at)
            VALUES (@id, @name, @checksum, @applied_at);
            """;

        UpsertTenant = $"""
            INSERT INTO {Schema}.tenants (id, slug, display_name, created_at)
            VALUES (@id, @slug, @display_name, @created_at)
            ON CONFLICT (slug) DO NOTHING;
            """;

        // --- Agent tanimlari ---

        UpsertAgentDefinition = $"""
            INSERT INTO {Schema}.agent_definitions (id, tenant_id, name, version, definition, created_at, updated_at)
            VALUES (@id, @tenant_id, @name, 1, @definition, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET version    = {Schema}.agent_definitions.version + 1,
                    definition = EXCLUDED.definition,
                    updated_at = EXCLUDED.updated_at
            RETURNING id, version;
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

        // --- Skill'ler ---

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

        // --- Skill script'leri ---

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

        // --- Script calistirma izinleri ---

        SelectSkillScriptGrants = $"""
            SELECT id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at
            FROM {Schema}.skill_script_grants
            WHERE tenant_id = @tenant_id
            ORDER BY skill_name, COALESCE(script_name, '');
            """;

        // Dar izin genis olani yener: script'e ozgu kayit once gelsin diye
        // script_name IS NOT NULL olanlar basa siralanir ve tek satir alinir.
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

        // Izin SILINMEZ, iptal edilir: "kim ne zaman izin verdi ve ne zaman geri
        // aldi" sorusu denetim izinin disinda da cevaplanabilmelidir.
        RevokeSkillScriptGrant = $"""
            UPDATE {Schema}.skill_script_grants
            SET revoked_at = @revoked_at
            WHERE tenant_id = @tenant_id
              AND skill_name = @skill_name
              AND COALESCE(script_name, '') = COALESCE(@script_name, '')
              AND revoked_at IS NULL;
            """;

        // --- Oturumlar ---

        UpsertSession = $"""
            INSERT INTO {Schema}.sessions (id, tenant_id, agent_name, state, schema_version, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @state, @schema_version, @created_at, @updated_at)
            ON CONFLICT (tenant_id, id) DO UPDATE
                SET agent_name     = EXCLUDED.agent_name,
                    state          = EXCLUDED.state,
                    schema_version = EXCLUDED.schema_version,
                    updated_at     = EXCLUDED.updated_at;
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
            ORDER BY updated_at DESC
            OFFSET @skip LIMIT @take;
            """;

        // --- Calistirmalar ---

        // 🚨 Faz 46: UPSERT'tir. Kuyruga alinan bir calistirma once Queued
        // olarak yazilir; isci is'i gercekten calistirdiginda AYNI id ile
        // ikinci kez cagrilir ve satir yerinde guncellenir (yeni satir ACILMAZ).
        InsertRun = $"""
            INSERT INTO {Schema}.runs (id, tenant_id, agent_name, session_id, model_id, status, started_at, is_streaming, event_count,
                                       parent_run_id, root_run_id, depth, kind, workflow_name, agent_version, experiment_id, variant,
                                       replay_of_run_id)
            VALUES (@id, @tenant_id, @agent_name, @session_id, @model_id, @status, @started_at, @is_streaming, 0,
                    @parent_run_id, @root_run_id, @depth, @kind, @workflow_name, @agent_version, @experiment_id, @variant,
                    @replay_of_run_id)
            ON CONFLICT (id) DO UPDATE SET
                tenant_id     = EXCLUDED.tenant_id,
                agent_name    = EXCLUDED.agent_name,
                session_id    = EXCLUDED.session_id,
                model_id      = EXCLUDED.model_id,
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
                replay_of_run_id = EXCLUDED.replay_of_run_id;
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
                pricing_source = @pricing_source
            WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        // Yalniz bakim ucu (POST /api/stats/recalculate-costs) tarafindan
        // kullanilir; normal akista maliyet UpdateRunCompletion ile bir kez yazilir.
        UpdateRunCost = $"""
            UPDATE {Schema}.runs
            SET input_cost     = @input_cost,
                output_cost    = @output_cost,
                cost_currency  = @cost_currency,
                pricing_source = @pricing_source
            WHERE id = @id AND (@tenant_id IS NULL OR tenant_id = @tenant_id);
            """;

        // Oksuz calistirma uzlastirmasi (Faz 54). Yalniz Running satirlari
        // etkiler; degeri var olmayan veya baska durumdaki bir kimlik icin
        // sessizce sifir satir gunceller (bakim sinyali, hata firlatmaz).
        TouchRunHeartbeat = $"""
            UPDATE {Schema}.runs
            SET heartbeat_at = @at
            WHERE id = @id AND status = @status_running;
            """;

        // Tek bir ifadede: aday secimi (heartbeat_at yoksa started_at'e duser),
        // kapama VE hata alanlarinin yazimi. error_message satirin KENDI
        // heartbeat_at/started_at degerinden turetilir; error_fingerprint
        // SABIT bir dizedir ("orphaned") -- SHA-256 hash DEGILDIR, cunku tum
        // oksuz calistirmalar ayni arizadir ve InMemoryRunStore ile davranis
        // esitligi boyle saglanir (ErrorFingerprint AgentPrism.Core'da internal'dir,
        // bu derlemeden erisilemez). Queued satirlar status=@status_running
        // suzgeciyle asla eslesmez (Faz 46'nin sahiplik ayrimi).
        ClaimOrphanedRuns = $"""
            UPDATE {Schema}.runs
            SET status            = @status_failed,
                completed_at      = @now,
                error_type        = 'orphaned',
                error_message     = 'Calistirma yuruten surec yanit vermiyor; son isaret: '
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
                      error_fingerprint;
            """;

        // RunEventWriter o surecte artik yoktur; olayi uzlastirici yazar.
        // Sira numarasi mevcut en buyuk degerin bir fazlasidir -- calistirma
        // az once kapatildigi icin yaris riski yoktur (SingletonGuard zaten
        // tek uzlastiriciyi garanti eder).
        InsertOrphanRunEvent = $"""
            INSERT INTO {Schema}.run_events (run_id, seq, type, text, created_at)
            VALUES (@run_id,
                    COALESCE((SELECT MAX(seq) FROM {Schema}.run_events WHERE run_id = @run_id), -1) + 1,
                    @type, @text, @created_at);
            """;

        // Agac toplamlari OKUMADA hesaplanir, saklanmaz. Saklansaydi her alt
        // calistirmanin tamamlanmasi ustundeki her kaydi guncellemek zorunda kalir
        // ve kayit yolu derinlikle birlikte pahalilasirdi. LATERAL alt sorgu
        // runs_parent_idx ve runs_root_idx uzerinden calisir; sayfa basina en fazla
        // `take` satir icin degerlendirilir.
        var treeJoin = $"""
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::int AS child_count
                FROM {Schema}.runs AS child
                WHERE child.parent_run_id = r.id
            ) AS children ON TRUE
            LEFT JOIN LATERAL (
                SELECT COALESCE(SUM(sub.input_tokens), 0)::bigint  AS input_tokens,
                       COALESCE(SUM(sub.output_tokens), 0)::bigint AS output_tokens,
                       COALESCE(SUM(sub.total_tokens), 0)::bigint  AS total_tokens,
                       COUNT(sub.total_tokens)::bigint             AS usage_rows,
                       SUM(sub.input_cost)                         AS cost_input,
                       SUM(sub.output_cost)                        AS cost_output,
                       MAX(sub.cost_currency)                      AS cost_currency,
                       COUNT(*) FILTER (WHERE sub.pricing_source = 2)::bigint AS unknown_pricing_rows,
                       COUNT(sub.pricing_source)::bigint           AS pricing_rows
                FROM {Schema}.runs AS sub
                WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id
            ) AS tree ON TRUE
            """;

        // 🚨 Yeni sutunlar HER ZAMAN sona eklenir, aralara sokulmaz: okuyucu
        // (PostgresRunStore.ReadRun) sabit sira numarasiyla okur.
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
              AND (@session_id IS NULL OR r.session_id = @session_id)
              AND (@started_after IS NULL OR r.started_at > @started_after)
              AND (@root_run_id IS NULL OR r.root_run_id = @root_run_id OR r.id = @root_run_id)
              AND (
                    -- Ebeveyn filtresi verildiyse kok filtresi BILEREK yok sayilir:
                    -- ikisi mantiksal olarak celisir ve sessizce bos liste donmek
                    -- hata ayiklanmasi zor bir davranistir.
                    (@parent_run_id IS NOT NULL AND r.parent_run_id = @parent_run_id)
                 OR (@parent_run_id IS NULL AND (NOT @only_root_runs OR r.parent_run_id IS NULL))
              )
            ORDER BY r.started_at DESC, r.id DESC
            OFFSET @skip LIMIT @take;
            """;

        // Iki sonuc kumesi tek gidis donuste alinir: once genel ozet, sonra agent
        // kirilimi. Durum degerleri sabit sayi olarak gomulmez; RunStatus enum'undan
        // parametre olarak gelir, boylece enum ile SQL arasindaki bag aciktir.
        // kind <> @kind_eval: eval vaka calistirmalari sentetik test
        // cagrilaridir, gercek trafik degildir; ozeti kirletmemesi icin haric
        // tutulur (docs/18-DEGERLENDIRME.md, acik soru 4).
        // scored_runs/positive_rate (Faz 31): matched_run_scores runs'in AYNI
        // filtresini (kiraci/eval/agent/tarih) tekrarlar -- ayri bir CTE'ye
        // tasimak yerine skaler alt sorgu olarak eklenmesinin sebebi mevcut
        // toplam satirini degistirmeden en kucuk degisiklikle genisletmektir.
        var matchedRunScoresFilter = $"""
            {Schema}.run_scores rs
            JOIN {Schema}.runs r2 ON r2.id = rs.run_id
            WHERE rs.tenant_id = @tenant_id
              AND r2.tenant_id = @tenant_id
              AND r2.kind <> @kind_eval
              AND (@agent_name IS NULL OR r2.agent_name = @agent_name)
              AND (@started_after IS NULL OR r2.started_at > @started_after)
            """;

        SelectRunStatistics = $"""
            SELECT COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_completed)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_failed)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_canceled)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_running)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_awaiting)::bigint,
                   COALESCE(SUM(input_tokens), 0)::bigint,
                   COALESCE(SUM(output_tokens), 0)::bigint,
                   COALESCE(SUM(total_tokens), 0)::bigint,
                   CASE WHEN COUNT(*) FILTER (WHERE input_cost IS NOT NULL OR output_cost IS NOT NULL) = 0
                        THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END,
                   MAX(cost_currency),
                   COUNT(*) FILTER (WHERE pricing_source = @pricing_source_unknown)::bigint,
                   (SELECT COUNT(DISTINCT rs.run_id) FROM {matchedRunScoresFilter})::bigint,
                   (SELECT CASE WHEN COUNT(*) FILTER (WHERE rs.kind = @kind_binary) = 0 THEN NULL
                                ELSE (COUNT(*) FILTER (WHERE rs.kind = @kind_binary AND rs.value = 1))::float8
                                     / COUNT(*) FILTER (WHERE rs.kind = @kind_binary)
                           END
                    FROM {matchedRunScoresFilter})
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after);

            SELECT agent_name,
                   COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_failed)::bigint,
                   COALESCE(SUM(total_tokens), 0)::bigint
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY agent_name
            ORDER BY COUNT(*) DESC, agent_name
            LIMIT @max_agents;

            SELECT model_id,
                   COUNT(*)::bigint,
                   COALESCE(SUM(input_tokens), 0)::bigint,
                   COALESCE(SUM(output_tokens), 0)::bigint,
                   COALESCE(SUM(total_tokens), 0)::bigint,
                   CASE WHEN COUNT(*) FILTER (WHERE input_cost IS NOT NULL OR output_cost IS NOT NULL) = 0
                        THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END
            FROM {Schema}.runs
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
                   COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_failed)::bigint,
                   COALESCE(SUM(total_tokens), 0)::bigint
            FROM {Schema}.runs
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
            SELECT COALESCE(error_class, 0)::smallint,
                   COUNT(*)::bigint
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND kind <> @kind_eval
              AND status = @status_failed
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY COALESCE(error_class, 0)
            ORDER BY COUNT(*) DESC;

            -- Altinci sonuc kumesi: sinif basina en sik uc parmak izi kumesi.
            -- Iki pencere fonksiyonu gecisi: `failed` her satirin kumesindeki
            -- sayiyi/son goruntuyu ve orneklemek icin en yeni satiri (sample_rank)
            -- isaretler; `ranked` kumeleri sinif icinde sayiya gore siralar.
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

        // 🚨 VALUES degil SELECT ... WHERE EXISTS: yazma yalnizca hedef calistirma
        // BEKLENEN kiraciya aitse uygulanir (K-355). @tenant_id NULL ise denetim
        // yapilmaz. Alt sorgu birincil anahtar aramasidir; sicak yazma yolunda
        // ek maliyeti bir indeks okumasidir.
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

        // --- Konusmalar (sohbet gecmisi) ---

        // ON CONFLICT DO UPDATE konusma satirini kilitler; boylece ayni konusmaya
        // es zamanli yazan iki islem sira numarasi icin sirayla bekler.
        // metadata sutunu yazilmaz; sema varsayilani bos bir jsonb nesnesidir.
        UpsertConversation = $"""
            INSERT INTO {Schema}.conversations (id, tenant_id, agent_name, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @now, @now)
            ON CONFLICT (id) DO UPDATE
                SET updated_at = EXCLUDED.updated_at;
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

        // --- Konusma dallandirma (Faz 47) ---

        SelectConversationBranchPoint = $"""
            SELECT COALESCE(MAX(seq), -1), COUNT(*)
            FROM {Schema}.conversation_items
            WHERE conversation_id = @conversation_id
              AND (@up_to_sequence IS NULL OR seq <= @up_to_sequence);
            """;

        // Ustveri (agent_name, metadata) kaynaktan KOPYALANIR: dal ayni agent'in
        // konusmasidir. Kiraci suzgeci SELECT tarafindadir; baska bir kiracinin
        // konusmasi icin hicbir satir yazilmaz ve cagiran 0 etkilenen satir gorur.
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

        // --- Calistirma girdileri (Faz 47) ---

        // 🚨 `messages` sutunu `json`, `jsonb` DEGIL: ChatMessage icerikleri
        // polimorfiktir ve `$type` ayraci nesnenin ILK ozelligi olmalidir (K-027).
        // Ikinci yazim yok sayilir: kuyruga alinan bir calistirma (Faz 46) ayni
        // kimlikle iki kez baslar ve girdi degismemelidir.
        InsertRunInput = $"""
            INSERT INTO {Schema}.run_inputs (run_id, tenant_id, messages, created_at)
            VALUES (@run_id, @tenant_id, @messages, @created_at)
            ON CONFLICT (run_id) DO NOTHING;
            """;

        SelectRunInput = $"""
            SELECT messages, created_at
            FROM {Schema}.run_inputs
            WHERE run_id = @run_id AND tenant_id = @tenant_id;
            """;

        // --- Tool cagrilari (Faz 6) ---

        // InsertRunEvent ile ayni kiraci muhafizi (K-355).
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

        // 🚨 Yeni sutunlar HER ZAMAN sona eklenir; mevcut sabit-indeks okuyucular
        // (ReadToolInvocation) yeniden numaralandirilmaz. Faz 20 dersi.
        SelectToolInvocations = $"""
            SELECT t.id, t.run_id, t.tool_name, t.tool_call_id, t.source, t.arguments, t.result,
                   t.duration_ms, t.error, t.created_at,
                   t.usage_unit, t.usage_quantity, t.usage_estimated, t.cost, t.cost_currency
            FROM {Schema}.tool_invocations t
            JOIN {Schema}.runs r ON r.id = t.run_id
            WHERE t.run_id = @run_id AND r.tenant_id = @tenant_id
            ORDER BY t.created_at, t.id;
            """;

        // Ortalama sure yalnizca sure tasiyan cagrilar uzerinden alinir:
        // AVG NULL degerleri zaten atlar, bu yuzden payda dogru olur.
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

        // --- Deneyler (Faz 19) ---

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

        // Draft-disi bir deneyi guncelleme girisimi 0 satir dondurur; cagiran
        // taraf bunu onceden GetAsync ile ayirt edip anlamli bir hata verir.
        // canary_policy/rollback_reason BILEREK SET listesinde YOK: bir Draft
        // duzenlemesi (SaveAsync) daha once SetCanaryPolicyAsync ile tanimlanmis
        // kurali silmemelidir.
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

        DeleteExperiment = $"""
            DELETE FROM {Schema}.experiments
            WHERE tenant_id = @tenant_id AND name = @name AND status <> 1;
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

        // Durumdan BAGIMSIZ calisir (Draft veya Running) — SaveAsync'in aksine.
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

        // run_avg_scores: ONCE calistirma basina ortalama (yazar/judge sayisindan
        // bagimsiz), SONRA varyant basina bu ortalamalarin ortalamasi. Dogrudan
        // run_scores JOIN'i (calistirma basina birden fazla puan satiri olabilir)
        // GROUP BY variant'taki TUM diger toplamlari (token, maliyet, sayim) da
        // COGALTIRDI — bu yuzden AYRI bir CTE ile once calistirma duzeyine indirilir.
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
                   COALESCE(SUM(r.input_tokens), 0)::bigint,
                   COALESCE(SUM(r.output_tokens), 0)::bigint,
                   COALESCE(SUM(r.total_tokens), 0)::bigint,
                   AVG(EXTRACT(EPOCH FROM (r.completed_at - r.started_at)) * 1000) FILTER (WHERE r.completed_at IS NOT NULL),
                   CASE WHEN COUNT(*) FILTER (WHERE r.input_cost IS NOT NULL OR r.output_cost IS NOT NULL) = 0
                        THEN NULL ELSE COALESCE(SUM(r.input_cost), 0) + COALESCE(SUM(r.output_cost), 0) END,
                   MAX(r.cost_currency),
                   AVG(s.avg_score)
            FROM {Schema}.runs r
            LEFT JOIN run_avg_scores s ON s.run_id = r.id
            WHERE r.tenant_id = @tenant_id AND r.experiment_id = @experiment_id AND r.variant IS NOT NULL
            GROUP BY r.variant;
            """;

        // Bos kovalar da doner (generate_series + LEFT JOIN): aksi halde
        // grafikte kesinti "veri yok" degil "sifir" gibi gorunur. Eval/Workflow
        // calistirmalari BILEREK haric TUTULMAZ — SelectRunStatistics'in aksine
        // (bkz. docs/KARARLAR.md K-152); @kind verilirse filtrelenir.
        SelectRunTimeSeries = $"""
            WITH buckets AS (
                -- Ust sinir kapsayicidir (Truncate(to_ts) dahil), sonra bucket < to_ts
                -- ile filtrelenir: to_ts tam bir kova sinirina denk gelirse o kova
                -- (icinde hicbir zaman `started_at < to_ts` olan satir olamaz) elenir.
                -- InMemoryRunStore'daki `cursor < To` dongusuyle birebir ayni kural.
                SELECT bucket
                FROM generate_series(
                    date_trunc(@bucket_unit, @from_ts),
                    date_trunc(@bucket_unit, @to_ts),
                    @bucket_step) AS bucket
                WHERE bucket < @to_ts
            ),
            matched AS (
                SELECT date_trunc(@bucket_unit, started_at) AS bucket,
                       COUNT(*)::bigint AS runs,
                       COUNT(*) FILTER (WHERE status = @status_failed)::bigint AS failed_runs,
                       COALESCE(SUM(input_tokens), 0)::bigint AS input_tokens,
                       COALESCE(SUM(output_tokens), 0)::bigint AS output_tokens,
                       CASE WHEN COUNT(*) FILTER (WHERE input_cost IS NOT NULL OR output_cost IS NOT NULL) = 0
                            THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END AS cost,
                       AVG(EXTRACT(EPOCH FROM (completed_at - started_at)) * 1000)
                           FILTER (WHERE completed_at IS NOT NULL) AS avg_duration_ms
                FROM {Schema}.runs
                WHERE tenant_id = @tenant_id
                  AND started_at >= @from_ts AND started_at < @to_ts
                  AND (@agent_name IS NULL OR agent_name = @agent_name)
                  AND (@model_id   IS NULL OR model_id   = @model_id)
                  AND (@kind       IS NULL OR kind       = @kind)
                GROUP BY date_trunc(@bucket_unit, started_at)
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

        // --- Span'ler (Faz 6) ---

        // Trace basligi kiraci + W3C kimligi ciftinde benzersizdir; ayni
        // calistirma icin ikinci bir yazma basligi guncellemekle yetinir.
        UpsertTrace = $"""
            INSERT INTO {Schema}.traces (id, tenant_id, trace_id, run_id, started_at, ended_at)
            VALUES (@id, @tenant_id, @trace_id, @run_id, @started_at, @ended_at)
            ON CONFLICT (tenant_id, trace_id) DO UPDATE
                SET run_id     = COALESCE(EXCLUDED.run_id, {Schema}.traces.run_id),
                    started_at = LEAST({Schema}.traces.started_at, EXCLUDED.started_at),
                    ended_at   = GREATEST({Schema}.traces.ended_at, EXCLUDED.ended_at)
            RETURNING id;
            """;

        // Span kimligi W3C kimliklerinden turetilir, bu yuzden ayni span iki kez
        // yazilirsa cakisir ve satir guncellenir; tekrar kaydi olusmaz.
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

        // --- Tool onay kurallari (Faz 6) ---

        SelectToolApprovalRules = $"""
            SELECT id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at
            FROM {Schema}.tool_approval_rules
            WHERE tenant_id = @tenant_id
            ORDER BY created_at DESC;
            """;

        // Ayni kapsam icin ikinci bir kural acilmaz; mevcut kayit dondurulur.
        // Kisit COALESCE'li bir ifade indeksidir cunku NULL'lar PostgreSQL'de
        // birbirine esit sayilmaz ve duz bir UNIQUE kisit tekrari engellemezdi.
        InsertToolApprovalRule = $"""
            INSERT INTO {Schema}.tool_approval_rules
                (id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at)
            VALUES (@id, @tenant_id, @agent_name, @tool_name, @arguments_hash, @created_by, @created_at)
            ON CONFLICT (tenant_id, COALESCE(agent_name, ''), tool_name, COALESCE(arguments_hash, ''))
                DO UPDATE SET tool_name = {Schema}.tool_approval_rules.tool_name
            RETURNING id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at;
            """;

        DeleteToolApprovalRule = $"""
            DELETE FROM {Schema}.tool_approval_rules
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // --- MCP sunuculari (Faz 6) ---

        // 🚨 Yeni sutunlar HER ZAMAN sona eklenir: PostgresMcpServerStore.ReadServer
        // sabit sira numarasiyla okur (Faz 21 dersi, docs/hafiza/postgresql.md).
        const string mcpServerColumns = """
            id, tenant_id, name, description, endpoint, transport,
            authorization_configuration_key, headers, enabled, requires_approval, created_at, updated_at,
            oauth_enabled, oauth_client_id, oauth_client_secret_configuration_key, oauth_scopes, oauth_authorization_mode
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
            RETURNING {mcpServerColumns};
            """;

        DeleteMcpServer = $"DELETE FROM {Schema}.mcp_servers WHERE tenant_id = @tenant_id AND name = @name;";

        // --- Kiracilar (Faz 6) ---

        SelectTenants = $"""
            SELECT id, slug, display_name, created_at
            FROM {Schema}.tenants
            ORDER BY slug;
            """;

        UpsertTenantDescriptor = $"""
            INSERT INTO {Schema}.tenants (id, slug, display_name, created_at)
            VALUES (@id, @slug, @display_name, @created_at)
            ON CONFLICT (slug) DO UPDATE
                SET display_name = EXCLUDED.display_name
            RETURNING id, slug, display_name, created_at;
            """;

        DeleteTenant = $"DELETE FROM {Schema}.tenants WHERE slug = @slug;";

        // --- Ekler (Faz 14) ---

        InsertAttachment = $"""
            INSERT INTO {Schema}.attachments
                (id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256,
                 content, external_uri, created_by, created_at)
            VALUES
                (@id, @tenant_id, @session_id, @run_id, @file_name, @media_type, @byte_size, @sha256,
                 @content, @external_uri, @created_by, @created_at);
            """;

        SelectAttachment = $"""
            SELECT id, tenant_id, session_id, run_id, file_name, media_type, byte_size, sha256, created_by, created_at
            FROM {Schema}.attachments
            WHERE tenant_id = @tenant_id AND id = @id;
            """;

        // content yalniz istendiginde okunur (docs/14-COK-MODLULUK.md, bolum 14.2).
        SelectAttachmentContent = $"""
            SELECT content, external_uri, media_type
            FROM {Schema}.attachments
            WHERE tenant_id = @tenant_id AND id = @id;
            """;

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

        // --- Kalici agent dosya belleği (Faz 14, 14.5) ---

        SelectAgentFile = $"""
            SELECT content
            FROM {Schema}.agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;
            """;

        UpsertAgentFile = $"""
            INSERT INTO {Schema}.agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @path, @content, @now, @now)
            ON CONFLICT (tenant_id, agent_name, path) DO UPDATE
                SET content    = EXCLUDED.content,
                    updated_at = EXCLUDED.updated_at;
            """;

        DeleteAgentFile = $"""
            DELETE FROM {Schema}.agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;
            """;

        // Faz 51, Is A: onek, derinlik siniri ve glob SQL'e iner; PostgreSQL
        // ayrica regex'i `~` operatoruyle on suzgec olarak indirir (nihai
        // eslesme daima .NET Regex ile istemcide yapilir, degismez).
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

        // --- Workflow'lar (Faz 15) ---

        UpsertWorkflow = $"""
            INSERT INTO {Schema}.workflows (id, tenant_id, name, version, definition, created_at, updated_at)
            VALUES (@id, @tenant_id, @name, 1, @definition, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET version    = {Schema}.workflows.version + 1,
                    definition = EXCLUDED.definition,
                    updated_at = EXCLUDED.updated_at
            RETURNING version, updated_at;
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

        // Kontrol noktasi kimligini AgentPrism uretir; catisma yalnizca ayni
        // kimligin iki kez yazilmasi demektir ve bu bir hatadir -- sessizce
        // gecilmez, bu yuzden ON CONFLICT yan tumcesi YOKTUR.
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

        // Durum yuku BILEREK secilmez: liste ustveridir ve her satirin yanina
        // kilobaytlarca opak JSON tasimak, arayuzun checkpoint listesini
        // acilamaz hale getirirdi.
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

        // --- Denetim izi (Faz 9) ---

        InsertAuditEntry = $"""
            INSERT INTO {Schema}.audit_log (id, tenant_id, actor, action, entity, before, after, created_at)
            VALUES (@id, @tenant_id, @actor, @action, @entity, @before, @after, @created_at);
            """;

        SelectAuditLog = $"""
            SELECT id, tenant_id, actor, action, entity, before, after, created_at
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

        // --- Zamanlama ve is kuyrugu (Faz 17) ---

        UpsertJobSchedule = $"""
            INSERT INTO {Schema}.job_schedules
                (id, tenant_id, name, kind, target_name, cron, time_zone, payload, enabled,
                 next_run_at, last_run_at, created_by, created_at, updated_at)
            VALUES (@id, @tenant_id, @name, @kind, @target_name, @cron, @time_zone, @payload, @enabled,
                    @next_run_at, @last_run_at, @created_by, @created_at, @updated_at)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET kind        = EXCLUDED.kind,
                    target_name = EXCLUDED.target_name,
                    cron        = EXCLUDED.cron,
                    time_zone   = EXCLUDED.time_zone,
                    payload     = EXCLUDED.payload,
                    enabled     = EXCLUDED.enabled,
                    next_run_at = EXCLUDED.next_run_at,
                    last_run_at = EXCLUDED.last_run_at,
                    updated_at  = EXCLUDED.updated_at
            RETURNING id, created_by, created_at;
            """;

        const string scheduleColumns = """
            id, tenant_id, name, kind, target_name, cron, time_zone, payload, enabled,
            next_run_at, last_run_at, created_by, created_at, updated_at
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

        // Kiraciyla sinirlanmaz: bu sorgu isciye aittir, HTTP istegine degil.
        SelectDueJobSchedules = $"""
            SELECT {scheduleColumns}
            FROM {Schema}.job_schedules
            WHERE enabled = TRUE AND cron IS NOT NULL AND next_run_at IS NOT NULL AND next_run_at <= @as_of;
            """;

        DeleteJobSchedule = $"DELETE FROM {Schema}.job_schedules WHERE tenant_id = @tenant_id AND name = @name;";

        // CAS (compare-and-swap): yalnizca beklenen `next_run_at` hala gecerliyse
        // ilerletilir. Baska bir uygulama ornegi ayni anda ayni zamanlamayi zaten
        // ilerlettiyse eslesme tutmaz ve etkilenen satir sayisi sifir olur.
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

        // Kimlikler C# tarafinda uretilir (gen_random_uuid() sunucu surumune
        // gore degisken bir bagimliliktir); iki dizi UNNEST ile sira numarasi
        // (1 tabanli ord) uzerinden eslenir.
        InsertJobItems = $"""
            INSERT INTO {Schema}.job_items (id, job_id, seq, input, status)
            SELECT t.id, @job_id, (t.ord - 1)::int, t.input, 0
            FROM UNNEST(@ids, @inputs) WITH ORDINALITY AS t(id, input, ord);
            """;

        // 🚨 Yeni sutun her zaman SONA eklenir: ReadJob sabit sutun indeksi
        // kullanir ve mevcut indeksleri kaydirmak sessizce yanlis sutun okur.
        const string jobColumns = """
            id, tenant_id, schedule_id, kind, target_name, status, payload, total_items, done_items,
            failed_items, attempt, lease_owner, lease_until, scheduled_for, started_at, completed_at,
            error_message, created_at, max_attempts
            """;

        // FOR UPDATE SKIP LOCKED: birden fazla isci ayni veritabanina baglansa
        // bile bir is yalnizca bir isci tarafindan alinir. Suresi dolmus bir
        // kira (status IN (1, 2) AND lease_until < @now) da yeniden alinabilir.
        LeaseJob = $"""
            UPDATE {Schema}.jobs
               SET status = 1, lease_owner = @owner, lease_until = @lease_until, attempt = attempt + 1,
                   started_at = COALESCE(started_at, @now)
             WHERE id = (
                   SELECT id FROM {Schema}.jobs
                    WHERE (status = 0 AND scheduled_for <= @now)
                       OR (status IN (1, 2) AND lease_until < @now)
                    ORDER BY scheduled_for
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1)
            RETURNING {jobColumns};
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

        // @retry_at NULL ise scheduled_for'a dokunulmaz (eski davranis: is hemen
        // yeniden kiralanabilir). Dolu ise geri adimli bekleme uygulanir; bu,
        // webhook teslimi icin ikinci bir kuyruk yazmayi gereksiz kilar (K-160).
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
            ORDER BY created_at DESC
            OFFSET @skip LIMIT @take;
            """;

        SelectJobItems = $"""
            SELECT id, job_id, seq, input, run_id, status, error
            FROM {Schema}.job_items
            WHERE job_id = @job_id
            ORDER BY seq;
            """;

        // Idempotent raporlama: `updated` CTE'si yalnizca oge hala Pending (0)
        // ise bir satir dondurur; kira suresi dolup ayni oge iki kez
        // raporlanirsa ikinci cagri sayaclari BIR KEZ DAHA artirmaz.
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

        // --- Degerlendirme / eval (Faz 18) ---

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
            INSERT INTO {Schema}.eval_suites
                (id, tenant_id, name, description, agent_name, checks, created_at, updated_at)
            VALUES
                (@id, @tenant_id, @name, @description, @agent_name, @checks, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET description = EXCLUDED.description,
                    agent_name  = EXCLUDED.agent_name,
                    checks      = EXCLUDED.checks,
                    updated_at  = EXCLUDED.updated_at
            RETURNING {suiteColumns};
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

        // 🚨 `seq` burada DEPO tarafindan atomik uretilir (MAX+1 alt sorgusu),
        // cagiran hesaplamaz (docs/45-URETIMDEN-EVAL-KUMESI.md, bolum 45.2).
        // Es zamanli iki terfi ayni seq'i hesaplayabilir; bu durumda
        // eval_cases_suite_seq_uq ihlali SqlDialect.IsUniqueViolation ile
        // yakalanir ve SqlEvalStore yeniden dener. source_run_id catismasi
        // (ayni calistirma iki kez terfi) ayni yakalamaya duser ama farkli
        // yorumlanir: SqlEvalStore mevcut vakayi SelectEvalCaseBySourceRun ile
        // okur ve onu doner.
        InsertEvalCaseWithComputedSeq = $"""
            INSERT INTO {Schema}.eval_cases
                (id, suite_id, seq, query, expected_output, expected_tools, context,
                 source_run_id, source_kind, promoted_at)
            VALUES
                (@id, @suite_id,
                 COALESCE((SELECT MAX(seq) FROM {Schema}.eval_cases WHERE suite_id = @suite_id), -1) + 1,
                 @query, @expected_output, @expected_tools, @context,
                 @source_run_id, @source_kind, @promoted_at)
            RETURNING {evalCaseColumns};
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
            VALUES (@id, @tenant_id, @suite_id, @job_id, 0, @total, 0, 0, @started_at)
            RETURNING {evalRunColumns};
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
            ORDER BY started_at DESC
            OFFSET @skip LIMIT @take;
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

        // -------------------------------------------------------------------
        // Faz 21 -- kota
        // -------------------------------------------------------------------
        const string quotaColumns = """
            id, tenant_id, agent_name, period, max_runs, max_tokens, max_cost, enabled,
            created_at, updated_at
            """;

        // 🚨 Catisma hedefi COALESCE(agent_name, '') ifadesidir, sutun listesi
        // degil: PostgreSQL'de NULL'lar birbirine esit sayilmaz ve duz bir
        // (tenant_id, agent_name, period) hedefi, agent_name NULL olan ayni
        // kuralin sinirsiz kez eklenmesine izin verirdi. Benzersiz indeks de
        // ayni ifadeyle kuruludur (migration 0012).
        UpsertQuota = $"""
            INSERT INTO {Schema}.quotas
                ({quotaColumns})
            VALUES
                (@id, @tenant_id, @agent_name, @period, @max_runs, @max_tokens, @max_cost, @enabled,
                 @created_at, @updated_at)
            ON CONFLICT (tenant_id, COALESCE(agent_name, ''), period) DO UPDATE
               SET max_runs   = EXCLUDED.max_runs,
                   max_tokens = EXCLUDED.max_tokens,
                   max_cost   = EXCLUDED.max_cost,
                   enabled    = EXCLUDED.enabled,
                   updated_at = EXCLUDED.updated_at
            RETURNING {quotaColumns};
            """;

        SelectQuotas = $"""
            SELECT {quotaColumns}
            FROM {Schema}.quotas
            WHERE tenant_id = @tenant_id
            ORDER BY COALESCE(agent_name, ''), period;
            """;

        SelectQuota = $"""
            SELECT {quotaColumns}
            FROM {Schema}.quotas
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        DeleteQuota = $"""
            DELETE FROM {Schema}.quotas WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // Tuketim ATOMIK olarak artirilir. Eszamanli calistirmalar ayni satiri
        // artirir ve hicbir artis kaybolmaz; okuma-degistir-yaz dizisi olsaydi
        // ayni anda biten iki calistirmadan biri sessizce yutulurdu.
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

        SelectQuotaUsage = $"""
            SELECT tenant_id, agent_name, period, period_start, runs, tokens, cost, updated_at
            FROM {Schema}.quota_usage
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@period     IS NULL OR period     = @period)
            ORDER BY agent_name, period, period_start DESC;
            """;

        // -------------------------------------------------------------------
        // Faz 21 -- webhook
        // -------------------------------------------------------------------
        // 🚨 Sutun listesinde SIR YOKTUR: yalnizca secret_configuration_key
        // (anahtarin ADI) vardir (K-059).
        const string webhookSubscriptionColumns = """
            id, tenant_id, name, url, events, secret_configuration_key, headers, enabled,
            consecutive_failures, created_at, updated_at
            """;

        UpsertWebhookSubscription = $"""
            INSERT INTO {Schema}.webhook_subscriptions
                ({webhookSubscriptionColumns})
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
            RETURNING {webhookSubscriptionColumns};
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

        // events bir text[] sutunudur; = ANY(...) tam eslesme arar ve indeks
        // kullanabilir. LIKE tabanli bir arama 'run.completed' ararken
        // 'run.completed.v2' aboneligini de yanlislikla eslerdi.
        SelectWebhookSubscriptionsForEvent = $"""
            SELECT {webhookSubscriptionColumns}
            FROM {Schema}.webhook_subscriptions
            WHERE tenant_id = @tenant_id AND enabled = true AND @event_type = ANY (events)
            ORDER BY name;
            """;

        DeleteWebhookSubscription = $"""
            DELETE FROM {Schema}.webhook_subscriptions WHERE tenant_id = @tenant_id AND name = @name;
            """;

        // Ust uste basarisizlik sayaci ve otomatik kapatma TEK ifadede yapilir;
        // okuma-degistir-yaz dizisi eszamanli teslimlerle yarisirdi. Donen satir
        // "bu cagri aboneligi kapatti mi" sorusunu yanitlar.
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
            ORDER BY created_at DESC
            OFFSET @skip LIMIT @take;
            """;

        // -------------------------------------------------------------------
        // Faz 53 -- kiraci bazli API anahtarlari
        // -------------------------------------------------------------------
        // 🚨 Sutun listesinde HAM DEGER YOKTUR: yalnizca geri donduruleyemez
        // key_hash ozeti vardir (bolum 53.2).
        const string apiKeyColumns = """
            id, tenant_id, name, key_hash, key_prefix, scopes, expires_at, revoked_at,
            last_used_at, created_at
            """;

        InsertApiKey = $"""
            INSERT INTO {Schema}.api_keys
                ({apiKeyColumns})
            VALUES
                (@id, @tenant_id, @name, @key_hash, @key_prefix, @scopes, @expires_at, @revoked_at,
                 @last_used_at, @created_at);
            """;

        SelectApiKeys = $"""
            SELECT {apiKeyColumns}
            FROM {Schema}.api_keys
            WHERE tenant_id = @tenant_id
            ORDER BY created_at;
            """;

        // Kiraci suzgeci BILEREK yoktur (bolum 53.5): kiraci bu sorgunun
        // ciktisidir, girdisi degil.
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

        // Kurulum saglik denetimi (ExternalSurfaceGuard, bolum 53.4): kiraci
        // suzgeci BILEREK yoktur.
        HasApiKeyWithScope = $"""
            SELECT EXISTS (
                SELECT 1 FROM {Schema}.api_keys
                WHERE revoked_at IS NULL
                  AND (expires_at IS NULL OR expires_at > @now)
                  AND @scope = ANY (scopes)
            );
            """;

        const string retentionPolicyColumns =
            "id, tenant_id, target, max_age_days, max_rows, archive, enabled, created_at, updated_at";

        UpsertRetentionPolicy = $"""
            INSERT INTO {Schema}.retention_policies
                ({retentionPolicyColumns})
            VALUES
                (@id, @tenant_id, @target, @max_age_days, @max_rows, @archive, @enabled, @created_at, @updated_at)
            ON CONFLICT (tenant_id, target) DO UPDATE
               SET max_age_days = EXCLUDED.max_age_days,
                   max_rows     = EXCLUDED.max_rows,
                   archive      = EXCLUDED.archive,
                   enabled      = EXCLUDED.enabled,
                   updated_at   = EXCLUDED.updated_at
            RETURNING {retentionPolicyColumns};
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
        // Faz 31 -- calistirma/mesaj puani
        // -------------------------------------------------------------------
        const string runScoreColumns =
            "id, tenant_id, run_id, message_id, kind, value, comment, source, author, created_at";

        // 🚨 Catisma hedefi COALESCE(message_id, '') ifadesidir (migration
        // 0017'deki benzersizlik indeksiyle BIREBIR ayni olmalidir); author
        // ise duz sutundur -- NULL oldugunda catisma hic olusmaz ve her
        // cagri yeni bir satir acar (K1: kimliksiz kurulumda sessiz bir
        // benzersizlik mekanizmasi getirilmez).
        UpsertRunScore = $"""
            INSERT INTO {Schema}.run_scores
                ({runScoreColumns})
            VALUES
                (@id, @tenant_id, @run_id, @message_id, @kind, @value, @comment, @source, @author, @created_at)
            ON CONFLICT (tenant_id, run_id, COALESCE(message_id, ''), author) DO UPDATE
               SET kind       = EXCLUDED.kind,
                   value      = EXCLUDED.value,
                   comment    = EXCLUDED.comment,
                   source     = EXCLUDED.source,
                   created_at = EXCLUDED.created_at
            RETURNING {runScoreColumns};
            """;

        SelectRunScores = $"""
            SELECT {runScoreColumns}
            FROM {Schema}.run_scores
            WHERE tenant_id = @tenant_id AND run_id = @run_id;
            """;

        DeleteRunScore = $"""
            DELETE FROM {Schema}.run_scores WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // Kira baskasindaysa ve suresi dolmamissa WHERE yanlis kalir; conflict
        // satiri GUNCELLENMEZ ve RETURNING hicbir satir uretmez (K-177'nin
        // PostgreSQL tarafi: tek ifadelik upsert, ikinci sonuc kumesi yoktur).
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

        RenewSingletonLease = $"""
            UPDATE {Schema}.singleton_leases
               SET expires_at = @expires_at, updated_at = @now
             WHERE name = @name AND owner_id = @owner_id;
            """;

        ReleaseSingletonLease = $"""
            DELETE FROM {Schema}.singleton_leases WHERE name = @name AND owner_id = @owner_id;
            """;

        // Catisma DO NOTHING/UPDATE degil DUZ INSERT'tir: cagiran taraf
        // SqlDialect.IsUniqueViolation ile ihlali yakalar ve mevcut kaydi
        // SelectIdempotencyKey ile okur (K-177'nin iki dalli upsert deseninden
        // FARKLI — burada "zaten var" bir hata degil, normal bir akis dalidir).
        InsertIdempotencyKey = $"""
            INSERT INTO {Schema}.idempotency_keys (tenant_id, "key", fingerprint, state, created_at)
            VALUES (@tenant_id, @key, @fingerprint, 0, @created_at);
            """;

        SelectIdempotencyKey = $"""
            SELECT state, fingerprint, status_code, content_type, body, run_id
              FROM {Schema}.idempotency_keys
             WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        CompleteIdempotencyKey = $"""
            UPDATE {Schema}.idempotency_keys
               SET state = 2, status_code = @status_code, content_type = @content_type,
                   body = @body, run_id = @run_id, completed_at = @completed_at
             WHERE tenant_id = @tenant_id AND "key" = @key;
            """;

        DeleteIdempotencyKey = $"""
            DELETE FROM {Schema}.idempotency_keys WHERE tenant_id = @tenant_id AND "key" = @key;
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

        // WHERE status = @status_pending: ikinci bir karar 0 satir etkiler,
        // DecideAsync bunu false olarak yorumlar.
        DecidePendingApproval = $"""
            UPDATE {Schema}.pending_approvals
               SET status = @status, decided_by = @decided_by, decided_at = @decided_at
             WHERE id = @id AND tenant_id = @tenant_id AND status = @status_pending;
            """;

        // ClaimOrphanedRuns ile AYNI desen: aday secimi + kapama TEK ifadede,
        // RETURNING ile kapatilan satirlar okunur. Kiraci suzgeci YOKTUR — bir
        // bakim islemidir.
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
                       decided_by, decided_at, expires_at, created_at;
            """;
    }
}
