-- Phase 64 -- audit trail hash chain.
--
-- For the rationale (nullable columns, no backfill, filtered unique index) see
-- PostgreSQL 0031_audit_chain.sql.
--
-- 🚨 There is no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes
-- from the migration runner (the same file does not run a second time).

ALTER TABLE {schema}audit_log ADD COLUMN prev_hash TEXT NULL;
ALTER TABLE {schema}audit_log ADD COLUMN hash TEXT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS {schema}audit_log_tenant_prev_hash_idx
    ON {schema}audit_log (tenant_id, COALESCE(prev_hash, ''))
    WHERE hash IS NOT NULL;
