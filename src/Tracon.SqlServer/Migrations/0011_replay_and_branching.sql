-- Phase 47 -- replay and conversation branching.
--
-- For the rationale and the column meanings see PostgreSQL
-- 0023_replay_and_branching.sql.
--
-- 🚨 The `messages` column is nvarchar(max) and CARRIES NO ISJSON constraint: the
-- polymorphic payload is stored as `json` (NOT jsonb) in PostgreSQL too and the
-- key order must be kept. For behaviour equality no validation is done here either.

IF OBJECT_ID(N'{schema}.run_inputs', N'U') IS NULL
CREATE TABLE {schema}.run_inputs (
    run_id     uniqueidentifier NOT NULL,
    tenant_id  nvarchar(200)    NOT NULL,
    messages   nvarchar(max)    NOT NULL,
    created_at datetimeoffset   NOT NULL,
    CONSTRAINT run_inputs_pk PRIMARY KEY (run_id),
    CONSTRAINT run_inputs_run_fk FOREIGN KEY (run_id)
        REFERENCES {schema}.runs (id) ON DELETE CASCADE
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_inputs_tenant_created_idx' AND object_id = OBJECT_ID(N'{schema}.run_inputs'))
CREATE INDEX run_inputs_tenant_created_idx
    ON {schema}.run_inputs (tenant_id, created_at DESC);

-- Replay lineage. The column is added AT THE END; the reader uses fixed column
-- indexes.
IF COL_LENGTH(N'{schema}.runs', N'replay_of_run_id') IS NULL
ALTER TABLE {schema}.runs ADD replay_of_run_id uniqueidentifier NULL;

-- 🚨 Wrapped in EXEC: replay_of_run_id is added above with ALTER TABLE in the
-- SAME batch; without EXEC it gives "Invalid column name" (see 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_replay_of_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
EXEC(N'CREATE INDEX runs_replay_of_idx
    ON {schema}.runs (tenant_id, replay_of_run_id)
    WHERE replay_of_run_id IS NOT NULL;');

-- Conversation branch pointer.
--
-- 🚨 THERE IS NO foreign key; the rationale and the measured SQL Server limit
-- (error 1785) are written in PostgreSQL 0023. The pointer is only origin
-- information and is never JOINed on any read path.
IF COL_LENGTH(N'{schema}.conversations', N'parent_conversation_id') IS NULL
ALTER TABLE {schema}.conversations ADD parent_conversation_id uniqueidentifier NULL;

IF COL_LENGTH(N'{schema}.conversations', N'branch_from_seq') IS NULL
ALTER TABLE {schema}.conversations ADD branch_from_seq bigint NULL;

-- 🚨 Wrapped in EXEC: parent_conversation_id is added above with ALTER TABLE in
-- the SAME batch; without EXEC it gives "Invalid column name" (see 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'conversations_parent_idx' AND object_id = OBJECT_ID(N'{schema}.conversations'))
EXEC(N'CREATE INDEX conversations_parent_idx
    ON {schema}.conversations (parent_conversation_id)
    WHERE parent_conversation_id IS NOT NULL;');
