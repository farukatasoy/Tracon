namespace AgentPrism;

/// <summary>
/// <see cref="SqlQueriesBase"/> yuzeyinin SQL Server (T-SQL) metinleri.
/// </summary>
/// <remarks>
/// <para>
/// Sorgu <em>adlari</em> ve dondurdukleri sutun sirasi PostgreSQL ile birebir
/// aynidir; paylasilan depo kodu ikisini ayirt etmez. Farklar yalnizca metnin
/// icindedir.
/// </para>
/// <para>Uygulanan ceviri kurallari:</para>
/// <list type="bullet">
///   <item><description>
///     <strong><c>MERGE</c> KULLANILMAZ.</strong> Ifadenin bilinen esszamanlilik
///     ve dogruluk sorunlari vardir. Upsert'ler
///     <c>UPDATE ... WITH (UPDLOCK, SERIALIZABLE) ... OUTPUT</c> ve ardindan
///     <c>IF ROWCOUNT = 0 INSERT ... OUTPUT</c> ile yazilir. <c>SERIALIZABLE</c>
///     ipucu aralik kilidi alir; boylece iki oturum ayni anahtari ayni anda
///     ekleyemez. Gerekce: <c>docs/KARARLAR.md</c>, karar K-177.
///   </description></item>
///   <item><description>
///     <c>RETURNING</c> -> <c>OUTPUT inserted.*</c> / <c>OUTPUT deleted.*</c>.
///     Upsert'in iki dali da <em>ayni sutunlari</em> dondurur; bu yuzden C#
///     tarafinda tek bir okuyucu yeter.
///   </description></item>
///   <item><description>
///     <c>COUNT(*) FILTER (WHERE p)</c> -> <c>COALESCE(SUM(CASE WHEN p THEN 1 ELSE 0 END), 0)</c>.
///     🚨 <c>COALESCE</c> zorunludur: bos kume uzerinde <c>SUM</c> <c>NULL</c>
///     dondururken PostgreSQL'in <c>COUNT</c>'u sifir donduruyordu.
///   </description></item>
///   <item><description>
///     <c>LEAST</c> / <c>GREATEST</c> SQL Server 2019'da <strong>yoktur</strong>
///     (2022 ile geldi) ve <c>CASE</c> ile yazilir.
///   </description></item>
///   <item><description>
///     <c>FOR UPDATE SKIP LOCKED</c> -> <c>WITH (UPDLOCK, READPAST, ROWLOCK)</c>.
///   </description></item>
///   <item><description>
///     <c>UNNEST</c> ve <c>= ANY(dizi)</c> -> <c>OPENJSON</c>; diziler JSON metni
///     olarak tasinir (K-182).
///   </description></item>
///   <item><description>
///     🚨 <c>OFFSET ... FETCH NEXT @take ROWS ONLY</c> <c>@take = 0</c> iken
///     <strong>hata verir</strong>; PostgreSQL'de <c>LIMIT 0</c> bos liste
///     dondururdu. Davranis esitligi icin sayfali sorgular <c>@take &gt; 0</c>
///     kosulunu WHERE'e ekler ve <c>FETCH</c> degerini en az bire sabitler.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class SqlServerQueries : SqlQueriesBase
{
    /// <summary>
    /// Sayfali sorgularin sonuna eklenen atlama/alma yan tumcesi.
    /// </summary>
    /// <remarks>
    /// <c>@take = 0</c> durumu WHERE tarafinda elenir (bkz. <see cref="TakeGuard"/>);
    /// buradaki <c>CASE</c> yalnizca <c>FETCH</c>'in sifir gormesini onler.
    /// </remarks>
    private const string Paging = "OFFSET @skip ROWS FETCH NEXT (CASE WHEN @take < 1 THEN 1 ELSE @take END) ROWS ONLY;";

    /// <summary>Sayfali sorgularin WHERE tumcesine eklenen sifir koruma kosulu.</summary>
    private const string TakeGuard = "AND @take > 0";

    /// <summary>Yeni bir sorgu kumesi olusturur.</summary>
    /// <param name="schemaName">Dogrulanacak sema adi.</param>
    /// <exception cref="AgentPrismException">Sema adi gecerli bir tanimlayici degilse.</exception>
    public SqlServerQueries(string schemaName)
    {
        Schema = SqlIdentifier.RequireSchemaName(schemaName);

        // CREATE SCHEMA bir toplu islemin ILK ifadesi olmak zorundadir; bu yuzden
        // kosullu calistirma EXEC ile sarilir.
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

        // --- Agent tanimlari ---

        UpsertAgentDefinition = $"""
            UPDATE {Schema}.agent_definitions WITH (UPDLOCK, SERIALIZABLE)
               SET version    = version + 1,
                   definition = @definition,
                   updated_at = @now
             OUTPUT inserted.id, inserted.version
             WHERE tenant_id = @tenant_id AND name = @name;

            IF ROWCOUNT = 0
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

        // --- Skill'ler ---

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

            IF ROWCOUNT = 0
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

        const string grantColumns = """
            id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at
            """;

        SelectSkillScriptGrants = $"""
            SELECT {grantColumns}
            FROM {Schema}.skill_script_grants
            WHERE tenant_id = @tenant_id
            ORDER BY skill_name, ISNULL(script_name, N'');
            """;

        // Dar izin genis olani yener: script'e ozgu kayit once gelsin diye
        // script_name IS NOT NULL olanlar basa siralanir ve tek satir alinir.
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

        // SQL Server benzersiz indekste NULL'lari ESIT sayar; PostgreSQL'in
        // COALESCE'li ifade indeksi burada gerekmez (K-184). Eslesme yine de
        // ISNULL ile yazilir: @script_name NULL iken `= ` karsilastirmasi
        // UNKNOWN dondururdu.
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

            IF ROWCOUNT = 0
            INSERT INTO {Schema}.skill_script_grants ({grantColumns})
            OUTPUT inserted.id, inserted.tenant_id, inserted.skill_name, inserted.script_name,
                   inserted.granted_by, inserted.granted_at, inserted.expires_at, inserted.revoked_at
            VALUES (@id, @tenant_id, @skill_name, @script_name, @granted_by, @granted_at, @expires_at, NULL);
            """;

        // Izin SILINMEZ, iptal edilir.
        RevokeSkillScriptGrant = $"""
            UPDATE {Schema}.skill_script_grants
               SET revoked_at = @revoked_at
             WHERE tenant_id = @tenant_id
               AND skill_name = @skill_name
               AND ISNULL(script_name, N'') = ISNULL(@script_name, N'')
               AND revoked_at IS NULL;
            """;

        // --- Oturumlar ---

        UpsertSession = $"""
            UPDATE {Schema}.sessions WITH (UPDLOCK, SERIALIZABLE)
               SET tenant_id      = @tenant_id,
                   agent_name     = @agent_name,
                   state          = @state,
                   schema_version = @schema_version,
                   updated_at     = @updated_at
             WHERE id = @id;

            IF ROWCOUNT = 0
            INSERT INTO {Schema}.sessions (id, tenant_id, agent_name, state, schema_version, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @state, @schema_version, @created_at, @updated_at);
            """;

        SelectSession = $"""
            SELECT agent_name, state, schema_version, created_at, updated_at, tenant_id
            FROM {Schema}.sessions
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

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

        // --- Calistirmalar ---

        InsertRun = $"""
            INSERT INTO {Schema}.runs (id, tenant_id, agent_name, session_id, model_id, status, started_at, is_streaming, event_count,
                                       parent_run_id, root_run_id, depth, kind, workflow_name, agent_version, experiment_id, variant)
            VALUES (@id, @tenant_id, @agent_name, @session_id, @model_id, @status, @started_at, @is_streaming, 0,
                    @parent_run_id, @root_run_id, @depth, @kind, @workflow_name, @agent_version, @experiment_id, @variant);
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
                   input_cost     = @input_cost,
                   output_cost    = @output_cost,
                   cost_currency  = @cost_currency,
                   pricing_source = @pricing_source
             WHERE id = @id;
            """;

        UpdateRunCost = $"""
            UPDATE {Schema}.runs
               SET input_cost     = @input_cost,
                   output_cost    = @output_cost,
                   cost_currency  = @cost_currency,
                   pricing_source = @pricing_source
             WHERE id = @id;
            """;

        // PostgreSQL'in LEFT JOIN LATERAL ... ON TRUE yapisinin karsiligi
        // OUTER APPLY'dir. Agac toplamlari OKUMADA hesaplanir, saklanmaz.
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

        // 🚨 Sutun sirasi PostgreSQL ile BIREBIR aynidir: SqlRunStore.ReadRun
        // sabit sira numarasiyla okur ve iki saglayici ayni okuyucuyu paylasir.
        const string runColumns = """
            r.id, r.tenant_id, r.agent_name, r.session_id, r.status, r.started_at, r.completed_at, r.is_streaming,
            r.input_tokens, r.output_tokens, r.total_tokens, r.event_count, r.error_type, r.error_message, r.model_id,
            r.parent_run_id, r.root_run_id, r.depth,
            children.child_count,
            tree.input_tokens, tree.output_tokens, tree.total_tokens, tree.usage_rows,
            r.kind, r.workflow_name, r.agent_version, r.experiment_id, r.variant,
            r.input_cost, r.output_cost, r.cost_currency, r.pricing_source,
            tree.cost_input, tree.cost_output, tree.cost_currency, tree.unknown_pricing_rows, tree.pricing_rows
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
              AND (@session_id IS NULL OR r.session_id = @session_id)
              AND (@started_after IS NULL OR r.started_at > @started_after)
              AND (@root_run_id IS NULL OR r.root_run_id = @root_run_id OR r.id = @root_run_id)
              AND (
                    -- Ebeveyn filtresi verildiyse kok filtresi BILEREK yok sayilir.
                    (@parent_run_id IS NOT NULL AND r.parent_run_id = @parent_run_id)
                 OR (@parent_run_id IS NULL AND (@only_root_runs = 0 OR r.parent_run_id IS NULL))
              )
              {TakeGuard}
            ORDER BY r.started_at DESC, r.id DESC
            {Paging}
            """;

        // Dort sonuc kumesi tek gidis donuste alinir.
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
                   CAST(COALESCE(SUM(CASE WHEN pricing_source = @pricing_source_unknown THEN 1 ELSE 0 END), 0) AS bigint)
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
            """;

        InsertRunEvent = $"""
            INSERT INTO {Schema}.run_events (run_id, seq, type, text, tool_name, tool_call_id, payload, created_at)
            VALUES (@run_id, @seq, @type, @text, @tool_name, @tool_call_id, @payload, @created_at);
            """;

        SelectRunEvents = $"""
            SELECT e.run_id, e.seq, e.type, e.text, e.tool_name, e.tool_call_id, e.payload, e.created_at
            FROM {Schema}.run_events e
            JOIN {Schema}.runs r ON r.id = e.run_id
            WHERE e.run_id = @run_id AND e.seq >= @from_sequence AND r.tenant_id = @tenant_id
            ORDER BY e.seq;
            """;

        // --- Konusmalar (sohbet gecmisi) ---

        // UPDATE satiri kilitler; boylece ayni konusmaya es zamanli yazan iki
        // islem sira numarasi icin sirayla bekler.
        UpsertConversation = $"""
            UPDATE {Schema}.conversations WITH (UPDLOCK, SERIALIZABLE)
               SET updated_at = @now
             WHERE id = @id;

            IF ROWCOUNT = 0
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

        // --- Tool cagrilari ---

        InsertToolInvocation = $"""
            INSERT INTO {Schema}.tool_invocations
                (id, run_id, tool_name, tool_call_id, source, arguments, result, duration_ms, error, created_at)
            VALUES
                (@id, @run_id, @tool_name, @tool_call_id, @source, @arguments, @result, @duration_ms, @error, @created_at);
            """;

        SelectToolInvocations = $"""
            SELECT t.id, t.run_id, t.tool_name, t.tool_call_id, t.source, t.arguments, t.result,
                   t.duration_ms, t.error, t.created_at
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

        // --- Deneyler ---

        const string experimentColumns = """
            id, tenant_id, name, agent_name, variants, status, assignment_key, started_at, ended_at, updated_at
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

        // 🚨 Draft-disi bir deney icin UPDATE sifir satir etkiler; INSERT dali
        // yalnizca kayit HIC YOKSA calisir. Aksi halde benzersizlik ihlali
        // olusurdu. `ROWCOUNT` degeri once bir degiskene alinir: bilesik bir
        // kosulda alt sorgu once degerlendirilirse sayac sifirlanirdi.
        UpsertExperiment = $"""
            DECLARE @updated int;

            UPDATE {Schema}.experiments WITH (UPDLOCK, SERIALIZABLE)
               SET agent_name     = @agent_name,
                   variants       = @variants,
                   assignment_key = @assignment_key,
                   updated_at     = @updated_at
             OUTPUT inserted.id
             WHERE tenant_id = @tenant_id AND name = @name AND status = 0;

            SET @updated = ROWCOUNT;

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

        // EXTRACT(EPOCH FROM (a - b)) * 1000 -> DATEDIFF_BIG(millisecond, b, a).
        SelectExperimentResults = $"""
            SELECT variant,
                   MAX(agent_version),
                   CAST(COUNT(*) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_completed THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_failed    THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(CASE WHEN status = @status_canceled  THEN 1 ELSE 0 END), 0) AS bigint),
                   CAST(COALESCE(SUM(input_tokens), 0) AS bigint),
                   CAST(COALESCE(SUM(output_tokens), 0) AS bigint),
                   CAST(COALESCE(SUM(total_tokens), 0) AS bigint),
                   AVG(CASE WHEN completed_at IS NOT NULL
                            THEN CAST(DATEDIFF_BIG(millisecond, started_at, completed_at) AS float) END),
                   CASE WHEN COALESCE(SUM(CASE WHEN input_cost IS NOT NULL OR output_cost IS NOT NULL THEN 1 ELSE 0 END), 0) = 0
                        THEN NULL ELSE COALESCE(SUM(input_cost), 0) + COALESCE(SUM(output_cost), 0) END,
                   MAX(cost_currency)
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id AND experiment_id = @experiment_id AND variant IS NOT NULL
            GROUP BY variant;
            """;

        // 🚨 generate_series'in karsiligi ozyinelemeli bir CTE'dir.
        // date_trunc(@unit, x) parametrik DATEPART alamaz; CASE ile yazilir ve
        // hesap datetime2 uzerinde yapilip TODATETIMEOFFSET ile geri cevrilir
        // (degerler her zaman UTC yazilir, bu yuzden ofset sifirdir).
        //
        // Bos kovalar da doner: aksi halde grafikte kesinti "veri yok" degil
        // "sifir" gibi gorunur.
        SelectRunTimeSeries = $"""
            WITH buckets AS (
                SELECT CASE WHEN @bucket_unit = N'hour'
                            THEN DATEADD(hour, DATEDIFF(hour, 0, CONVERT(datetime2(7), @from_ts)), CONVERT(datetime2(7), 0))
                            ELSE DATEADD(day,  DATEDIFF(day,  0, CONVERT(datetime2(7), @from_ts)), CONVERT(datetime2(7), 0))
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
                            THEN DATEADD(hour, DATEDIFF(hour, 0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), 0))
                            ELSE DATEADD(day,  DATEDIFF(day,  0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), 0))
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
                              THEN DATEADD(hour, DATEDIFF(hour, 0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), 0))
                              ELSE DATEADD(day,  DATEDIFF(day,  0, CONVERT(datetime2(7), started_at)), CONVERT(datetime2(7), 0))
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

        // --- Span'ler ---

        // LEAST / GREATEST SQL Server 2019'da yoktur. PostgreSQL'de bu iki islev
        // NULL argumani ATLAR; CASE zinciri ayni davranisi kurar.
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

            IF ROWCOUNT = 0
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

            IF ROWCOUNT = 0
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

        // --- Tool onay kurallari ---

        const string approvalColumns = """
            id, tenant_id, agent_name, tool_name, arguments_hash, created_by, created_at
            """;

        SelectToolApprovalRules = $"""
            SELECT {approvalColumns}
            FROM {Schema}.tool_approval_rules
            WHERE tenant_id = @tenant_id
            ORDER BY created_at DESC;
            """;

        // Ayni kapsam icin ikinci bir kural acilmaz; mevcut kayit dondurulur.
        // UPDATE bilerek bir sutunu kendisiyle degistirir: amac yazmak degil,
        // var olan satiri OUTPUT ile geri vermektir.
        InsertToolApprovalRule = $"""
            UPDATE {Schema}.tool_approval_rules WITH (UPDLOCK, SERIALIZABLE)
               SET tool_name = tool_name
             OUTPUT inserted.id, inserted.tenant_id, inserted.agent_name, inserted.tool_name,
                    inserted.arguments_hash, inserted.created_by, inserted.created_at
             WHERE tenant_id = @tenant_id
               AND ISNULL(agent_name, N'') = ISNULL(@agent_name, N'')
               AND tool_name = @tool_name
               AND ISNULL(arguments_hash, N'') = ISNULL(@arguments_hash, N'');

            IF ROWCOUNT = 0
            INSERT INTO {Schema}.tool_approval_rules ({approvalColumns})
            OUTPUT inserted.id, inserted.tenant_id, inserted.agent_name, inserted.tool_name,
                   inserted.arguments_hash, inserted.created_by, inserted.created_at
            VALUES (@id, @tenant_id, @agent_name, @tool_name, @arguments_hash, @created_by, @created_at);
            """;

        DeleteToolApprovalRule = $"""
            DELETE FROM {Schema}.tool_approval_rules
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        // --- MCP sunuculari ---

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

            IF ROWCOUNT = 0
            INSERT INTO {Schema}.mcp_servers ({mcpServerColumns})
            OUTPUT {mcpServerOutput}
            VALUES (@id, @tenant_id, @name, @description, @endpoint, @transport,
                    @authorization_configuration_key, @headers, @enabled, @requires_approval, @now, @now,
                    @oauth_enabled, @oauth_client_id, @oauth_client_secret_configuration_key,
                    @oauth_scopes, @oauth_authorization_mode);
            """;

        DeleteMcpServer = $"DELETE FROM {Schema}.mcp_servers WHERE tenant_id = @tenant_id AND name = @name;";

        // --- Kiracilar ---

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

            IF ROWCOUNT = 0
            INSERT INTO {Schema}.tenants (id, slug, display_name, created_at)
            OUTPUT inserted.id, inserted.slug, inserted.display_name, inserted.created_at
            VALUES (@id, @slug, @display_name, @created_at);
            """;

        DeleteTenant = $"DELETE FROM {Schema}.tenants WHERE slug = @slug;";

        // --- Ekler ---

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

        // --- Kalici agent dosya bellegi ---

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

            IF ROWCOUNT = 0
            INSERT INTO {Schema}.agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at)
            VALUES (@id, @tenant_id, @agent_name, @path, @content, @now, @now);
            """;

        DeleteAgentFile = $"""
            DELETE FROM {Schema}.agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name AND path = @path;
            """;

        SelectAgentFiles = $"""
            SELECT path, content
            FROM {Schema}.agent_files
            WHERE tenant_id = @tenant_id AND agent_name = @agent_name
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

            IF ROWCOUNT = 0
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

        // --- Denetim izi ---

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

        // --- Zamanlama ve is kuyrugu ---

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

            IF ROWCOUNT = 0
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

        // Kiraciyla sinirlanmaz: bu sorgu isciye aittir, HTTP istegine degil.
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

        // 🚨 SQL Server'da dizi parametresi yoktur: `UNNEST(@ids, @inputs)
        // WITH ORDINALITY` yerine iki JSON dizisi OPENJSON ile acilir ve
        // `[key]` (0 tabanli sira) uzerinden eslenir. Gerekce: K-182.
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

        // 🚨 `FOR UPDATE SKIP LOCKED` karsiligi `WITH (UPDLOCK, READPAST, ROWLOCK)`
        // ipuclaridir: UPDLOCK secilen satiri yazma icin kilitler, READPAST baska
        // bir iscinin kilitledigi satiri ATLAR, ROWLOCK kilidi satir duzeyinde
        // tutar. Guncelleme CTE uzerinden yapilir; `UPDATE TOP (n)` ORDER BY
        // kabul etmez ve en eski isi almayi garanti edemezdi.
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

        // 🚨 T-SQL'de veri degistiren CTE yoktur; PostgreSQL'in
        // `WITH updated AS (UPDATE ... RETURNING)` yapisi bir tablo degiskenine
        // OUTPUT ile yazilarak kurulur. Idempotenttir: oge yalnizca hala Pending
        // (0) ise guncellenir, bu yuzden ayni oge iki kez raporlanirsa sayaclar
        // BIR KEZ DAHA artmaz.
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

            IF ROWCOUNT = 0
            INSERT INTO {Schema}.eval_suites ({suiteColumns})
            OUTPUT inserted.id, inserted.tenant_id, inserted.name, inserted.description,
                   inserted.agent_name, inserted.checks, inserted.created_at, inserted.updated_at
            VALUES (@id, @tenant_id, @name, @description, @agent_name, @checks, @now, @now);
            """;

        DeleteEvalSuite = $"DELETE FROM {Schema}.eval_suites WHERE tenant_id = @tenant_id AND name = @name;";

        SelectEvalCases = $"""
            SELECT id, suite_id, seq, query, expected_output, expected_tools, context
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

            IF ROWCOUNT = 0
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

        // Tuketim ATOMIK olarak artirilir. UPDATE ... SET x = x + @y tek
        // ifadedir; eszamanli calistirmalarda hicbir artis kaybolmaz.
        AddQuotaUsage = $"""
            UPDATE {Schema}.quota_usage WITH (UPDLOCK, SERIALIZABLE)
               SET runs       = runs   + @runs,
                   tokens     = tokens + @tokens,
                   cost       = cost   + @cost,
                   updated_at = @updated_at
             WHERE tenant_id = @tenant_id AND agent_name = @agent_name
               AND period = @period AND period_start = @period_start;

            IF ROWCOUNT = 0
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

        // 🚨 Sutun listesinde SIR YOKTUR: yalnizca secret_configuration_key
        // (anahtarin ADI) vardir (K-059).
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

            IF ROWCOUNT = 0
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

        // 🚨 `events` bir JSON dizisidir; PostgreSQL'in `@event_type = ANY(events)`
        // ifadesinin karsiligi OPENJSON uzerinde TAM eslesmedir. LIKE tabanli bir
        // arama 'run.completed' ararken 'run.completed.v2' aboneligini de
        // yanlislikla eslerdi.
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

        // Ust uste basarisizlik sayaci ve otomatik kapatma TEK ifadede yapilir.
        // Donen satir "bu cagri aboneligi kapatti mi" sorusunu yanitlar.
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
    }
}
