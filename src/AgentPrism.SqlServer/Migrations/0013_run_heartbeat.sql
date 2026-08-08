-- Faz 54 -- oksuz calistirma uzlastirmasi.
--
-- Gerekce ve sutun anlami icin PostgreSQL 0026_run_heartbeat.sql'e bakin.

IF COL_LENGTH(N'{schema}.runs', N'heartbeat_at') IS NULL
ALTER TABLE {schema}.runs ADD heartbeat_at datetimeoffset NULL;

-- 🚨 EXEC ile sarilir: heartbeat_at yukarida AYNI toplu islemde ALTER TABLE
-- ile eklenir; SQL Server toplu islemi calistirmadan once TAMAMINI derler ve
-- EXEC olmadan "Invalid column name 'heartbeat_at'" verir (bkz. 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_running_heartbeat_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
EXEC(N'CREATE INDEX runs_running_heartbeat_idx
    ON {schema}.runs (heartbeat_at)
    WHERE status = 0;');
