namespace AgentPrism;

/// <summary>
/// Sema adina gore olusturulmus SQL metinleri.
/// </summary>
/// <remarks>
/// <para>
/// Sema adi bir tanimlayicidir ve parametre olarak gonderilemez; SQL metnine
/// dogrudan yerlestirilir. Ad, <see cref="SqlIdentifier.RequireSchemaName"/> ile
/// kati bicimde dogrulandiktan sonra kullanilir.
/// </para>
/// <para>
/// Tum sorgu metinleri bir kez kurulur ve alan olarak saklanir; her cagrida yeniden
/// birlestirme yapilmaz.
/// </para>
/// </remarks>
internal sealed class SqlQueries
{
    /// <summary>Yeni bir sorgu kumesi olusturur.</summary>
    /// <param name="schemaName">Dogrulanacak sema adi.</param>
    /// <exception cref="AgentPrismException">Sema adi gecerli bir tanimlayici degilse.</exception>
    public SqlQueries(string schemaName)
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
            SELECT v.definition
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
            ON CONFLICT (id) DO UPDATE
                SET tenant_id      = EXCLUDED.tenant_id,
                    agent_name     = EXCLUDED.agent_name,
                    state          = EXCLUDED.state,
                    schema_version = EXCLUDED.schema_version,
                    updated_at     = EXCLUDED.updated_at;
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
            ORDER BY updated_at DESC
            OFFSET @skip LIMIT @take;
            """;

        // --- Calistirmalar ---

        InsertRun = $"""
            INSERT INTO {Schema}.runs (id, tenant_id, agent_name, session_id, model_id, status, started_at, is_streaming, event_count,
                                       parent_run_id, root_run_id, depth)
            VALUES (@id, @tenant_id, @agent_name, @session_id, @model_id, @status, @started_at, @is_streaming, 0,
                    @parent_run_id, @root_run_id, @depth);
            """;

        UpdateRunCompletion = $"""
            UPDATE {Schema}.runs
            SET status        = @status,
                completed_at  = @completed_at,
                event_count   = @event_count,
                input_tokens  = @input_tokens,
                output_tokens = @output_tokens,
                total_tokens  = @total_tokens,
                error_type    = @error_type,
                error_message = @error_message
            WHERE id = @id;
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
                       COUNT(sub.total_tokens)::bigint             AS usage_rows
                FROM {Schema}.runs AS sub
                WHERE sub.tenant_id = r.tenant_id AND sub.root_run_id = r.id
            ) AS tree ON TRUE
            """;

        const string runColumns = """
            r.id, r.tenant_id, r.agent_name, r.session_id, r.status, r.started_at, r.completed_at, r.is_streaming,
            r.input_tokens, r.output_tokens, r.total_tokens, r.event_count, r.error_type, r.error_message, r.model_id,
            r.parent_run_id, r.root_run_id, r.depth,
            children.child_count,
            tree.input_tokens, tree.output_tokens, tree.total_tokens, tree.usage_rows
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
        SelectRunStatistics = $"""
            SELECT COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_completed)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_failed)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_canceled)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_running)::bigint,
                   COALESCE(SUM(input_tokens), 0)::bigint,
                   COALESCE(SUM(output_tokens), 0)::bigint,
                   COALESCE(SUM(total_tokens), 0)::bigint
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after);

            SELECT agent_name,
                   COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = @status_failed)::bigint,
                   COALESCE(SUM(total_tokens), 0)::bigint
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY agent_name
            ORDER BY COUNT(*) DESC, agent_name
            LIMIT @max_agents;

            SELECT model_id,
                   COUNT(*)::bigint,
                   COALESCE(SUM(input_tokens), 0)::bigint,
                   COALESCE(SUM(output_tokens), 0)::bigint,
                   COALESCE(SUM(total_tokens), 0)::bigint
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND model_id IS NOT NULL
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@started_after IS NULL OR started_at > @started_after)
            GROUP BY model_id
            ORDER BY COUNT(*) DESC, model_id
            LIMIT @max_agents;
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

        // --- Tool cagrilari (Faz 6) ---

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

        SelectMcpServers = $"""
            SELECT id, tenant_id, name, description, endpoint, transport,
                   authorization_configuration_key, headers, enabled, requires_approval, created_at, updated_at
            FROM {Schema}.mcp_servers
            WHERE tenant_id = @tenant_id
            ORDER BY name;
            """;

        SelectMcpServer = $"""
            SELECT id, tenant_id, name, description, endpoint, transport,
                   authorization_configuration_key, headers, enabled, requires_approval, created_at, updated_at
            FROM {Schema}.mcp_servers
            WHERE tenant_id = @tenant_id AND name = @name;
            """;

        UpsertMcpServer = $"""
            INSERT INTO {Schema}.mcp_servers
                (id, tenant_id, name, description, endpoint, transport,
                 authorization_configuration_key, headers, enabled, requires_approval, created_at, updated_at)
            VALUES
                (@id, @tenant_id, @name, @description, @endpoint, @transport,
                 @authorization_configuration_key, @headers, @enabled, @requires_approval, @now, @now)
            ON CONFLICT (tenant_id, name) DO UPDATE
                SET description                     = EXCLUDED.description,
                    endpoint                        = EXCLUDED.endpoint,
                    transport                       = EXCLUDED.transport,
                    authorization_configuration_key = EXCLUDED.authorization_configuration_key,
                    headers                         = EXCLUDED.headers,
                    enabled                         = EXCLUDED.enabled,
                    requires_approval               = EXCLUDED.requires_approval,
                    updated_at                      = EXCLUDED.updated_at
            RETURNING id, tenant_id, name, description, endpoint, transport,
                      authorization_configuration_key, headers, enabled, requires_approval, created_at, updated_at;
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
    }

    /// <summary>Bir tool cagrisi kaydi ekler.</summary>
    public string InsertToolInvocation { get; }

    /// <summary>Bir calistirmanin tool cagrilarini listeler.</summary>
    public string SelectToolInvocations { get; }

    /// <summary>Tool bazinda kullanim ozetini cikarir.</summary>
    public string SelectToolUsage { get; }

    /// <summary>Trace basligini ekler veya gunceller ve kimligini dondurur.</summary>
    public string UpsertTrace { get; }

    /// <summary>Bir span'i ekler veya gunceller.</summary>
    public string UpsertSpan { get; }

    /// <summary>Bir calistirmanin trace basligini getirir.</summary>
    public string SelectTraceByRun { get; }

    /// <summary>Bir trace'in span'lerini getirir.</summary>
    public string SelectSpans { get; }

    /// <summary>Bir kiracinin onay kurallarini listeler.</summary>
    public string SelectToolApprovalRules { get; }

    /// <summary>Bir onay kurali ekler; ayni kapsam varsa mevcut kaydi dondurur.</summary>
    public string InsertToolApprovalRule { get; }

    /// <summary>Bir onay kuralini siler.</summary>
    public string DeleteToolApprovalRule { get; }

    /// <summary>Bir kiracinin MCP sunucularini listeler.</summary>
    public string SelectMcpServers { get; }

    /// <summary>Tek bir MCP sunucusunu getirir.</summary>
    public string SelectMcpServer { get; }

    /// <summary>Bir MCP sunucusunu ekler veya gunceller.</summary>
    public string UpsertMcpServer { get; }

    /// <summary>Bir MCP sunucusunu siler.</summary>
    public string DeleteMcpServer { get; }

    /// <summary>Kayitli kiracilari listeler.</summary>
    public string SelectTenants { get; }

    /// <summary>Bir kiraci kaydini ekler veya gunceller.</summary>
    public string UpsertTenantDescriptor { get; }

    /// <summary>Bir kiraci kaydini siler.</summary>
    public string DeleteTenant { get; }

    /// <summary>Bir denetim izi kaydi ekler.</summary>
    public string InsertAuditEntry { get; }

    /// <summary>Denetim izi kayitlarini filtreleyerek okur.</summary>
    public string SelectAuditLog { get; }

    /// <summary>Dogrulanmis sema adi.</summary>
    public string Schema { get; }

    /// <summary>Semayi olusturur.</summary>
    public string CreateSchema { get; }

    /// <summary>Migration defterini olusturur.</summary>
    public string CreateMigrationsTable { get; }

    /// <summary>Uygulanmis migration'lari okur.</summary>
    public string SelectAppliedMigrations { get; }

    /// <summary>Uygulanan bir migration'i deftere yazar.</summary>
    public string InsertMigration { get; }

    /// <summary>Kiraci kaydini yoksa ekler.</summary>
    public string UpsertTenant { get; }

    /// <summary>Agent tanimini ekler veya surumunu artirir.</summary>
    public string UpsertAgentDefinition { get; }

    /// <summary>Tanimin degismez surum kaydini ekler.</summary>
    public string InsertAgentDefinitionVersion { get; }

    /// <summary>Bir tanimin guncel surumunu okur.</summary>
    public string SelectAgentDefinition { get; }

    /// <summary>Kiracinin tum tanimlarini okur.</summary>
    public string SelectAgentDefinitions { get; }

    /// <summary>Tanimi ve gecmisini siler.</summary>
    public string DeleteAgentDefinition { get; }

    /// <summary>Bir tanimin tum surumlerini okur.</summary>
    public string SelectAgentDefinitionVersions { get; }

    /// <summary>Bir tanimin belirli bir surumunu okur.</summary>
    public string SelectAgentDefinitionVersion { get; }

    /// <summary>Kiracinin skill'lerini listeler.</summary>
    public string SelectAgentSkills { get; }

    /// <summary>Tek bir skill'i okur.</summary>
    public string SelectAgentSkill { get; }

    /// <summary>Skill'i ekler veya gunceller.</summary>
    public string UpsertAgentSkill { get; }

    /// <summary>Skill'i siler.</summary>
    public string DeleteAgentSkill { get; }

    /// <summary>Skill'in kaynaklarini siler.</summary>
    public string DeleteAgentSkillResources { get; }

    /// <summary>Skill kaynagini ekler.</summary>
    public string InsertAgentSkillResource { get; }

    /// <summary>Skill kaynaklarini listeler.</summary>
    public string SelectAgentSkillResources { get; }

    /// <summary>Bir skill'in tum script'lerini siler.</summary>
    public string DeleteAgentSkillScripts { get; }

    /// <summary>Bir skill script'i ekler.</summary>
    public string InsertAgentSkillScript { get; }

    /// <summary>Bir skill'in script'lerini okur.</summary>
    public string SelectAgentSkillScripts { get; }

    /// <summary>Bir kiracinin tum script calistirma izinlerini okur.</summary>
    public string SelectSkillScriptGrants { get; }

    /// <summary>Belirli bir script icin gecerli izni okur.</summary>
    public string SelectActiveSkillScriptGrant { get; }

    /// <summary>Bir script calistirma izni ekler veya yeniler.</summary>
    public string UpsertSkillScriptGrant { get; }

    /// <summary>Bir script calistirma iznini iptal eder.</summary>
    public string RevokeSkillScriptGrant { get; }

    /// <summary>Oturumu ekler veya gunceller.</summary>
    public string UpsertSession { get; }

    /// <summary>Oturumu okur.</summary>
    public string SelectSession { get; }

    /// <summary>Oturumu siler.</summary>
    public string DeleteSession { get; }

    /// <summary>Oturumlari filtreleyerek okur.</summary>
    public string SelectSessions { get; }

    /// <summary>Yeni calistirma kaydi acar.</summary>
    public string InsertRun { get; }

    /// <summary>Calistirmayi sonlandirir.</summary>
    public string UpdateRunCompletion { get; }

    /// <summary>Bir calistirmayi okur.</summary>
    public string SelectRun { get; }

    /// <summary>Calistirmalari filtreleyerek okur.</summary>
    public string SelectRuns { get; }

    /// <summary>Calistirma ozetini ve agent kirilimini iki sonuc kumesi olarak dondurur.</summary>
    public string SelectRunStatistics { get; }

    /// <summary>Calistirma olayi ekler.</summary>
    public string InsertRunEvent { get; }

    /// <summary>Calistirma olaylarini sira numarasina gore okur.</summary>
    public string SelectRunEvents { get; }

    /// <summary>Konusmayi ekler veya gunceller.</summary>
    public string UpsertConversation { get; }

    /// <summary>Konusmadaki siradaki sira numarasini dondurur.</summary>
    public string SelectNextConversationSequence { get; }

    /// <summary>Konusmaya mesaj ekler.</summary>
    public string InsertConversationItem { get; }

    /// <summary>Konusmanin mesajlarini sirali okur.</summary>
    public string SelectConversationItems { get; }

    /// <summary>Gomulu migration metnindeki sema yer tutucusunu gercek adla degistirir.</summary>
    /// <param name="sql">Ham migration metni.</param>
    /// <returns>Calistirilabilir SQL.</returns>
    public string ApplySchema(string sql)
        => sql.Replace(SchemaPlaceholder, Schema, StringComparison.Ordinal);

    /// <summary>Gomulu SQL dosyalarinda sema adinin yerine gecen isaret.</summary>
    public const string SchemaPlaceholder = "{schema}";
}
