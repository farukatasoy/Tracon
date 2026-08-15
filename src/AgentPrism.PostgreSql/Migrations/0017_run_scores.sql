-- Phase 31 -- human (or judge) score per run and per message.
--
-- The score IS NOT ADDED to the `runs` table as a column: a run can get more
-- than one score (different author/time), a score can also be given at message
-- level, and `runs` is the hot path written on every run -- scores arrive rarely
-- and follow a separate life cycle.
--
-- The `source` column is added today already: F-71 (online evaluation) will write
-- the judge score to the SAME table and it must be told apart from the human
-- score. Adding the column later means three more migrations; today it is free.
--
-- There is NO foreign key -- in this store no "event/summary" table
-- (run_events, tool_invocations, voice_sessions) carries an FK to `runs`.
--
-- Rationale: docs/31-GERI-BILDIRIM-VE-PUANLAMA.md, section 31.1.

CREATE TABLE IF NOT EXISTS {schema}.run_scores (
    id          uuid        NOT NULL PRIMARY KEY,
    tenant_id   text        NOT NULL,
    run_id      uuid        NOT NULL,

    -- If NULL the score belongs to the WHOLE run; if filled, to a single message.
    message_id  text,

    -- RunScoreKind. smallint: enum values are stable, a change in the JSON
    -- format does not affect the stored data.
    kind        smallint    NOT NULL,

    -- 0/1 for Binary, 1..5 for Stars.
    value       integer     NOT NULL,
    comment     text,

    -- human | api | judge. Today the only value is 'human'.
    source      text        NOT NULL,
    author      text,
    created_at  timestamptz NOT NULL
);

-- An author scores a target (a run or a message) once; a second write UPDATES it.
-- When message_id is NULL it counts as '' through COALESCE so that the run level
-- score follows the same rule (the same as the quota pattern of K-023).
--
-- 🚨 author IS DELIBERATELY NOT COALESCED: because in PostgreSQL no NULL is equal
-- to any NULL, when author is empty (an installation without identity) the
-- uniqueness constraint never takes effect and every call opens a new row. This
-- IS INTENDED (docs/31-GERI-BILDIRIM-VE-PUANLAMA.md, open question 4).
CREATE UNIQUE INDEX IF NOT EXISTS run_scores_target_author_idx
    ON {schema}.run_scores (tenant_id, run_id, COALESCE(message_id, ''), author);

-- Listing the scores of a run -- that is the only access pattern.
CREATE INDEX IF NOT EXISTS run_scores_run_idx
    ON {schema}.run_scores (tenant_id, run_id);
