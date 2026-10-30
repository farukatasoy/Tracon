-- Storage of idempotency response headers.
--
-- For the rationale see PostgreSQL 0029_idempotency_response_headers.sql.
-- SQLite has no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes
-- from the migration runner (the same file does not run a second time).

ALTER TABLE {schema}idempotency_keys ADD COLUMN headers TEXT;
