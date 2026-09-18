-- A tool call that outlived its timeout and then settled anyway.
--
-- For the rationale see PostgreSQL 0052_tool_late_completion.sql.
--
-- 🚨 SQLite has no `ADD COLUMN IF NOT EXISTS`. The guard is the migration
-- ledger: a numbered file runs exactly once, so a plain ADD COLUMN is correct.

ALTER TABLE {schema}tool_invocations ADD COLUMN late_completed_at TEXT NULL;
