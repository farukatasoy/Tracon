-- Faz 22 -- MCP derinlesmesi: OAuth kolonlari.
--
-- Prompts ve resources icin sema degisikligi YOKTUR: prompt anlik goruntusu
-- AgentDefinition.Metadata (mevcut jsonb definition sutunu) icinde tasinir,
-- Mod A kaynak referanslari AgentDefinition.McpResourceUris ile ayni sekilde.
-- Yalniz OAuth baglanti bilgisi yeni kolon gerektirir.

-- 🚨 client_secret SAKLANMAZ (K-059). Yalniz degerin okunacagi yapilandirma
-- anahtarinin adi saklanir; deger calisma aninda IConfiguration'dan cozulur.
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_enabled                    boolean     NOT NULL DEFAULT false;
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_client_id                  text;
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_client_secret_configuration_key text;
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_scopes                     text;
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_authorization_mode         smallint    NOT NULL DEFAULT 0;
