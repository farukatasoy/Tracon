-- Read contract views (Phase 111). Opt-in via TraconPostgreSqlOptions.EnableReadViews.
--
-- Rules (docs/111-OKUMA-SOZLESMESI-GORUNUMLERI.md, 111.1):
--   * A published view never loses a column, renames a column, or narrows a
--     column's type. Adding a column is free. A breaking change ships as a
--     NEW view (runs_v2), the old one keeps working for at least one major
--     version.
--   * Metadata and derived values only. No ProtectedColumn ever enters a view.
--   * NOT a tenant boundary: tenant_id travels unfiltered. Filtering is the
--     consumer's own query's job (see docs-site/.../reference/read-views.md).
--   * Read-only. Writing goes through the store interfaces, never the view.
--
-- ReadViewColumnSetTests and ReadViewCostTermTests (Tracon.Sql.Shared.UnitTests)
-- read this file's TEXT (no database) and enforce the markers below.

CREATE OR REPLACE VIEW {schema}.runs_v1 AS
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
    cached_input_cost,
    -- total_cost: BEGIN (kept in sync with SqlQueriesBase.CostAddends -- K-483:
    -- NULL only when ALL addends are NULL, otherwise the non-null ones sum).
    -- A fourth *_cost column added to `runs` without being added HERE is
    -- caught by ReadViewCostTermTests at build time, not in production.
    CASE
        WHEN input_cost IS NULL AND output_cost IS NULL AND cached_input_cost IS NULL THEN NULL
        ELSE COALESCE(input_cost, 0) + COALESCE(output_cost, 0) + COALESCE(cached_input_cost, 0)
    END                   AS total_cost,
    -- total_cost: END
    cost_currency,
    error_type,
    model_provider,
    input_price_per_mtok,
    output_price_per_mtok,
    cached_input_price_per_mtok
    -- runs_v1: columns END
FROM {schema}.runs;
