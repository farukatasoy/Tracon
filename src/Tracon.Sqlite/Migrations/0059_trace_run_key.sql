-- A trace belongs to one run: the header is unique on (tenant_id, run_id), not on (tenant_id, trace_id).
--
-- 🚨 The W3C trace id is the caller's to choose. A gateway that sends a traceparent header gives every run it
-- starts the same id, and so does a host that runs several runs under one Activity. The old key joined those
-- runs into one header: the header kept the run_id of the LAST writer, the first run's trace answered 404, the
-- second run's trace carried the spans of both, and deleting one run deleted or kept the other run's spans.
-- A run that was retried and written under two W3C ids had two headers, and the read picked one of them.
--
-- SQLite cannot drop the UNIQUE (tenant_id, trace_id) of a table: the index of a table constraint cannot be
-- dropped, and neither can a column that a constraint names. The two tables are rebuilt.
--
-- 🚨 CHILD FIRST. `spans` is the child of `traces` with ON DELETE CASCADE, and with foreign keys on, DROP TABLE
-- deletes the rows of the table first, which cascades. PRAGMA foreign_keys = OFF does nothing inside the
-- transaction a migration runs in. So the rebuild never drops a table that still has a child holding rows:
--   1. traces_new is built and filled. The headers that share a (tenant_id, run_id) become one: the header that
--      ended last stays (a header with no ended_at ranks last, a tie goes to the later started_at, then to the
--      larger id) and widens to cover the others. NO SPAN IS DELETED.
--   2. spans_new is built and filled. Every span moves to the header its old one became. The mapping from an old
--      header to the one that stays is a query over the old table, written in both inserts, so no helper table
--      is left in the schema.
--   3. spans (the child) is dropped, then traces. Neither has a child with rows left to cascade into.
--   4. The two new tables take the old names. SQLite rewrites the REFERENCES clause of spans_new when
--      traces_new is renamed.
--   5. The two indexes are created again under their old names (an index name belongs to the whole database,
--      so the names carry the prefix, and the old ones went with their tables).
--
-- Every statement that creates a table starts with DROP ... IF EXISTS, so a run that stopped in the middle
-- leaves nothing behind and can be applied again.
--
-- Rows that were already joined under one W3C id stay joined. The header is the last run's, its spans are the
-- spans of every run that shared the id, and the first run's trace still answers 404. Splitting them again
-- would mean walking each span tree from its root, and a wrong split loses the link of a span to its run;
-- no row is changed for it.
--
-- The rows that were written with no run (the column can be NULL) stay as they are: SQLite treats NULLs as
-- distinct in a UNIQUE constraint, and each of them maps to itself.
--
-- Cost: the spans table is copied once. Measured with Microsoft.Data.Sqlite on a file with 550 thousand spans in 5 500
-- traces (171 MiB): about three seconds.
-- An index of your own on traces or spans goes with the table and is not created again; a view or trigger of your own that
-- names either table stops the migration with "error in view ..." (SQLite parses every view when a table is renamed), and
-- nothing is changed when it stops: drop it first and create it again afterwards.
-- The old release cannot write a trace into the new shape: its upsert names the old key, which no longer
-- exists, and the write fails. The run is not affected, the collector catches the failure; the traces of the
-- runs that end on an old node until it is stopped are lost.

DROP TABLE IF EXISTS {schema}spans_new;
DROP TABLE IF EXISTS {schema}traces_new;

CREATE TABLE {schema}traces_new (
    id         TEXT NOT NULL PRIMARY KEY,
    tenant_id  TEXT NOT NULL,
    trace_id   TEXT NOT NULL,
    run_id     TEXT NULL REFERENCES {schema}runs (id) ON DELETE CASCADE,
    started_at TEXT NOT NULL,
    ended_at   TEXT NULL,
    UNIQUE (tenant_id, run_id)
);

WITH mapped AS (
    SELECT id AS old_id,
           FIRST_VALUE(id) OVER (
               PARTITION BY tenant_id, run_id
               ORDER BY CASE WHEN ended_at IS NULL THEN 1 ELSE 0 END, ended_at DESC, started_at DESC, id DESC) AS new_id
      FROM {schema}traces
     WHERE run_id IS NOT NULL
    UNION ALL
    SELECT id, id
      FROM {schema}traces
     WHERE run_id IS NULL
)
INSERT INTO {schema}traces_new (id, tenant_id, trace_id, run_id, started_at, ended_at)
SELECT t.id, t.tenant_id, t.trace_id, t.run_id, g.started_at, g.ended_at
  FROM {schema}traces t
  JOIN (SELECT m.new_id AS kept_id,
               MIN(h.started_at) AS started_at,
               MAX(h.ended_at) AS ended_at
          FROM mapped m
          JOIN {schema}traces h ON h.id = m.old_id
         GROUP BY m.new_id) g ON g.kept_id = t.id;

CREATE TABLE {schema}spans_new (
    id             TEXT    NOT NULL PRIMARY KEY,
    trace_id       TEXT    NOT NULL REFERENCES {schema}traces_new (id) ON DELETE CASCADE,
    parent_span_id TEXT    NULL,
    name           TEXT    NOT NULL,
    kind           INTEGER NOT NULL,
    started_at     TEXT    NOT NULL,
    ended_at       TEXT    NULL,
    attributes     TEXT    NULL CHECK (attributes IS NULL OR json_valid(attributes)),
    status         INTEGER NULL,
    span_id        TEXT    NULL
);

WITH mapped AS (
    SELECT id AS old_id,
           FIRST_VALUE(id) OVER (
               PARTITION BY tenant_id, run_id
               ORDER BY CASE WHEN ended_at IS NULL THEN 1 ELSE 0 END, ended_at DESC, started_at DESC, id DESC) AS new_id
      FROM {schema}traces
     WHERE run_id IS NOT NULL
    UNION ALL
    SELECT id, id
      FROM {schema}traces
     WHERE run_id IS NULL
)
INSERT INTO {schema}spans_new (id, trace_id, parent_span_id, name, kind, started_at, ended_at, attributes, status, span_id)
SELECT s.id, m.new_id, s.parent_span_id, s.name, s.kind, s.started_at, s.ended_at, s.attributes, s.status, s.span_id
  FROM {schema}spans s
  JOIN mapped m ON m.old_id = s.trace_id;

DROP TABLE {schema}spans;
DROP TABLE {schema}traces;

ALTER TABLE {schema}traces_new RENAME TO {schema}traces;
ALTER TABLE {schema}spans_new RENAME TO {schema}spans;

CREATE INDEX IF NOT EXISTS {schema}traces_run_idx ON {schema}traces (run_id) WHERE run_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS {schema}spans_trace_started_idx ON {schema}spans (trace_id, started_at);

