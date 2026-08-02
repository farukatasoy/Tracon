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
            INSERT INTO {Schema}.runs (id, tenant_id, agent_name, session_id, status, started_at, is_streaming, event_count)
            VALUES (@id, @tenant_id, @agent_name, @session_id, @status, @started_at, @is_streaming, 0);
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

        SelectRun = $"""
            SELECT id, tenant_id, agent_name, session_id, status, started_at, completed_at, is_streaming,
                   input_tokens, output_tokens, total_tokens, event_count, error_type, error_message
            FROM {Schema}.runs
            WHERE id = @id AND tenant_id = @tenant_id;
            """;

        SelectRuns = $"""
            SELECT id, tenant_id, agent_name, session_id, status, started_at, completed_at, is_streaming,
                   input_tokens, output_tokens, total_tokens, event_count, error_type, error_message
            FROM {Schema}.runs
            WHERE tenant_id = @tenant_id
              AND (@agent_name IS NULL OR agent_name = @agent_name)
              AND (@status     IS NULL OR status     = @status)
              AND (@session_id IS NULL OR session_id = @session_id)
              AND (@started_after IS NULL OR started_at > @started_after)
            ORDER BY started_at DESC, id DESC
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
    }

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
