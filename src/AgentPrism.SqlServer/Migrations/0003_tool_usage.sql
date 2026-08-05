-- Faz 28 -- tool cagrisi basina token DISI olcum ve maliyet.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0015_tool_usage.sql'e bakin.
--
-- 🚨 decimal sutunlarinda olcek ACIKCA verilir. Tipi verilmemis bir decimal
-- parametresi SQL Server'da decimal(18,0) sayilir ve ondalik kisim SESSIZCE
-- kesilir; butun para/olcum sutunlari bu yuzden decimal(20,10)'dur ve
-- SqlServerDialect.AddDecimal Precision/Scale yazar.

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

-- Filtreli indeks: olcumu olmayan satirlar (cogunluk) yer kaplamaz.
-- nvarchar(64) indekslenebilir; nvarchar(max) olsaydi indekslenemezdi.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'tool_invocations_usage_idx' AND object_id = OBJECT_ID(N'{schema}.tool_invocations'))
CREATE INDEX tool_invocations_usage_idx
    ON {schema}.tool_invocations (usage_unit, created_at DESC)
    WHERE usage_unit IS NOT NULL;
