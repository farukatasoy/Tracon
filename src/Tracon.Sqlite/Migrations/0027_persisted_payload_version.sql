-- Persisted payload version stamps (Phase 126).
--
-- For the rationale see PostgreSQL 0040_persisted_payload_version.sql.
-- SQLite has no `ADD COLUMN IF NOT EXISTS`; migrations here run exactly
-- once, tracked by the migration table, so the bare statements are correct.
-- `RENAME COLUMN` (supported since SQLite 3.25.0) updates any dependent
-- view automatically; `sessions` has none (only `runs_v1` exists, and only
-- when EnableReadViews is on).

ALTER TABLE {schema}sessions RENAME COLUMN schema_version TO state_schema_version;
ALTER TABLE {schema}sessions ADD COLUMN state_maf_version TEXT;

ALTER TABLE {schema}workflow_checkpoints ADD COLUMN state_schema_version INTEGER;
ALTER TABLE {schema}workflow_checkpoints ADD COLUMN state_maf_version TEXT;
