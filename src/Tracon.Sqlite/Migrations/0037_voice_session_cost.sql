-- Phase 161 -- the provider, the model and what a voice session cost.
--
-- For the rationale and the column meanings see PostgreSQL 0050_voice_session_cost.sql.
--
-- 🚨 SQLite has no `ADD COLUMN IF NOT EXISTS`. The guard is the migration ledger:
-- a numbered file runs exactly once, so a plain ADD COLUMN is correct here.
--
-- `numeric` needs no special handling: the driver writes TEXT and is culture
-- independent (the Precision/Scale duty of SQL Server IS ABSENT here).

ALTER TABLE {schema}voice_sessions ADD COLUMN provider       TEXT NULL;
ALTER TABLE {schema}voice_sessions ADD COLUMN model          TEXT NULL;
ALTER TABLE {schema}voice_sessions ADD COLUMN live_seconds   TEXT NULL;
ALTER TABLE {schema}voice_sessions ADD COLUMN duration_cost  TEXT NULL;
ALTER TABLE {schema}voice_sessions ADD COLUMN character_cost TEXT NULL;
ALTER TABLE {schema}voice_sessions ADD COLUMN currency       TEXT NULL;
