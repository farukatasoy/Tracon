-- ---------------------------------------------------------------------------
-- 0023 — Replay and conversation branching (phase 47)
--
-- Three changes:
--   1. run_inputs        — the INPUT of a run. Until now it was not stored
--                          anywhere; it is the source of replay.
--   2. runs.replay_of_run_id — lineage. It shows the source of a replay.
--   3. conversations.parent_conversation_id / branch_from_seq — branch pointer.
--
-- Rationale: docs/47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md
-- ---------------------------------------------------------------------------

-- ---------------------------------------------------------------------------
-- Run inputs
-- ---------------------------------------------------------------------------
-- 🚨 `messages` is deliberately `json`, NOT `jsonb`. ChatMessage contents are
-- polymorphic and the `$type` discriminator of System.Text.Json must be the FIRST
-- property of the object; `jsonb` REORDERS keys first by length then by byte
-- order and the read fails with JsonException. The same reason holds for
-- sessions.state, conversation_items.item and workflow_checkpoints.state.
-- Decision K-027 — this is the FOURTH application of that decision.
--
-- The input is in a separate table, it IS NOT ADDED to `runs` as a column: `runs`
-- is the hottest table and every list/statistics query reads it. For the same
-- reason conversation_items was also put in a separate table (0001).
--
-- ON DELETE CASCADE: an input does not outlive its run.

CREATE TABLE IF NOT EXISTS {schema}.run_inputs (
    run_id     uuid        NOT NULL PRIMARY KEY
               REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    tenant_id  text        NOT NULL,
    messages   json        NOT NULL,
    created_at timestamptz NOT NULL
);

-- The retention policy (RetentionTargets.RunInputs) scans by tenant + time.
CREATE INDEX IF NOT EXISTS run_inputs_tenant_created_idx
    ON {schema}.run_inputs (tenant_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- Replay lineage
-- ---------------------------------------------------------------------------
-- The column is added AT THE END: PostgresRunStore.ReadRun reads with fixed column
-- indexes and a column inserted in between would shift them all (docs/hafiza/postgresql.md).
--
-- There is NO foreign key: the source run can be deleted by the retention policy
-- and the replay itself must not be affected by that. The same reason holds for
-- run_scores.

ALTER TABLE {schema}.runs
    ADD COLUMN IF NOT EXISTS replay_of_run_id uuid;

-- For the "the repeats of this run" query. Partial index: the great majority of
-- the rows are NULL and never enter the index.
CREATE INDEX IF NOT EXISTS runs_replay_of_idx
    ON {schema}.runs (tenant_id, replay_of_run_id)
    WHERE replay_of_run_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Conversation branching
-- ---------------------------------------------------------------------------
-- A branch COPIES the items; these two columns are only ORIGIN information. If a
-- pointer chain had been chosen, every history read would be recursive and
-- SqlChatHistoryProvider (the hottest read path, run on every agent turn) would
-- turn into a recursive CTE.
--
-- 🚨 THERE IS NO FOREIGN KEY and that is deliberate. The plan expected
-- `ON DELETE SET NULL`; measured: SQL Server does not accept SET NULL on a SELF
-- REFERENCING foreign key (error 1785, "may cause cycles or multiple cascade
-- paths"). If the constraint were added only to PostgreSQL and SQLite the same
-- delete would give THREE DIFFERENT results on three providers -- the only thing
-- this repo never accepts (K-184: a schema difference must not become a behaviour difference).
--
-- The result is the same on every provider: if the main conversation is deleted
-- THE BRANCH KEEPS LIVING; the pointer only shows an origin that can no longer be
-- resolved. CASCADE was already rejected: it would tie a branch to the retention
-- policy of the main conversation and silently delete a branch the user saved.
--
-- The pointer is never JOINed on any read path; the copy design (47.4) needs no
-- backward resolution.

ALTER TABLE {schema}.conversations
    ADD COLUMN IF NOT EXISTS parent_conversation_id uuid;

ALTER TABLE {schema}.conversations
    ADD COLUMN IF NOT EXISTS branch_from_seq bigint;

CREATE INDEX IF NOT EXISTS conversations_parent_idx
    ON {schema}.conversations (parent_conversation_id)
    WHERE parent_conversation_id IS NOT NULL;
