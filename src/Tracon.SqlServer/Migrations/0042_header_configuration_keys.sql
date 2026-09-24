-- Credential headers declared by the NAME of their configuration key (phase 190).
--
-- For the rationale see PostgreSQL 0054_header_configuration_keys.sql. Same shape
-- as `headers`: nvarchar(max), `{}` default, ISJSON check. Adding a NOT NULL
-- column with a DEFAULT fills every existing row with `{}`.

IF COL_LENGTH(N'{schema}.mcp_servers', N'header_configuration_keys') IS NULL
ALTER TABLE {schema}.mcp_servers ADD header_configuration_keys nvarchar(max) NOT NULL
    CONSTRAINT mcp_servers_header_configuration_keys_default DEFAULT N'{}'
    CONSTRAINT mcp_servers_header_configuration_keys_json CHECK (ISJSON(header_configuration_keys) = 1);

IF COL_LENGTH(N'{schema}.webhook_subscriptions', N'header_configuration_keys') IS NULL
ALTER TABLE {schema}.webhook_subscriptions ADD header_configuration_keys nvarchar(max) NOT NULL
    CONSTRAINT webhook_subscriptions_header_configuration_keys_default DEFAULT N'{}'
    CONSTRAINT webhook_subscriptions_header_configuration_keys_json CHECK (ISJSON(header_configuration_keys) = 1);
