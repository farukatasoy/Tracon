-- ---------------------------------------------------------------------------
-- 0030 — The job's kind becomes an open handler key (Phase 137)
--
-- For the rationale and the nine-value mapping see PostgreSQL
-- 0043_job_handler_key.sql. Migration numbers are independent per provider (K-178).
--
-- 🚨 DEVIATION FROM THE PLAN: the column is DROPPED IN PLACE, not moved by
-- rebuilding the table (the 0006_sessions_tenant_key.sql precedent).
-- Measured: `jobs` and `job_schedules` both have children under
-- `PRAGMA foreign_keys = ON` (job_items.job_id REFERENCES jobs ON DELETE
-- CASCADE; jobs.schedule_id REFERENCES job_schedules). With foreign keys on,
-- DROP TABLE performs an implicit DELETE FROM and DOES fire ON DELETE CASCADE
-- -- rebuilding `jobs` would silently delete EVERY row of `job_items`. The
-- usual escape (PRAGMA foreign_keys = OFF) is a no-op inside a transaction,
-- and MigrationRunner runs each migration inside one. 0006's table had no
-- children, which is why the rebuild was safe there and is not here.
-- ALTER TABLE ... DROP COLUMN needs SQLite 3.35+ (SQLitePCLRaw 2.1.12 ships
-- far newer) and refuses a column an index depends on -- `kind` is in no index.
--
-- 🚨 SQLite has no ALTER COLUMN, so NOT NULL has to come from the ADD itself,
-- and ADD COLUMN ... NOT NULL requires a default. The default is the empty
-- string, which is deliberately NOT a valid handler key: an insert that ever
-- omitted the column would produce a job that fails closed with
-- JobErrorCodes.UnknownHandlerKey, rather than a NULL that breaks the reader
-- (and with it the whole job-list endpoint) at GetString.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}jobs          ADD COLUMN handler_key TEXT NOT NULL DEFAULT '';
ALTER TABLE {schema}job_schedules ADD COLUMN handler_key TEXT NOT NULL DEFAULT '';

UPDATE {schema}jobs SET handler_key = CASE kind
    WHEN 0 THEN 'agentprism.agent-batch'
    WHEN 1 THEN 'agentprism.workflow'
    WHEN 2 THEN 'agentprism.eval'
    WHEN 3 THEN 'agentprism.webhook-delivery'
    WHEN 4 THEN 'agentprism.retention'
    WHEN 5 THEN 'agentprism.agent-run'
    WHEN 6 THEN 'agentprism.online-eval'
    WHEN 7 THEN 'agentprism.approval-resume'
    WHEN 8 THEN 'agentprism.run-continuation'
    ELSE ''
END;

UPDATE {schema}job_schedules SET handler_key = CASE kind
    WHEN 0 THEN 'agentprism.agent-batch'
    WHEN 1 THEN 'agentprism.workflow'
    WHEN 2 THEN 'agentprism.eval'
    WHEN 3 THEN 'agentprism.webhook-delivery'
    WHEN 4 THEN 'agentprism.retention'
    WHEN 5 THEN 'agentprism.agent-run'
    WHEN 6 THEN 'agentprism.online-eval'
    WHEN 7 THEN 'agentprism.approval-resume'
    WHEN 8 THEN 'agentprism.run-continuation'
    ELSE ''
END;

ALTER TABLE {schema}jobs          DROP COLUMN kind;
ALTER TABLE {schema}job_schedules DROP COLUMN kind;
