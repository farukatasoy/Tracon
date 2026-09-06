-- Per-user session ownership.
--
-- For the rationale, the null semantics and the "no foreign key" decision see
-- PostgreSQL 0047_session_owner.sql.
--
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes
-- from the migration runner, which runs this file exactly once.
--
-- 🚨 The index name carries the TABLE PREFIX (K-193): SQLite index names share
-- one database-wide namespace.

ALTER TABLE {schema}sessions ADD COLUMN owner_id TEXT NULL;

CREATE INDEX IF NOT EXISTS {schema}sessions_tenant_owner_updated_idx
    ON {schema}sessions (tenant_id, owner_id, updated_at DESC)
    WHERE owner_id IS NOT NULL;
