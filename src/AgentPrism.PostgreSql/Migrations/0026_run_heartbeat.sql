-- Phase 54 -- orphaned run reconciliation.
--
-- heartbeat_at can stay NULL: old rows (opened before this migration) and new
-- rows where the heartbeat writer has not run its first turn yet have no value
-- at all. The reconciler then falls back to started_at (COALESCE), so a run that
-- crashed before any heartbeat is also caught when the threshold passes.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS heartbeat_at timestamptz;

-- Partial index: only Running rows (a small minority) pay the scan cost;
-- Completed/Failed/Canceled/AwaitingInput/Queued rows take no space in the
-- index. Reconciliation is OFF by default (K1); the index is still always
-- created -- once switched on it works at once and waits for no migration.
CREATE INDEX IF NOT EXISTS runs_running_heartbeat_idx
    ON {schema}.runs (heartbeat_at)
    WHERE status = 0;
