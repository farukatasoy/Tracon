-- Audit trail chain key.
--
-- For the rationale (a key id, never the key; no backfill; no index) see PostgreSQL
-- 0067_audit_chain_key.sql.
--
-- 64 characters is the longest key id the host accepts (Tracon:Audit:ChainKeyId).
IF COL_LENGTH(N'{schema}.audit_log', N'key_id') IS NULL
ALTER TABLE {schema}.audit_log ADD key_id nvarchar(64) NULL;
