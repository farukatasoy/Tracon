-- ---------------------------------------------------------------------------
-- 0041 — Job queue lanes (Phase 129)
--
-- Nine JobKind values share one pool and one MaxConcurrentJobs budget today;
-- a long AgentBatch run holds a slot a fast ApprovalResume is waiting for
-- (head-of-line blocking). `lane` is a plain text tag: a job carries one, a
-- worker subscribes to a subset (TraconSchedulingOptions.Lanes) and may
-- give a lane its own concurrency budget (MaxConcurrentJobsPerLane).
--
-- DEFAULT 'default' covers every row written before this column existed, and
-- is also JobLanes.Default: an upgraded database's jobs read back exactly as
-- they did before lanes existed (no worker configuration, K1).
--
-- jobs_claim_idx is REBUILT with `lane` leading: LeaseJob's WHERE narrows by
-- lane first, then by the status/scheduled_for pair the old index covered.
-- The old index is dropped and the new one created in the SAME transaction
-- (MigrationRunner runs each migration inside one) -- CREATE INDEX
-- CONCURRENTLY cannot run inside a transaction, so this briefly locks writes
-- to `jobs` for the duration of the rebuild, same as every other index change
-- in this migration set.
--
-- 🚨 The partial predicate also WIDENS from `status IN (0, 1)` to
-- `status IN (0, 1, 2)` -- a PRE-EXISTING gap, found while proving this index
-- is actually used. LeaseJob's WHERE clause is
-- `(status = 0 AND scheduled_for <= now()) OR (status IN (1, 2) AND
-- lease_until < now())` -- its second branch reaches status = 2 (Running,
-- reclaiming a job whose owning worker died mid-execution), which the OLD
-- `status IN (0, 1)` predicate never covered. Measured: with the old
-- predicate, EXPLAIN showed a full Seq Scan for this branch regardless of
-- table size, because Postgres cannot prove the OR is covered by an index
-- whose own WHERE clause excludes one of the OR's branches; the widened
-- predicate lets the planner rewrite it as a BitmapOr over two Bitmap Index
-- Scans on this same index.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.jobs
    ADD COLUMN IF NOT EXISTS lane text NOT NULL DEFAULT 'default';

ALTER TABLE {schema}.job_schedules
    ADD COLUMN IF NOT EXISTS lane text NOT NULL DEFAULT 'default';

DROP INDEX IF EXISTS {schema}.jobs_claim_idx;

CREATE INDEX IF NOT EXISTS jobs_claim_idx
    ON {schema}.jobs (lane, status, scheduled_for)
    WHERE status IN (0, 1, 2);
