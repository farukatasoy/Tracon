-- Faz 29 -- gercek zamanli konusma baglantilarinin ozet kaydi.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0016_voice_sessions.sql'e bakin.
--
-- 🚨 decimal sutununda olcek ACIKCA verilir. Tipi verilmemis bir decimal
-- parametresi SQL Server'da decimal(18,0) sayilir ve ondalik kisim SESSIZCE
-- kesilir; SqlServerDialect.AddDecimal Precision/Scale yazar.

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
