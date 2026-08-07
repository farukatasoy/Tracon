-- Faz 44 -- hata siniflandirmasi ve ariza kumeleme.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0021_error_classification.sql'e
-- bakin. error_fingerprint SHA-256 ozetinin onaltilik gosterimidir (64 karakter
-- sabit uzunluk); nvarchar(64) bunu tam karsilar.

IF COL_LENGTH(N'{schema}.runs', N'error_class') IS NULL
ALTER TABLE {schema}.runs ADD error_class smallint NULL;

IF COL_LENGTH(N'{schema}.runs', N'error_fingerprint') IS NULL
ALTER TABLE {schema}.runs ADD error_fingerprint nvarchar(64) NULL;

-- Kismi (filtreli) indeks: error_class NULL olan (cogunluk) satirlar
-- indekste yer kaplamaz. Sozdizimi PostgreSQL ile aynidir (K-178 devir notu).
--
-- 🚨 EXEC ile sarilir: error_class yukarida AYNI toplu islemde ALTER TABLE
-- ile eklenir; SQL Server toplu islemi calistirmadan once TAMAMINI derler ve
-- EXEC olmadan "Invalid column name 'error_class'" verir (bkz. 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_error_class_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
EXEC(N'CREATE INDEX runs_error_class_idx
    ON {schema}.runs (tenant_id, error_class, started_at DESC)
    WHERE error_class IS NOT NULL;');
