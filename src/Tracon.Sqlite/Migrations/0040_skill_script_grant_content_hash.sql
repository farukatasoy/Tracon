-- The content a script grant authorizes.
--
-- For the rationale see PostgreSQL 0053_skill_script_grant_content_hash.sql.
-- Existing rows stay NULL on purpose: no backfill.
--
-- 🚨 SQLite has no `ADD COLUMN IF NOT EXISTS`. The guard is the migration
-- ledger: a numbered file runs exactly once, so a plain ADD COLUMN is correct.

ALTER TABLE {schema}skill_script_grants ADD COLUMN content_hash TEXT NULL;
