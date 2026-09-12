-- ---------------------------------------------------------------------------
-- 0037 — Interrupted-run continuation (phase 87)
--
-- runs.continued_from_run_id — lineage. Shows which interrupted run this one
-- continues. Same pattern as replay_of_run_id (0023): the column is added AT
-- THE END, no foreign key (the source run can be deleted by the retention
-- policy without affecting the continuation).
--
-- Rationale: docs/87-KESILEN-ISIN-DEVAMI.md
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.runs
    ADD COLUMN IF NOT EXISTS continued_from_run_id uuid;

-- For the "the continuations of this run" query, and for the reconciliation
-- trigger's chain-length walk. Partial index: the great majority of rows are
-- NULL and never enter the index.
CREATE INDEX IF NOT EXISTS runs_continued_from_idx
    ON {schema}.runs (tenant_id, continued_from_run_id)
    WHERE continued_from_run_id IS NOT NULL;
