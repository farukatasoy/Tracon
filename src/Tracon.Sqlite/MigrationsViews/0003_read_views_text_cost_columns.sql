-- Read contract views, third definition. Opt-in via
-- TraconSqliteOptions.EnableReadViews.
--
-- The core migration 0058_decimal_columns_as_text.sql gave `cached_input_cost` and the five
-- unit prices of `runs` the TEXT type (they were NUMERIC) and, because SQLite will not drop a
-- column a view names, dropped `runs_v1`. This file builds it again.
--
-- The view's columns are published: `cached_input_cost` and the five prices were numbers in
-- `runs_v1`, and a number stays a number. Each is CAST to REAL here, the way `total_cost`
-- already casts every term. The same columns in `runs` are exact decimal text now; the view
-- is the float convenience for a query tool, and the exact amounts are `IRunStore` and the
-- table's own columns. `total_cost` is unchanged: the REAL sum of the four terms.
--
-- The earlier definitions (0001, 0002) are applied migrations and stay as they were. This one is the
-- whole definition of the view.
--
-- Rules:
--   * A published view never loses a column, renames a column, or narrows a
--     column's type. Adding a column is free. A breaking change ships as a
--     NEW view (runs_v2), the old one keeps working for at least one major
--     version.
--   * Metadata and derived values only. No ProtectedColumn ever enters a view.
--   * NOT a tenant boundary: tenant_id travels unfiltered. Filtering is the
--     consumer's own query's job (see docs-site/.../reference/read-views.md).
--   * Read-only. Writing goes through the store interfaces, never the view.
--
-- SQLite has no schema concept; {schema} is the table prefix, concatenated
-- directly -- "{schema}runs_v1", not "{schema}.runs_v1".
--
-- SQLite has no CREATE OR REPLACE VIEW; DROP + CREATE is the idempotent form.
-- No transaction wraps this pair by itself, but MigrationRunner applies the
-- whole migration file inside its own retry/lock discipline, same as every
-- other migration.
--
-- ReadViewColumnSetTests and ReadViewCostTermTests (Tracon.Sql.Shared.UnitTests)
-- read this file's TEXT (no database) and enforce the markers below.

DROP VIEW IF EXISTS {schema}runs_v1;

CREATE VIEW {schema}runs_v1 AS
SELECT
    -- runs_v1: columns BEGIN
    id                    AS run_id,
    tenant_id,
    agent_name,
    session_id,
    status,
    -- status_name: BEGIN (mirrors RunStatus, Tracon.Abstractions/Runs/RunStatus.cs.
    -- The numeric values are stable -- 0001_initial.sql header comment -- so this
    -- hand-written mapping is safe; a new status is APPENDED there and must be
    -- appended here too. ReadViewStatusNameTests checks every RunStatus name
    -- appears in this block, at build time, without a database.)
    CASE status
        WHEN 0 THEN 'Running'
        WHEN 1 THEN 'Completed'
        WHEN 2 THEN 'Failed'
        WHEN 3 THEN 'Canceled'
        WHEN 4 THEN 'AwaitingInput'
        WHEN 5 THEN 'Queued'
        WHEN 6 THEN 'AwaitingApproval'
    END                   AS status_name,
    -- status_name: END
    started_at,
    completed_at,
    is_streaming,
    input_tokens,
    output_tokens,
    cached_input_tokens,
    reasoning_tokens,
    total_tokens,
    input_cost,
    output_cost,
    CAST(cached_input_cost AS REAL) AS cached_input_cost,
    -- total_cost: BEGIN (kept in sync with SqlQueriesBase.CostAddends:
    -- NULL only when ALL addends are NULL, otherwise the non-null ones sum).
    -- A fifth *_cost column added to `runs` without being added HERE is
    -- caught by ReadViewCostTermTests at build time, not in production.
    -- 🚨 All four terms are TEXT in `runs`: an explicit CAST to REAL on every
    -- addend is required, or the sum silently reads a non-numeric term as 0
    -- instead of failing loudly.
    CASE
        WHEN input_cost IS NULL AND output_cost IS NULL AND cached_input_cost IS NULL AND cache_write_cost IS NULL THEN NULL
        ELSE COALESCE(CAST(input_cost AS REAL), 0)
             + COALESCE(CAST(output_cost AS REAL), 0)
             + COALESCE(CAST(cached_input_cost AS REAL), 0)
             + COALESCE(CAST(cache_write_cost AS REAL), 0)
    END                   AS total_cost,
    -- total_cost: END
    cost_currency,
    error_type,
    model_provider,
    CAST(input_price_per_mtok AS REAL)         AS input_price_per_mtok,
    CAST(output_price_per_mtok AS REAL)        AS output_price_per_mtok,
    CAST(cached_input_price_per_mtok AS REAL)  AS cached_input_price_per_mtok,
    cache_write_input_tokens,
    cache_write_long_input_tokens,
    cache_write_cost,
    CAST(cache_write_price_per_mtok AS REAL)       AS cache_write_price_per_mtok,
    CAST(cache_write_long_price_per_mtok AS REAL)  AS cache_write_long_price_per_mtok
    -- runs_v1: columns END
FROM {schema}runs;
