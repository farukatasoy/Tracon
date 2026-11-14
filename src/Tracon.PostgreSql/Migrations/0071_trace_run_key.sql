-- A trace belongs to one run: the header is unique on (tenant_id, run_id), not on (tenant_id, trace_id).
--
-- 🚨 The W3C trace id is the caller's to choose. A gateway that sends a traceparent header gives every run it
-- starts the same id, and so does a host that runs several runs under one Activity. The old key joined those
-- runs into one header: the header kept the run_id of the LAST writer, the first run's trace answered 404, the
-- second run's trace carried the spans of both, and deleting one run deleted or kept the other run's spans.
-- A run that was retried and written under two W3C ids had two headers, and the read picked one of them.
--
-- What this migration does, in this order:
--   1. Merges the headers that already share a (tenant_id, run_id). The header that ended last stays (a header
--      with no ended_at ranks last; a tie goes to the later started_at, then to the larger id). The spans of
--      the other headers move to it, its started_at and ended_at widen to cover them, and the other headers
--      are deleted. NO SPAN IS DELETED: the statement is one, so the delete cascade finds no span left under
--      a header it removes.
--   2. Drops the unique constraint on (tenant_id, trace_id).
--   3. Adds the unique index on (tenant_id, run_id). PostgreSQL treats NULLs as distinct, so the rows that
--      were written with no run (the column can be NULL) stay as they are.
--
-- Rows that were already joined under one W3C id stay joined. The header is the last run's, its spans are the
-- spans of every run that shared the id, and the first run's trace still answers 404. Splitting them again
-- would mean walking each span tree from its root, and a wrong split loses the link of a span to its run;
-- no row is changed for it.
--
-- traces_run_idx stays: the delete cascade from runs looks a header up by run_id alone, and the new index
-- starts with tenant_id.
--
-- The statement locks the rows it merges. A database that never saw a shared id or a retried run merges
-- nothing. The old release cannot write a trace into the new shape: its upsert names the old key, which no
-- longer exists, and the write fails. The run is not affected, the collector catches the failure; the traces
-- of the runs that end on an old node until it is stopped are lost. Start the new nodes first and stop the
-- old ones right after.

WITH ranked AS (
    SELECT id,
           tenant_id,
           run_id,
           started_at,
           ended_at,
           ROW_NUMBER() OVER (
               PARTITION BY tenant_id, run_id
               ORDER BY ended_at DESC NULLS LAST, started_at DESC, id DESC) AS rn
      FROM {schema}.traces
     WHERE run_id IS NOT NULL
),
duplicate AS (
    SELECT d.id AS duplicate_id,
           k.id AS kept_id
      FROM ranked d
      JOIN ranked k ON k.tenant_id = d.tenant_id AND k.run_id = d.run_id AND k.rn = 1
     WHERE d.rn > 1
),
extent AS (
    SELECT k.id AS kept_id,
           MIN(g.started_at) AS started_at,
           MAX(g.ended_at) AS ended_at
      FROM ranked k
      JOIN ranked g ON g.tenant_id = k.tenant_id AND g.run_id = k.run_id
     WHERE k.rn = 1
     GROUP BY k.id
    HAVING COUNT(*) > 1
),
moved AS (
    UPDATE {schema}.spans s
       SET trace_id = duplicate.kept_id
      FROM duplicate
     WHERE s.trace_id = duplicate.duplicate_id
    RETURNING s.id
),
widened AS (
    UPDATE {schema}.traces t
       SET started_at = extent.started_at,
           ended_at   = extent.ended_at
      FROM extent
     WHERE t.id = extent.kept_id
    RETURNING t.id
)
DELETE FROM {schema}.traces
 WHERE id IN (SELECT duplicate_id FROM duplicate);

ALTER TABLE {schema}.traces DROP CONSTRAINT IF EXISTS traces_tenant_trace_uq;

CREATE UNIQUE INDEX IF NOT EXISTS traces_tenant_run_uq
    ON {schema}.traces (tenant_id, run_id);
