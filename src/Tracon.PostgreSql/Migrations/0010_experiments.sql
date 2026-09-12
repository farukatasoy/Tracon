-- Phase 19 -- version comparison, diff and A/B experiments.

-- ---------------------------------------------------------------------------
-- Version and experiment information of runs
-- ---------------------------------------------------------------------------
-- agent_version: the definition version that this run measured. It is also
-- filled for runs outside an experiment (RunRecordingAgentDecorator passes
-- descriptor.Version as the default).
-- experiment_id / variant: filled only on runs that have been assigned by an
-- A/B experiment.

ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS agent_version integer;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS experiment_id uuid;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS variant       text;

-- For the version based breakdown (RunStatistics.ByVersion) and the "how much
-- did this version run" query. Partial index: rows where agent_version is empty
-- (records before phase 19) take no space in the index.
CREATE INDEX IF NOT EXISTS runs_agent_version_idx
    ON {schema}.runs (tenant_id, agent_name, agent_version, started_at DESC)
    WHERE agent_version IS NOT NULL;

-- For the experiment result query (GROUP BY per variant).
CREATE INDEX IF NOT EXISTS runs_experiment_idx
    ON {schema}.runs (experiment_id, variant)
    WHERE experiment_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Experiments
-- ---------------------------------------------------------------------------
-- variants is kept in a single jsonb column: the arms of an experiment are always
-- read and written as a whole (the same reason as EvalSuite.checks). Every element
-- has the form {"name": "...", "version": N, "weight": N}.

CREATE TABLE IF NOT EXISTS {schema}.experiments (
    id             uuid        NOT NULL PRIMARY KEY,
    tenant_id      text        NOT NULL,
    name           text        NOT NULL,
    agent_name     text        NOT NULL,
    variants       jsonb       NOT NULL,
    status         smallint    NOT NULL DEFAULT 0,  -- 0=Draft 1=Running 2=Stopped
    assignment_key text,
    started_at     timestamptz,
    ended_at       timestamptz,
    updated_at     timestamptz NOT NULL,
    CONSTRAINT experiments_tenant_name_uq UNIQUE (tenant_id, name)
);

-- Only ONE Running experiment can exist for the same agent at a time. A partial
-- unique index enforces the rule in the database, not in an application code race.
CREATE UNIQUE INDEX IF NOT EXISTS experiments_running_agent_uq
    ON {schema}.experiments (tenant_id, agent_name)
    WHERE status = 1;

CREATE INDEX IF NOT EXISTS experiments_tenant_agent_idx
    ON {schema}.experiments (tenant_id, agent_name);
