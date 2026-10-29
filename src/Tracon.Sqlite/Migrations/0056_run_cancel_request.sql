-- Phase 262 (F-764) -- cross-instance run cancellation.
--
-- For the rationale and the column meaning see PostgreSQL 0069_run_cancel_request.sql.
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes from the
-- migration runner (the same file does not run a second time). The timestamp is ISO 8601
-- TEXT, like heartbeat_at (K-191's adjacent rule).

ALTER TABLE {schema}runs ADD COLUMN cancel_requested_at TEXT NULL;
