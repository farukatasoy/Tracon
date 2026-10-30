-- Prompt-cache writes become the fourth term of a run's cost.
--
-- For the rationale and the column meanings see PostgreSQL 0066_cache_write_cost.sql.
--
-- 🚨 decimal(20,10) is written EXPLICITLY, like every money column here: an untyped
-- decimal parameter is decimal(18,0) and the fractional part is cut SILENTLY
-- (SqlServerDialect.AddDecimal covers the parameter side).
IF COL_LENGTH(N'{schema}.runs', N'cache_write_input_tokens') IS NULL
ALTER TABLE {schema}.runs ADD cache_write_input_tokens bigint NULL;

IF COL_LENGTH(N'{schema}.runs', N'cache_write_long_input_tokens') IS NULL
ALTER TABLE {schema}.runs ADD cache_write_long_input_tokens bigint NULL;

IF COL_LENGTH(N'{schema}.runs', N'cache_write_cost') IS NULL
ALTER TABLE {schema}.runs ADD cache_write_cost decimal(20,10) NULL;

IF COL_LENGTH(N'{schema}.runs', N'cache_write_price_per_mtok') IS NULL
ALTER TABLE {schema}.runs ADD cache_write_price_per_mtok decimal(20,10) NULL;

IF COL_LENGTH(N'{schema}.runs', N'cache_write_long_price_per_mtok') IS NULL
ALTER TABLE {schema}.runs ADD cache_write_long_price_per_mtok decimal(20,10) NULL;
