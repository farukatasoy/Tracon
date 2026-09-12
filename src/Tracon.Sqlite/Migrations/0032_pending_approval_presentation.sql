-- Phase 142 -- tool-approval presentation.
--
-- For the column rationale see PostgreSQL 0045_pending_approval_presentation.sql.
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes
-- from the migration runner (the same file does not run a second time).

ALTER TABLE {schema}pending_approvals ADD COLUMN presentation TEXT;
