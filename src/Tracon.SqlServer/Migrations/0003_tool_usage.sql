-- Phase 28 -- NON token usage and cost per tool call.
--
-- For the rationale and the column meanings see PostgreSQL 0015_tool_usage.sql.
--
-- 🚨 The scale is given EXPLICITLY on decimal columns. A decimal parameter with
-- no type counts as decimal(18,0) on SQL Server and the fraction is cut
-- SILENTLY; all money/usage columns are therefore decimal(20,10) and
-- SqlServerDialect.AddDecimal writes Precision/Scale.

IF COL_LENGTH(N'{schema}.tool_invocations', N'usage_unit') IS NULL
ALTER TABLE {schema}.tool_invocations ADD usage_unit nvarchar(64) NULL;

IF COL_LENGTH(N'{schema}.tool_invocations', N'usage_quantity') IS NULL
ALTER TABLE {schema}.tool_invocations ADD usage_quantity decimal(20,10) NULL;

IF COL_LENGTH(N'{schema}.tool_invocations', N'usage_estimated') IS NULL
ALTER TABLE {schema}.tool_invocations ADD usage_estimated bit NULL;

IF COL_LENGTH(N'{schema}.tool_invocations', N'cost') IS NULL
ALTER TABLE {schema}.tool_invocations ADD cost decimal(20,10) NULL;

IF COL_LENGTH(N'{schema}.tool_invocations', N'cost_currency') IS NULL
ALTER TABLE {schema}.tool_invocations ADD cost_currency nvarchar(16) NULL;

-- Filtered index: rows without usage (the majority) take no space.
-- nvarchar(64) can be indexed; nvarchar(max) could not.
--
-- 🚨 Wrapped in EXEC: the whole migration file is sent as ONE batch
-- (MigrationRunner does not split on GO). The usage_unit column ADDED a little
-- above in the same batch does not exist yet at compile time -- SQL Server
-- compiles the WHOLE batch before running it and CREATE INDEX is rejected with
-- "Invalid column name 'usage_unit'". EXEC(N'...') defers the compile to run
-- time; by then the ALTER TABLE is already applied.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'tool_invocations_usage_idx' AND object_id = OBJECT_ID(N'{schema}.tool_invocations'))
EXEC(N'CREATE INDEX tool_invocations_usage_idx
    ON {schema}.tool_invocations (usage_unit, created_at DESC)
    WHERE usage_unit IS NOT NULL;');
