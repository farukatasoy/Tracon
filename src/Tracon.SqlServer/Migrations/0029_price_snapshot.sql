-- Phase 132 -- the price snapshot gains the unit prices it applied, and the
-- run gains the provider that actually answered.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0042_price_snapshot.sql.
--
-- 🚨 nvarchar(200), like model_id: the same width, no index needed here.
IF COL_LENGTH(N'{schema}.runs', N'model_provider') IS NULL
ALTER TABLE {schema}.runs ADD model_provider nvarchar(200) NULL;

-- 🚨 decimal(20,10) is written EXPLICITLY, like every money column here: an
-- untyped decimal parameter is decimal(18,0) and the fractional part is cut
-- SILENTLY (SqlServerDialect.AddDecimal covers the parameter side).
IF COL_LENGTH(N'{schema}.runs', N'input_price_per_mtok') IS NULL
ALTER TABLE {schema}.runs ADD input_price_per_mtok decimal(20,10) NULL;

IF COL_LENGTH(N'{schema}.runs', N'output_price_per_mtok') IS NULL
ALTER TABLE {schema}.runs ADD output_price_per_mtok decimal(20,10) NULL;

IF COL_LENGTH(N'{schema}.runs', N'cached_input_price_per_mtok') IS NULL
ALTER TABLE {schema}.runs ADD cached_input_price_per_mtok decimal(20,10) NULL;
