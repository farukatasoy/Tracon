-- Phase 47 -- replay and conversation branching.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0023_replay_and_branching.sql.
--
-- 🚨 Table and index names carry the PREFIX (K-193): SQLite has no schema and
-- index names share a single database wide namespace.
--
-- 🚨 There is no `IF NOT EXISTS` for `ALTER TABLE ... ADD COLUMN`; safety comes
-- from the migration runner (the same file does not run a second time).

CREATE TABLE IF NOT EXISTS {schema}run_inputs (
    run_id     TEXT NOT NULL PRIMARY KEY
               REFERENCES {schema}runs (id) ON DELETE CASCADE,
    tenant_id  TEXT NOT NULL,
    messages   TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}run_inputs_tenant_created_idx
    ON {schema}run_inputs (tenant_id, created_at DESC);

-- Replay lineage. The column is added AT THE END; the reader uses fixed column
-- indexes.
ALTER TABLE {schema}runs ADD COLUMN replay_of_run_id TEXT NULL;

CREATE INDEX IF NOT EXISTS {schema}runs_replay_of_idx
    ON {schema}runs (tenant_id, replay_of_run_id)
    WHERE replay_of_run_id IS NOT NULL;

-- Conversation branch pointer. There is NO foreign key; rationale in PostgreSQL 0023.
ALTER TABLE {schema}conversations ADD COLUMN parent_conversation_id TEXT NULL;
ALTER TABLE {schema}conversations ADD COLUMN branch_from_seq INTEGER NULL;

CREATE INDEX IF NOT EXISTS {schema}conversations_parent_idx
    ON {schema}conversations (parent_conversation_id)
    WHERE parent_conversation_id IS NOT NULL;
