-- Phase 146 -- durable quota threshold notification dedup.
--
-- For the column rationale see PostgreSQL 0046_quota_threshold_notifications.sql.
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety
-- comes from the migration runner (the same file does not run a second time).

ALTER TABLE {schema}quota_usage ADD COLUMN notified_thresholds TEXT;
