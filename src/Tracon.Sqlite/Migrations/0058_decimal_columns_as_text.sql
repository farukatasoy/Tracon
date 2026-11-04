-- Seven columns of `runs` become TEXT, and the quota counters lose the float tail an older addition left on them.
--
-- `cached_input_cost`, `non_token_cost` and the five unit prices (`input_price_per_mtok`, `output_price_per_mtok`,
-- `cached_input_price_per_mtok`, `cache_write_price_per_mtok`, `cache_write_long_price_per_mtok`) were declared NUMERIC.
-- The driver writes a decimal as text, and the NUMERIC affinity turns text that looks like a number into an INTEGER or a
-- REAL: 999999999.999999 read back as 999999999.99999905, and the digits past the 15th were gone. Every other cost column of
-- these tables was TEXT from the start, as `numeric(20,10)` is on PostgreSQL and SQL Server. A TEXT column keeps the driver's
-- text as it is.
--
-- What the stored value becomes (typeof() of the old column):
--   NULL     stays NULL.
--   integer  CAST(x AS TEXT). Exact.
--   real     printf('%.15g', x): the 15 significant digits SQLite documents it keeps when it turns text into a real. A value
--            that was written with 15 digits or fewer (every real cost and price) comes back digit for digit. A value written
--            with more was rounded to a double when it was written; it ends here as its 15-digit decimal
--            (1234567890.1234567890 -> 1234567890.12346). The rule does not depend on the SQLite version, which
--            CAST(x AS TEXT) does (3.53 writes 999999999.99999905, older versions write other digits).
--   text     stays as it is (the driver never writes one of these).
-- printf('%g') writes an exponent for a value under 0.0001 (1.234e-05). The TEXT columns hold plain decimal text, and the
-- exact sum and the readers rely on it, so the second statement spells the exponent out (1.234e-05 -> 0.00001234).
--
-- No table is rebuilt: `runs` has children with ON DELETE CASCADE, and with foreign keys on, DROP TABLE would delete their
-- rows. A column is added, filled, the old one dropped and the new one renamed, as in 0035. The seven columns end up at the
-- end of the table; every query names its columns.
--
-- ALTER TABLE ... DROP COLUMN refuses a column that a view names, so the read view `runs_v1` is dropped first. The "views"
-- migration set (0003) builds it again in the same run when the set is enabled; a host that had it enabled and later switched
-- it off loses the view that was left behind. A view or trigger of your own over one of these columns stops this migration with
-- "error in view ... after drop column", and nothing is changed: drop it first and create it again afterwards.
--
-- `quota_usage.cost` is TEXT, but the old upsert added it with `cost + excluded.cost`, which SQLite does on doubles
-- (0.1 + 0.2 stored 0.30000000000000004; a tiny amount was stored as 3.0e-05). The counters are rounded to 15 significant
-- digits and the exponent is spelled out, the same rule as above. Only the running period of a counter is concerned. From now
-- on the upsert adds exactly (tracon_decimal_add).

ALTER TABLE {schema}runs ADD COLUMN cached_input_cost_text TEXT NULL;
ALTER TABLE {schema}runs ADD COLUMN non_token_cost_text TEXT NULL;
ALTER TABLE {schema}runs ADD COLUMN input_price_per_mtok_text TEXT NULL;
ALTER TABLE {schema}runs ADD COLUMN output_price_per_mtok_text TEXT NULL;
ALTER TABLE {schema}runs ADD COLUMN cached_input_price_per_mtok_text TEXT NULL;
ALTER TABLE {schema}runs ADD COLUMN cache_write_price_per_mtok_text TEXT NULL;
ALTER TABLE {schema}runs ADD COLUMN cache_write_long_price_per_mtok_text TEXT NULL;

UPDATE {schema}runs
   SET cached_input_cost_text = CASE typeof(cached_input_cost) WHEN 'real' THEN printf('%.15g', cached_input_cost) WHEN 'integer' THEN CAST(cached_input_cost AS TEXT) ELSE cached_input_cost END,
       non_token_cost_text = CASE typeof(non_token_cost) WHEN 'real' THEN printf('%.15g', non_token_cost) WHEN 'integer' THEN CAST(non_token_cost AS TEXT) ELSE non_token_cost END,
       input_price_per_mtok_text = CASE typeof(input_price_per_mtok) WHEN 'real' THEN printf('%.15g', input_price_per_mtok) WHEN 'integer' THEN CAST(input_price_per_mtok AS TEXT) ELSE input_price_per_mtok END,
       output_price_per_mtok_text = CASE typeof(output_price_per_mtok) WHEN 'real' THEN printf('%.15g', output_price_per_mtok) WHEN 'integer' THEN CAST(output_price_per_mtok AS TEXT) ELSE output_price_per_mtok END,
       cached_input_price_per_mtok_text = CASE typeof(cached_input_price_per_mtok) WHEN 'real' THEN printf('%.15g', cached_input_price_per_mtok) WHEN 'integer' THEN CAST(cached_input_price_per_mtok AS TEXT) ELSE cached_input_price_per_mtok END,
       cache_write_price_per_mtok_text = CASE typeof(cache_write_price_per_mtok) WHEN 'real' THEN printf('%.15g', cache_write_price_per_mtok) WHEN 'integer' THEN CAST(cache_write_price_per_mtok AS TEXT) ELSE cache_write_price_per_mtok END,
       cache_write_long_price_per_mtok_text = CASE typeof(cache_write_long_price_per_mtok) WHEN 'real' THEN printf('%.15g', cache_write_long_price_per_mtok) WHEN 'integer' THEN CAST(cache_write_long_price_per_mtok AS TEXT) ELSE cache_write_long_price_per_mtok END
 WHERE cached_input_cost IS NOT NULL
    OR non_token_cost IS NOT NULL
    OR input_price_per_mtok IS NOT NULL
    OR output_price_per_mtok IS NOT NULL
    OR cached_input_price_per_mtok IS NOT NULL
    OR cache_write_price_per_mtok IS NOT NULL
    OR cache_write_long_price_per_mtok IS NOT NULL;

UPDATE {schema}runs
   SET cached_input_cost_text = CASE WHEN instr(cached_input_cost_text, 'e') = 0 THEN cached_input_cost_text
                  ELSE CASE WHEN substr(cached_input_cost_text, 1, 1) = '-' THEN '-' ELSE '' END ||
                       CASE WHEN CAST(substr(cached_input_cost_text, instr(cached_input_cost_text, 'e') + 1) AS INTEGER) < 0
                            THEN '0.' || replace(hex(zeroblob(-CAST(substr(cached_input_cost_text, instr(cached_input_cost_text, 'e') + 1) AS INTEGER) - 1)), '00', '0') || replace(replace(substr(cached_input_cost_text, 1, instr(cached_input_cost_text, 'e') - 1), '-', ''), '.', '')
                            ELSE replace(replace(substr(cached_input_cost_text, 1, instr(cached_input_cost_text, 'e') - 1), '-', ''), '.', '') || replace(hex(zeroblob(CAST(substr(cached_input_cost_text, instr(cached_input_cost_text, 'e') + 1) AS INTEGER) + 1 - length(replace(replace(substr(cached_input_cost_text, 1, instr(cached_input_cost_text, 'e') - 1), '-', ''), '.', '')))), '00', '0')
                       END
             END,
       non_token_cost_text = CASE WHEN instr(non_token_cost_text, 'e') = 0 THEN non_token_cost_text
                  ELSE CASE WHEN substr(non_token_cost_text, 1, 1) = '-' THEN '-' ELSE '' END ||
                       CASE WHEN CAST(substr(non_token_cost_text, instr(non_token_cost_text, 'e') + 1) AS INTEGER) < 0
                            THEN '0.' || replace(hex(zeroblob(-CAST(substr(non_token_cost_text, instr(non_token_cost_text, 'e') + 1) AS INTEGER) - 1)), '00', '0') || replace(replace(substr(non_token_cost_text, 1, instr(non_token_cost_text, 'e') - 1), '-', ''), '.', '')
                            ELSE replace(replace(substr(non_token_cost_text, 1, instr(non_token_cost_text, 'e') - 1), '-', ''), '.', '') || replace(hex(zeroblob(CAST(substr(non_token_cost_text, instr(non_token_cost_text, 'e') + 1) AS INTEGER) + 1 - length(replace(replace(substr(non_token_cost_text, 1, instr(non_token_cost_text, 'e') - 1), '-', ''), '.', '')))), '00', '0')
                       END
             END,
       input_price_per_mtok_text = CASE WHEN instr(input_price_per_mtok_text, 'e') = 0 THEN input_price_per_mtok_text
                  ELSE CASE WHEN substr(input_price_per_mtok_text, 1, 1) = '-' THEN '-' ELSE '' END ||
                       CASE WHEN CAST(substr(input_price_per_mtok_text, instr(input_price_per_mtok_text, 'e') + 1) AS INTEGER) < 0
                            THEN '0.' || replace(hex(zeroblob(-CAST(substr(input_price_per_mtok_text, instr(input_price_per_mtok_text, 'e') + 1) AS INTEGER) - 1)), '00', '0') || replace(replace(substr(input_price_per_mtok_text, 1, instr(input_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')
                            ELSE replace(replace(substr(input_price_per_mtok_text, 1, instr(input_price_per_mtok_text, 'e') - 1), '-', ''), '.', '') || replace(hex(zeroblob(CAST(substr(input_price_per_mtok_text, instr(input_price_per_mtok_text, 'e') + 1) AS INTEGER) + 1 - length(replace(replace(substr(input_price_per_mtok_text, 1, instr(input_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')))), '00', '0')
                       END
             END,
       output_price_per_mtok_text = CASE WHEN instr(output_price_per_mtok_text, 'e') = 0 THEN output_price_per_mtok_text
                  ELSE CASE WHEN substr(output_price_per_mtok_text, 1, 1) = '-' THEN '-' ELSE '' END ||
                       CASE WHEN CAST(substr(output_price_per_mtok_text, instr(output_price_per_mtok_text, 'e') + 1) AS INTEGER) < 0
                            THEN '0.' || replace(hex(zeroblob(-CAST(substr(output_price_per_mtok_text, instr(output_price_per_mtok_text, 'e') + 1) AS INTEGER) - 1)), '00', '0') || replace(replace(substr(output_price_per_mtok_text, 1, instr(output_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')
                            ELSE replace(replace(substr(output_price_per_mtok_text, 1, instr(output_price_per_mtok_text, 'e') - 1), '-', ''), '.', '') || replace(hex(zeroblob(CAST(substr(output_price_per_mtok_text, instr(output_price_per_mtok_text, 'e') + 1) AS INTEGER) + 1 - length(replace(replace(substr(output_price_per_mtok_text, 1, instr(output_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')))), '00', '0')
                       END
             END,
       cached_input_price_per_mtok_text = CASE WHEN instr(cached_input_price_per_mtok_text, 'e') = 0 THEN cached_input_price_per_mtok_text
                  ELSE CASE WHEN substr(cached_input_price_per_mtok_text, 1, 1) = '-' THEN '-' ELSE '' END ||
                       CASE WHEN CAST(substr(cached_input_price_per_mtok_text, instr(cached_input_price_per_mtok_text, 'e') + 1) AS INTEGER) < 0
                            THEN '0.' || replace(hex(zeroblob(-CAST(substr(cached_input_price_per_mtok_text, instr(cached_input_price_per_mtok_text, 'e') + 1) AS INTEGER) - 1)), '00', '0') || replace(replace(substr(cached_input_price_per_mtok_text, 1, instr(cached_input_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')
                            ELSE replace(replace(substr(cached_input_price_per_mtok_text, 1, instr(cached_input_price_per_mtok_text, 'e') - 1), '-', ''), '.', '') || replace(hex(zeroblob(CAST(substr(cached_input_price_per_mtok_text, instr(cached_input_price_per_mtok_text, 'e') + 1) AS INTEGER) + 1 - length(replace(replace(substr(cached_input_price_per_mtok_text, 1, instr(cached_input_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')))), '00', '0')
                       END
             END,
       cache_write_price_per_mtok_text = CASE WHEN instr(cache_write_price_per_mtok_text, 'e') = 0 THEN cache_write_price_per_mtok_text
                  ELSE CASE WHEN substr(cache_write_price_per_mtok_text, 1, 1) = '-' THEN '-' ELSE '' END ||
                       CASE WHEN CAST(substr(cache_write_price_per_mtok_text, instr(cache_write_price_per_mtok_text, 'e') + 1) AS INTEGER) < 0
                            THEN '0.' || replace(hex(zeroblob(-CAST(substr(cache_write_price_per_mtok_text, instr(cache_write_price_per_mtok_text, 'e') + 1) AS INTEGER) - 1)), '00', '0') || replace(replace(substr(cache_write_price_per_mtok_text, 1, instr(cache_write_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')
                            ELSE replace(replace(substr(cache_write_price_per_mtok_text, 1, instr(cache_write_price_per_mtok_text, 'e') - 1), '-', ''), '.', '') || replace(hex(zeroblob(CAST(substr(cache_write_price_per_mtok_text, instr(cache_write_price_per_mtok_text, 'e') + 1) AS INTEGER) + 1 - length(replace(replace(substr(cache_write_price_per_mtok_text, 1, instr(cache_write_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')))), '00', '0')
                       END
             END,
       cache_write_long_price_per_mtok_text = CASE WHEN instr(cache_write_long_price_per_mtok_text, 'e') = 0 THEN cache_write_long_price_per_mtok_text
                  ELSE CASE WHEN substr(cache_write_long_price_per_mtok_text, 1, 1) = '-' THEN '-' ELSE '' END ||
                       CASE WHEN CAST(substr(cache_write_long_price_per_mtok_text, instr(cache_write_long_price_per_mtok_text, 'e') + 1) AS INTEGER) < 0
                            THEN '0.' || replace(hex(zeroblob(-CAST(substr(cache_write_long_price_per_mtok_text, instr(cache_write_long_price_per_mtok_text, 'e') + 1) AS INTEGER) - 1)), '00', '0') || replace(replace(substr(cache_write_long_price_per_mtok_text, 1, instr(cache_write_long_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')
                            ELSE replace(replace(substr(cache_write_long_price_per_mtok_text, 1, instr(cache_write_long_price_per_mtok_text, 'e') - 1), '-', ''), '.', '') || replace(hex(zeroblob(CAST(substr(cache_write_long_price_per_mtok_text, instr(cache_write_long_price_per_mtok_text, 'e') + 1) AS INTEGER) + 1 - length(replace(replace(substr(cache_write_long_price_per_mtok_text, 1, instr(cache_write_long_price_per_mtok_text, 'e') - 1), '-', ''), '.', '')))), '00', '0')
                       END
             END
 WHERE instr(cached_input_cost_text, 'e') > 0
    OR instr(non_token_cost_text, 'e') > 0
    OR instr(input_price_per_mtok_text, 'e') > 0
    OR instr(output_price_per_mtok_text, 'e') > 0
    OR instr(cached_input_price_per_mtok_text, 'e') > 0
    OR instr(cache_write_price_per_mtok_text, 'e') > 0
    OR instr(cache_write_long_price_per_mtok_text, 'e') > 0;

DROP VIEW IF EXISTS {schema}runs_v1;

ALTER TABLE {schema}runs DROP COLUMN cached_input_cost;
ALTER TABLE {schema}runs DROP COLUMN non_token_cost;
ALTER TABLE {schema}runs DROP COLUMN input_price_per_mtok;
ALTER TABLE {schema}runs DROP COLUMN output_price_per_mtok;
ALTER TABLE {schema}runs DROP COLUMN cached_input_price_per_mtok;
ALTER TABLE {schema}runs DROP COLUMN cache_write_price_per_mtok;
ALTER TABLE {schema}runs DROP COLUMN cache_write_long_price_per_mtok;

ALTER TABLE {schema}runs RENAME COLUMN cached_input_cost_text TO cached_input_cost;
ALTER TABLE {schema}runs RENAME COLUMN non_token_cost_text TO non_token_cost;
ALTER TABLE {schema}runs RENAME COLUMN input_price_per_mtok_text TO input_price_per_mtok;
ALTER TABLE {schema}runs RENAME COLUMN output_price_per_mtok_text TO output_price_per_mtok;
ALTER TABLE {schema}runs RENAME COLUMN cached_input_price_per_mtok_text TO cached_input_price_per_mtok;
ALTER TABLE {schema}runs RENAME COLUMN cache_write_price_per_mtok_text TO cache_write_price_per_mtok;
ALTER TABLE {schema}runs RENAME COLUMN cache_write_long_price_per_mtok_text TO cache_write_long_price_per_mtok;

UPDATE {schema}quota_usage
   SET cost = printf('%.15g', CAST(cost AS REAL))
 WHERE instr(cost, 'e') > 0 OR length(cost) > 16;

UPDATE {schema}quota_usage
   SET cost = CASE WHEN instr(cost, 'e') = 0 THEN cost
                  ELSE CASE WHEN substr(cost, 1, 1) = '-' THEN '-' ELSE '' END ||
                       CASE WHEN CAST(substr(cost, instr(cost, 'e') + 1) AS INTEGER) < 0
                            THEN '0.' || replace(hex(zeroblob(-CAST(substr(cost, instr(cost, 'e') + 1) AS INTEGER) - 1)), '00', '0') || replace(replace(substr(cost, 1, instr(cost, 'e') - 1), '-', ''), '.', '')
                            ELSE replace(replace(substr(cost, 1, instr(cost, 'e') - 1), '-', ''), '.', '') || replace(hex(zeroblob(CAST(substr(cost, instr(cost, 'e') + 1) AS INTEGER) + 1 - length(replace(replace(substr(cost, 1, instr(cost, 'e') - 1), '-', ''), '.', '')))), '00', '0')
                       END
             END
 WHERE instr(cost, 'e') > 0;
