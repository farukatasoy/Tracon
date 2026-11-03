-- Compares the identity of a user, the name of an experiment variant and the actor of an audit entry
-- ordinally.
--
-- 🚨 Until now these columns took the collation of the database. SQL Server's default is case-INsensitive,
-- so two users whose identities differ only in letter case ("Alice" and "alice") were ONE user in every list
-- and every count here, while PostgreSQL, SQLite and the in-memory store kept them apart. The session list
-- of one owner returned the sessions of the other (with their state), the run list and the per-user
-- breakdown merged two people, two variants named "A" and "a" were one arm of an experiment, and the audit
-- list filtered by one actor returned the entries of the other. A read of a single session already compared
-- the owner ordinally, so the same system gave two answers to one question.
--
-- The value does not change, the comparison does. Latin1_General_BIN2 compares the code points, as
-- PostgreSQL's "C" collation and the ordinal comparison of .NET do. No column below is part of a primary
-- key or a unique constraint or index (the indexes that hold them are filtered and not unique), so the
-- comparison being finer can merge nothing: no row is merged, removed or rewritten.
--
-- One identity, one collation. The owner of a session and the user of a run are the same identity and
-- change together; a comparison of two columns of different collations stops with "Cannot resolve the
-- collation conflict".
--
-- Left as it is, on purpose: the owner of a singleton lease, which is the identity of a node that the
-- library writes itself, not a user.
--
-- Every index that holds one of the columns is dropped, the column changes, and the index comes back with
-- the definition it had. The runner applies a migration in one transaction, so a failure leaves the old
-- shape. While it runs, the runs and the sessions tables are locked against schema changes and the columns
-- are rewritten: plan the upgrade of a large database for a quiet hour, and raise the command timeout for
-- the run that applies it.

-- ---------------------------------------------------------------------------
-- sessions.owner_id
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'sessions_tenant_owner_updated_idx' AND object_id = OBJECT_ID(N'{schema}.sessions'))
DROP INDEX sessions_tenant_owner_updated_idx ON {schema}.sessions;

ALTER TABLE {schema}.sessions ALTER COLUMN owner_id nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'sessions_tenant_owner_updated_idx' AND object_id = OBJECT_ID(N'{schema}.sessions'))
CREATE INDEX sessions_tenant_owner_updated_idx
    ON {schema}.sessions (tenant_id, owner_id, updated_at DESC)
    WHERE owner_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- runs.user_id
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_user_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
DROP INDEX runs_tenant_user_idx ON {schema}.runs;

ALTER TABLE {schema}.runs ALTER COLUMN user_id nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_tenant_user_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_tenant_user_idx
    ON {schema}.runs (tenant_id, user_id, started_at DESC)
    WHERE user_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- runs.variant
-- ---------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_experiment_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
DROP INDEX runs_experiment_idx ON {schema}.runs;

ALTER TABLE {schema}.runs ALTER COLUMN variant nvarchar(200) COLLATE Latin1_General_BIN2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_experiment_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
CREATE INDEX runs_experiment_idx
    ON {schema}.runs (experiment_id, variant)
    WHERE experiment_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- audit_log.actor (no index holds it)
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.audit_log ALTER COLUMN actor nvarchar(200) COLLATE Latin1_General_BIN2 NULL;
