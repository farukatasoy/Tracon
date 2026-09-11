-- Phase 161 -- the provider, the model and what a voice session cost.
--
-- For the rationale and the column meanings see PostgreSQL 0050_voice_session_cost.sql.
--
-- 🚨 The scale is given EXPLICITLY on every decimal column. A decimal parameter
-- with no type counts as decimal(18,0) on SQL Server and the fraction is cut
-- SILENTLY; SqlServerDialect.AddDecimal writes Precision/Scale.

IF COL_LENGTH(N'{schema}.voice_sessions', N'provider') IS NULL
ALTER TABLE {schema}.voice_sessions ADD provider nvarchar(200) NULL;

IF COL_LENGTH(N'{schema}.voice_sessions', N'model') IS NULL
ALTER TABLE {schema}.voice_sessions ADD model nvarchar(200) NULL;

IF COL_LENGTH(N'{schema}.voice_sessions', N'live_seconds') IS NULL
ALTER TABLE {schema}.voice_sessions ADD live_seconds decimal(12,3) NULL;

IF COL_LENGTH(N'{schema}.voice_sessions', N'duration_cost') IS NULL
ALTER TABLE {schema}.voice_sessions ADD duration_cost decimal(18,8) NULL;

IF COL_LENGTH(N'{schema}.voice_sessions', N'character_cost') IS NULL
ALTER TABLE {schema}.voice_sessions ADD character_cost decimal(18,8) NULL;

IF COL_LENGTH(N'{schema}.voice_sessions', N'currency') IS NULL
ALTER TABLE {schema}.voice_sessions ADD currency nvarchar(10) NULL;
