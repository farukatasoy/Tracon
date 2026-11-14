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
--      are deleted. NO SPAN IS DELETED: the spans move before the headers go, so the delete cascade finds
--      no span under a header it removes.
--   2. Drops the unique constraint on (tenant_id, trace_id).
--   3. Adds a filtered unique index on (tenant_id, run_id) WHERE run_id IS NOT NULL. A UNIQUE constraint
--      would count two NULLs as equal in SQL Server, and the rows that were written with no run (the column
--      can be NULL) must stay as they are.
--
-- Rows that were already joined under one W3C id stay joined. The header is the last run's, its spans are the
-- spans of every run that shared the id, and the first run's trace still answers 404. Splitting them again
-- would mean walking each span tree from its root, and a wrong split loses the link of a span to its run;
-- no row is changed for it.
--
-- traces_run_idx stays: the delete cascade from runs looks a header up by run_id alone, and the new index
-- starts with tenant_id.
--
-- The whole file is one command in one transaction, under the command timeout. A database that never saw a shared
-- id or a retried run merges nothing and only swaps the index. Measured with 400 thousand runs (440 thousand headers,
-- 880 thousand spans) in a test container: about five seconds.
-- An old release keeps writing with the old key. Its upsert finds a header by the W3C id alone: when a run
-- shares the id with a stored trace it points that header at its own run, so the trace of the first run is
-- taken over (the defect this migration removes, for as long as an old node runs). Start the new nodes first and
-- stop the old ones right after.

IF OBJECT_ID(N'tempdb..#trace_rank') IS NOT NULL
DROP TABLE #trace_rank;

SELECT id,
       tenant_id,
       run_id,
       started_at,
       ended_at,
       ROW_NUMBER() OVER (
           PARTITION BY tenant_id, run_id
           ORDER BY CASE WHEN ended_at IS NULL THEN 1 ELSE 0 END, ended_at DESC, started_at DESC, id DESC) AS rn
  INTO #trace_rank
  FROM {schema}.traces
 WHERE run_id IS NOT NULL;

UPDATE s
   SET trace_id = k.id
  FROM {schema}.spans s
  JOIN #trace_rank d ON d.id = s.trace_id AND d.rn > 1
  JOIN #trace_rank k ON k.tenant_id = d.tenant_id AND k.run_id = d.run_id AND k.rn = 1;

UPDATE t
   SET started_at = g.started_at,
       ended_at   = g.ended_at
  FROM {schema}.traces t
  JOIN (SELECT k.id AS kept_id,
               MIN(r.started_at) AS started_at,
               MAX(r.ended_at) AS ended_at
          FROM #trace_rank k
          JOIN #trace_rank r ON r.tenant_id = k.tenant_id AND r.run_id = k.run_id
         WHERE k.rn = 1
         GROUP BY k.id
        HAVING COUNT(*) > 1) g ON g.kept_id = t.id;

DELETE FROM {schema}.traces
 WHERE id IN (SELECT id FROM #trace_rank WHERE rn > 1);

DROP TABLE #trace_rank;

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'traces_tenant_trace_uq' AND parent_object_id = OBJECT_ID(N'{schema}.traces'))
ALTER TABLE {schema}.traces DROP CONSTRAINT traces_tenant_trace_uq;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'traces_tenant_run_uq' AND object_id = OBJECT_ID(N'{schema}.traces'))
CREATE UNIQUE INDEX traces_tenant_run_uq ON {schema}.traces (tenant_id, run_id) WHERE run_id IS NOT NULL;
