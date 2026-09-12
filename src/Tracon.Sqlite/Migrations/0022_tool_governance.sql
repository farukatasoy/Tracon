-- Phase 69 -- tool authorization (F-113) and execution timeout (F-114).
--
-- For the rationale see PostgreSQL 0035_tool_governance.sql. SQLite has no
-- boolean type; 0/1 on INTEGER is this schema's existing convention.

ALTER TABLE {schema}tool_invocations ADD COLUMN authorization_denied INTEGER NOT NULL DEFAULT 0;
ALTER TABLE {schema}tool_invocations ADD COLUMN timed_out            INTEGER NOT NULL DEFAULT 0;
