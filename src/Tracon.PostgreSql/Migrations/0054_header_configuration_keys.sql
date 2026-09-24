-- Credential headers declared by the NAME of their configuration key (phase 190).
--
-- An MCP server or webhook target that wants `X-API-Key`, `Cookie` or
-- `Ocp-Apim-Subscription-Key` could only get it through the plain `headers`
-- column, which stores the value in the clear (a K-059 violation). The new
-- column maps a header name to the configuration key its value is read from;
-- the value is resolved at connection or delivery time and never stored.
--
-- Same shape as `headers`: a JSON object, `{}` when empty. Existing rows read `{}`.

ALTER TABLE {schema}.mcp_servers
    ADD COLUMN IF NOT EXISTS header_configuration_keys jsonb NOT NULL DEFAULT '{}'::jsonb;

ALTER TABLE {schema}.webhook_subscriptions
    ADD COLUMN IF NOT EXISTS header_configuration_keys jsonb NOT NULL DEFAULT '{}'::jsonb;
