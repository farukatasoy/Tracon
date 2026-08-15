-- Phase 44 -- error classification and failure clustering.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0021_error_classification.sql. error_fingerprint is the hexadecimal form of a
-- SHA-256 digest (fixed length, 64 characters); nvarchar(64) fits it exactly.

IF COL_LENGTH(N'{schema}.runs', N'error_class') IS NULL
ALTER TABLE {schema}.runs ADD error_class smallint NULL;

IF COL_LENGTH(N'{schema}.runs', N'error_fingerprint') IS NULL
ALTER TABLE {schema}.runs ADD error_fingerprint nvarchar(64) NULL;

-- Partial (filtered) index: rows where error_class is NULL (the majority) take
-- no space in the index. The syntax is the same as PostgreSQL (K-178 handover note).
--
-- 🚨 Wrapped in EXEC: error_class is added above with ALTER TABLE in the SAME
-- batch; SQL Server compiles the WHOLE batch before running it and without EXEC
-- it gives "Invalid column name 'error_class'" (see 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_error_class_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
EXEC(N'CREATE INDEX runs_error_class_idx
    ON {schema}.runs (tenant_id, error_class, started_at DESC)
    WHERE error_class IS NOT NULL;');
