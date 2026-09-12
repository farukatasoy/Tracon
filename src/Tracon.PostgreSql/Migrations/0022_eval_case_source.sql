-- ---------------------------------------------------------------------------
-- 0022 — Promotion of a production run to an eval case (phase 45, F-53)
--
-- The origin of promoting a production run (failed, scored negatively, or
-- successful as a reference) to an eval case with a single request.
--
-- source_run_id CARRIES NO foreign key: even if the source run is deleted by the
-- retention period the case must stay understandable (the same append-only
-- reason as eval_case_results in 0009_eval.sql, docs/45-URETIMDEN-EVAL-KUMESI.md 45.5).
--
-- The partial unique index stops the same run from being promoted to the same
-- suite twice; cases written by hand carry source_run_id = NULL and the
-- constraint does not affect them.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.eval_cases ADD COLUMN IF NOT EXISTS source_run_id uuid;
ALTER TABLE {schema}.eval_cases ADD COLUMN IF NOT EXISTS source_kind   smallint;
ALTER TABLE {schema}.eval_cases ADD COLUMN IF NOT EXISTS promoted_at   timestamptz;

CREATE UNIQUE INDEX IF NOT EXISTS eval_cases_source_run_uq
    ON {schema}.eval_cases (suite_id, source_run_id)
    WHERE source_run_id IS NOT NULL;
