-- Phase 22 -- MCP deepening: OAuth columns.
--
-- There is NO schema change for prompts and resources: the prompt snapshot is
-- carried inside AgentDefinition.Metadata (the existing jsonb definition column),
-- and Mode A resource references the same way with AgentDefinition.McpResourceUris.
-- Only the OAuth connection information needs new columns.

-- 🚨 client_secret IS NOT STORED (K-059). Only the name of the configuration key
-- that the value is read from is stored; the value is resolved at run time from IConfiguration.
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_enabled                    boolean     NOT NULL DEFAULT false;
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_client_id                  text;
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_client_secret_configuration_key text;
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_scopes                     text;
ALTER TABLE {schema}.mcp_servers ADD COLUMN IF NOT EXISTS oauth_authorization_mode         smallint    NOT NULL DEFAULT 0;
