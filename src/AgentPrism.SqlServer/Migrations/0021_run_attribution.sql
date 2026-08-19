-- Phase 68 -- run attribution and the token breakdown.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0034_run_attribution.sql.
--
-- 🚨 user_id is nvarchar(200), not nvarchar(max): the column is INDEXED below
-- and SQL Server cannot index nvarchar(max). RunLabels.MaxUserIdLength bounds
-- the value at exactly 200 so a legal identity can never be truncated here.
IF COL_LENGTH(N'{schema}.runs', N'user_id') IS NULL
ALTER TABLE {schema}.runs ADD user_id nvarchar(200) NULL;

-- SQL Server has no jsonb: the map is JSON TEXT and is expanded with OPENJSON
-- (K-182, the same treatment arrays get). No ISJSON constraint is added, for the
-- same reason run_events.payload carries none -- observability does not break
-- functionality, and a malformed map must not reject the run's own row.
IF COL_LENGTH(N'{schema}.runs', N'labels') IS NULL
ALTER TABLE {schema}.runs ADD labels nvarchar(max) NULL;

IF COL_LENGTH(N'{schema}.runs', N'cached_input_tokens') IS NULL
ALTER TABLE {schema}.runs ADD cached_input_tokens bigint NULL;

IF COL_LENGTH(N'{schema}.runs', N'reasoning_tokens') IS NULL
ALTER TABLE {schema}.runs ADD reasoning_tokens bigint NULL;

IF COL_LENGTH(N'{schema}.runs', N'audio_input_tokens') IS NULL
ALTER TABLE {schema}.runs ADD audio_input_tokens bigint NULL;

IF COL_LENGTH(N'{schema}.runs', N'audio_output_tokens') IS NULL
ALTER TABLE {schema}.runs ADD audio_output_tokens bigint NULL;

-- 🚨 decimal(20,10) is written EXPLICITLY, like every money column here: an
-- untyped decimal parameter is decimal(18,0) and the fractional part is cut
-- SILENTLY (SqlServerDialect.AddDecimal covers the parameter side).
IF COL_LENGTH(N'{schema}.runs', N'cached_input_cost') IS NULL
ALTER TABLE {schema}.runs ADD cached_input_cost decimal(20,10) NULL;

-- 🚨 Wrapped in EXEC: user_id is added above with ALTER TABLE in the SAME batch,
-- and SQL Server compiles the WHOLE batch before running it -- without EXEC this
-- gives "Invalid column name 'user_id'" (see 0009_error_classification.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_user_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
EXEC(N'CREATE INDEX runs_tenant_user_idx
    ON {schema}.runs (tenant_id, user_id, started_at DESC)
    WHERE user_id IS NOT NULL;');

-- No index over `labels`: SQL Server has no equivalent of a GIN index over JSON
-- text, and a computed-column index would have to name each key in advance --
-- which is exactly what a free label set does not allow. The label filter is a
-- scan over the already tenant-narrowed rows; RunLabels.MaxCount bounds the
-- expansion at eight rows per run.
