-- Phase 87 -- interrupted-run continuation.
--
-- For the rationale and the column meaning see PostgreSQL
-- 0037_run_continuation.sql.

IF COL_LENGTH(N'{schema}.runs', N'continued_from_run_id') IS NULL
ALTER TABLE {schema}.runs ADD continued_from_run_id uniqueidentifier NULL;

-- 🚨 Wrapped in EXEC: continued_from_run_id is added above with ALTER TABLE in
-- the SAME batch; without EXEC it gives "Invalid column name" (see 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_continued_from_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
EXEC(N'CREATE INDEX runs_continued_from_idx
    ON {schema}.runs (tenant_id, continued_from_run_id)
    WHERE continued_from_run_id IS NOT NULL;');
