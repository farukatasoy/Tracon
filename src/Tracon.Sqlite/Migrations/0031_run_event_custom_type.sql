-- Phase 141 -- the run event stream gains an escape hatch for a consumer's
-- own event: RunEventType.Custom, qualified by a namespaced CustomType string.
--
-- For the rationale see PostgreSQL 0044_run_event_custom_type.sql. SQLite has
-- no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes from the
-- migration runner (the same file runs once).
ALTER TABLE {schema}run_events ADD COLUMN custom_type TEXT NULL;
