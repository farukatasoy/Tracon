-- Audit trail chain key.
--
-- For the rationale (a key id, never the key; no backfill; no index) see PostgreSQL
-- 0067_audit_chain_key.sql.
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes from the
-- migration runner (the same file runs once).

ALTER TABLE {schema}audit_log ADD COLUMN key_id TEXT NULL;
