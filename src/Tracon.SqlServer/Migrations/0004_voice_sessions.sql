-- Phase 29 -- summary record of real time speech connections.
--
-- For the rationale and the column meanings see PostgreSQL 0016_voice_sessions.sql.
--
-- 🚨 The scale is given EXPLICITLY on the decimal column. A decimal parameter
-- with no type counts as decimal(18,0) on SQL Server and the fraction is cut
-- SILENTLY; SqlServerDialect.AddDecimal writes Precision/Scale.

IF OBJECT_ID(N'{schema}.voice_sessions', N'U') IS NULL
CREATE TABLE {schema}.voice_sessions (
    id            uniqueidentifier NOT NULL PRIMARY KEY,
    tenant_id     nvarchar(200)    NOT NULL,
    session_id    nvarchar(200)    NOT NULL,
    agent_name    nvarchar(200)    NOT NULL,
    started_at    datetimeoffset   NOT NULL,
    ended_at      datetimeoffset   NULL,
    turns         int              NOT NULL CONSTRAINT DF_voice_sessions_turns DEFAULT 0,
    input_seconds decimal(12,3)    NULL,
    output_chars  bigint           NULL,
    end_reason    smallint         NULL,
    created_by    nvarchar(400)    NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'voice_sessions_tenant_started_idx' AND object_id = OBJECT_ID(N'{schema}.voice_sessions'))
CREATE INDEX voice_sessions_tenant_started_idx
    ON {schema}.voice_sessions (tenant_id, started_at DESC);
