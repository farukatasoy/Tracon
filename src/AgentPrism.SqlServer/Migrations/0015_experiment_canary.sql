-- Faz 56 -- kanarya yayini ve otomatik geri alma.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0028_experiment_canary.sql'e
-- bakin. canary_policy nvarchar(max) tasir (K-182 desenindeki gibi ISJSON
-- KISITI KONMAZ -- audit_log.before/after ile ayni gerekce: yalniz uygulama
-- yazar, dogrulama gerekmez).

IF COL_LENGTH(N'{schema}.experiments', N'canary_policy') IS NULL
ALTER TABLE {schema}.experiments ADD canary_policy nvarchar(max) NULL;

IF COL_LENGTH(N'{schema}.experiments', N'rollback_reason') IS NULL
ALTER TABLE {schema}.experiments ADD rollback_reason nvarchar(max) NULL;

-- 🚨 EXEC ile sarilir: canary_policy yukarida AYNI toplu islemde ALTER TABLE
-- ile eklenir; SQL Server toplu islemi calistirmadan once TAMAMINI derler ve
-- EXEC olmadan "Invalid column name 'canary_policy'" verir (bkz. 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'experiments_running_canary_idx' AND object_id = OBJECT_ID(N'{schema}.experiments'))
EXEC(N'CREATE INDEX experiments_running_canary_idx
    ON {schema}.experiments (status)
    WHERE status = 1 AND canary_policy IS NOT NULL;');
