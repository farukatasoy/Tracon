-- Compares the keys a client chooses ordinally.
--
-- 🚨 Until now these columns took the collation of the database. SQL Server's default is
-- case-INsensitive, so two ids that differ only in letter case ("Session-A" and "session-a") were ONE
-- row here, while PostgreSQL and SQLite kept them apart. The second write replaced the first one
-- without a sign (every one of these tables is written by an upsert), and a read by either spelling
-- returned the content of the other.
--
-- The value does not change, the comparison does. Latin1_General_BIN2 compares the code points, as
-- PostgreSQL's "C" collation and the ordinal comparison of .NET do. Values that were unique under the
-- case-insensitive comparison stay unique under the ordinal one (it is the finer of the two), so no
-- row is merged, removed or rewritten.
--
-- One identity, one collation. A session id lives in nine columns, and the retention and erasure
-- queries compare them with each other. SQL Server refuses to compare two columns of different
-- collations ("Cannot resolve the collation conflict"), and a lookup by the one column left
-- case-insensitive would still list the runs of two sessions as one. All nine change together, and so
-- do the request ids a workflow request and its response share.
--
-- The names of definitions (agents, workflows, skills, schedules, and the like) keep the collation of
-- the database. Their columns are not compared with the ones below.
--
-- Every index or constraint that holds one of the columns is dropped, the column changes, and the
-- index or constraint comes back with the definition it had. The runner applies a migration in one
-- transaction, so a failure leaves the old shape. While it runs, each table is locked against
-- schema changes and the columns are rewritten: plan the upgrade of a large runs table for a quiet
-- hour.

-- ---------------------------------------------------------------------------
-- sessions
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'sessions_pk' AND parent_object_id = OBJECT_ID(N'{schema}.sessions'))
ALTER TABLE {schema}.sessions DROP CONSTRAINT sessions_pk;

ALTER TABLE {schema}.sessions ALTER COLUMN id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'sessions_pk' AND parent_object_id = OBJECT_ID(N'{schema}.sessions'))
ALTER TABLE {schema}.sessions ADD CONSTRAINT sessions_pk PRIMARY KEY (tenant_id, id);

-- ---------------------------------------------------------------------------
-- runs
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_session_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
DROP INDEX runs_session_idx ON {schema}.runs;

ALTER TABLE {schema}.runs ALTER COLUMN session_id nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_session_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_session_idx ON {schema}.runs (tenant_id, session_id, started_at DESC) WHERE session_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- attachments
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'attachments_session_idx' AND object_id = OBJECT_ID(N'{schema}.attachments'))
DROP INDEX attachments_session_idx ON {schema}.attachments;

ALTER TABLE {schema}.attachments ALTER COLUMN session_id nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'attachments_session_idx' AND object_id = OBJECT_ID(N'{schema}.attachments'))
CREATE INDEX attachments_session_idx ON {schema}.attachments (tenant_id, session_id) WHERE session_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- pending_approvals, responses, voice_sessions: a session id and no index over it
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.pending_approvals ALTER COLUMN session_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

-- The id of the approval request is the one the run, the stream and the answer name. No index holds it, but a
-- comparison of it with another request id would otherwise stop with the collation conflict.
ALTER TABLE {schema}.pending_approvals ALTER COLUMN request_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.responses ALTER COLUMN session_id nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

ALTER TABLE {schema}.voice_sessions ALTER COLUMN session_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

-- ---------------------------------------------------------------------------
-- workflow_checkpoints
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_checkpoints_session_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_checkpoints'))
DROP INDEX workflow_checkpoints_session_idx ON {schema}.workflow_checkpoints;

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'workflow_checkpoints_uq' AND parent_object_id = OBJECT_ID(N'{schema}.workflow_checkpoints'))
ALTER TABLE {schema}.workflow_checkpoints DROP CONSTRAINT workflow_checkpoints_uq;

ALTER TABLE {schema}.workflow_checkpoints ALTER COLUMN session_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.workflow_checkpoints ALTER COLUMN checkpoint_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

-- A checkpoint id by another name: the parent of a checkpoint is the checkpoint_id of an earlier row.
ALTER TABLE {schema}.workflow_checkpoints ALTER COLUMN parent_id nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'workflow_checkpoints_uq' AND parent_object_id = OBJECT_ID(N'{schema}.workflow_checkpoints'))
ALTER TABLE {schema}.workflow_checkpoints ADD CONSTRAINT workflow_checkpoints_uq UNIQUE (tenant_id, session_id, checkpoint_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_checkpoints_session_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_checkpoints'))
CREATE INDEX workflow_checkpoints_session_idx ON {schema}.workflow_checkpoints (tenant_id, session_id, created_at);

-- ---------------------------------------------------------------------------
-- workflow_requests
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_requests_logical_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_requests'))
DROP INDEX workflow_requests_logical_idx ON {schema}.workflow_requests;

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'workflow_requests_pk' AND parent_object_id = OBJECT_ID(N'{schema}.workflow_requests'))
ALTER TABLE {schema}.workflow_requests DROP CONSTRAINT workflow_requests_pk;

ALTER TABLE {schema}.workflow_requests ALTER COLUMN session_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.workflow_requests ALTER COLUMN request_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'workflow_requests_pk' AND parent_object_id = OBJECT_ID(N'{schema}.workflow_requests'))
ALTER TABLE {schema}.workflow_requests ADD CONSTRAINT workflow_requests_pk PRIMARY KEY (run_id, request_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_requests_logical_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_requests'))
CREATE INDEX workflow_requests_logical_idx ON {schema}.workflow_requests (tenant_id, session_id, request_id, requested_at);

-- ---------------------------------------------------------------------------
-- workflow_responses
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_responses_claim_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_responses'))
DROP INDEX workflow_responses_claim_idx ON {schema}.workflow_responses;

ALTER TABLE {schema}.workflow_responses ALTER COLUMN session_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.workflow_responses ALTER COLUMN request_id nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

ALTER TABLE {schema}.workflow_responses ALTER COLUMN claim_scope nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_responses_claim_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_responses'))
CREATE UNIQUE INDEX workflow_responses_claim_idx ON {schema}.workflow_responses (tenant_id, claim_scope, request_id)
    WHERE claim_scope IS NOT NULL AND status <> 2;

-- ---------------------------------------------------------------------------
-- idempotency_keys
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'idempotency_keys_pk' AND parent_object_id = OBJECT_ID(N'{schema}.idempotency_keys'))
ALTER TABLE {schema}.idempotency_keys DROP CONSTRAINT idempotency_keys_pk;

ALTER TABLE {schema}.idempotency_keys ALTER COLUMN [key] nvarchar(200) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'idempotency_keys_pk' AND parent_object_id = OBJECT_ID(N'{schema}.idempotency_keys'))
ALTER TABLE {schema}.idempotency_keys ADD CONSTRAINT idempotency_keys_pk PRIMARY KEY (tenant_id, [key]);

-- ---------------------------------------------------------------------------
-- agent_files
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'agent_files_tenant_path_idx' AND object_id = OBJECT_ID(N'{schema}.agent_files'))
DROP INDEX agent_files_tenant_path_idx ON {schema}.agent_files;

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_files_tenant_agent_path_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_files'))
ALTER TABLE {schema}.agent_files DROP CONSTRAINT agent_files_tenant_agent_path_uq;

ALTER TABLE {schema}.agent_files ALTER COLUMN path nvarchar(400) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'agent_files_tenant_agent_path_uq' AND parent_object_id = OBJECT_ID(N'{schema}.agent_files'))
ALTER TABLE {schema}.agent_files ADD CONSTRAINT agent_files_tenant_agent_path_uq UNIQUE (tenant_id, agent_name, path);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'agent_files_tenant_path_idx' AND object_id = OBJECT_ID(N'{schema}.agent_files'))
CREATE INDEX agent_files_tenant_path_idx ON {schema}.agent_files (tenant_id, path);

-- ---------------------------------------------------------------------------
-- run_scores
--
-- message_key is computed from message_id and sits in the unique index, so the index and the computed
-- column go first and come back last. The default of name stays: a change of collation does not touch it.
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_target_author_name_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
DROP INDEX run_scores_target_author_name_idx ON {schema}.run_scores;

IF COL_LENGTH(N'{schema}.run_scores', N'message_key') IS NOT NULL
ALTER TABLE {schema}.run_scores DROP COLUMN message_key;

ALTER TABLE {schema}.run_scores ALTER COLUMN author nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

ALTER TABLE {schema}.run_scores ALTER COLUMN message_id nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

ALTER TABLE {schema}.run_scores ALTER COLUMN name nvarchar(64) COLLATE Latin1_General_BIN2 NOT NULL;

IF COL_LENGTH(N'{schema}.run_scores', N'message_key') IS NULL
ALTER TABLE {schema}.run_scores
    ADD message_key AS ISNULL(message_id, N'') PERSISTED;

-- 🚨 Wrapped in EXEC for the reason 0037 gives: message_key is added by the statement above in the
-- SAME batch, and SQL Server compiles the whole batch before it runs any of it.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_target_author_name_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
EXEC(N'CREATE UNIQUE INDEX run_scores_target_author_name_idx
    ON {schema}.run_scores (tenant_id, run_id, message_key, author, name)
    WHERE author IS NOT NULL;');

-- ---------------------------------------------------------------------------
-- rate_limit_counters
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'rate_limit_counters_pk' AND parent_object_id = OBJECT_ID(N'{schema}.rate_limit_counters'))
ALTER TABLE {schema}.rate_limit_counters DROP CONSTRAINT rate_limit_counters_pk;

ALTER TABLE {schema}.rate_limit_counters ALTER COLUMN partition_key nvarchar(400) COLLATE Latin1_General_BIN2 NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'rate_limit_counters_pk' AND parent_object_id = OBJECT_ID(N'{schema}.rate_limit_counters'))
ALTER TABLE {schema}.rate_limit_counters ADD CONSTRAINT rate_limit_counters_pk PRIMARY KEY (partition_key);

-- ---------------------------------------------------------------------------
-- runs_v1
--
-- A view that is not schema-bound keeps the collation its columns had when it was created, so after the
-- change above runs_v1.session_id would still say the collation of the database. Refreshing it makes the
-- metadata that tools and drivers read tell the truth. A query that compares the column with a column of
-- another collation (a join on your own table) still needs COLLATE on one side; that is the same rule as
-- for the tables.
-- ---------------------------------------------------------------------------

IF OBJECT_ID(N'{schema}.runs_v1', N'V') IS NOT NULL
EXEC sp_refreshview N'{schema}.runs_v1';
