-- Phase 12 -- an agent calls an agent: the run tree.

ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS parent_run_id uuid;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS root_run_id   uuid;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS depth         smallint NOT NULL DEFAULT 0;

-- 🚨 NO FOREIGN KEY IS ADDED. parent_run_id points at the same table and would
-- create a delete order constraint: deleting the root of a tree would first
-- need all of its leaves deleted. The retention policy of phase 25 (bulk delete
-- of old runs) would suffer from that. An orphan parent_run_id shows in the UI
-- as "parent run not found"; it is not a data integrity problem.

-- To fetch child runs by parent id. Partial index: the overwhelming majority of
-- the records are roots and NULL rows must not take space in the index.
CREATE INDEX IF NOT EXISTS runs_parent_idx
    ON {schema}.runs (parent_run_id)
    WHERE parent_run_id IS NOT NULL;

-- To fetch a whole tree in a SINGLE query. root_run_id is denormalized; walking
-- over parent_run_id would need a recursive CTE and the tree view of the UI
-- would run it on every open. The extra cost is one uuid column.
CREATE INDEX IF NOT EXISTS runs_root_idx
    ON {schema}.runs (tenant_id, root_run_id, started_at)
    WHERE root_run_id IS NOT NULL;

-- The runs list shows ONLY root runs by default; this is the most frequently
-- executed query and it deserves its own partial index.
CREATE INDEX IF NOT EXISTS runs_roots_only_idx
    ON {schema}.runs (tenant_id, started_at DESC)
    WHERE parent_run_id IS NULL;
