-- ---------------------------------------------------------------------------
-- 0030 — The job's kind becomes an open handler key (Phase 137)
--
-- The PostgreSQL twin is 0043; migration numbers are independent per provider
-- (K-178). The rationale, the nine-value mapping and the statement order are
-- identical -- see that file's header.
--
-- 🚨 T-SQL compiles a batch UP FRONT: a statement that names `handler_key` in
-- the same batch as the ALTER TABLE that adds it fails with "Invalid column
-- name" (the K-475 class). MigrationRunner sends each file as ONE batch, so
-- every statement after the ADD goes through EXEC(N'...'), which is compiled
-- only when it runs.
-- ---------------------------------------------------------------------------

IF COL_LENGTH(N'{schema}.jobs', N'handler_key') IS NULL
    ALTER TABLE {schema}.jobs ADD handler_key nvarchar(200) NULL;

IF COL_LENGTH(N'{schema}.job_schedules', N'handler_key') IS NULL
    ALTER TABLE {schema}.job_schedules ADD handler_key nvarchar(200) NULL;

EXEC(N'
UPDATE {schema}.jobs SET handler_key = CASE kind
    WHEN 0 THEN N''tracon.agent-batch''
    WHEN 1 THEN N''tracon.workflow''
    WHEN 2 THEN N''tracon.eval''
    WHEN 3 THEN N''tracon.webhook-delivery''
    WHEN 4 THEN N''tracon.retention''
    WHEN 5 THEN N''tracon.agent-run''
    WHEN 6 THEN N''tracon.online-eval''
    WHEN 7 THEN N''tracon.approval-resume''
    WHEN 8 THEN N''tracon.run-continuation''
END
WHERE handler_key IS NULL;');

EXEC(N'
UPDATE {schema}.job_schedules SET handler_key = CASE kind
    WHEN 0 THEN N''tracon.agent-batch''
    WHEN 1 THEN N''tracon.workflow''
    WHEN 2 THEN N''tracon.eval''
    WHEN 3 THEN N''tracon.webhook-delivery''
    WHEN 4 THEN N''tracon.retention''
    WHEN 5 THEN N''tracon.agent-run''
    WHEN 6 THEN N''tracon.online-eval''
    WHEN 7 THEN N''tracon.approval-resume''
    WHEN 8 THEN N''tracon.run-continuation''
END
WHERE handler_key IS NULL;');

EXEC(N'ALTER TABLE {schema}.jobs ALTER COLUMN handler_key nvarchar(200) NOT NULL;');
EXEC(N'ALTER TABLE {schema}.job_schedules ALTER COLUMN handler_key nvarchar(200) NOT NULL;');

IF COL_LENGTH(N'{schema}.jobs', N'kind') IS NOT NULL
    EXEC(N'ALTER TABLE {schema}.jobs DROP COLUMN kind;');

IF COL_LENGTH(N'{schema}.job_schedules', N'kind') IS NOT NULL
    EXEC(N'ALTER TABLE {schema}.job_schedules DROP COLUMN kind;');
