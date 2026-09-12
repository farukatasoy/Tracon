-- Phase 15 -- workflows: execution and persistence.

-- NO SEPARATE TABLE IS OPENED for workflow runs. The runs screen, the SSE stream,
-- the tenant filters, the statistics and the waterfall are already built on
-- `runs`; a second record line would double all of them. Two columns separate them.
--
-- `kind` is NOT NULL DEFAULT 0: every existing row is an agent run and the
-- default classifies them correctly.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS kind smallint NOT NULL DEFAULT 0;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS workflow_name text;

-- Partial index: workflow rows are a small percentage of the total. A full index
-- would also make every write of the agent rows more expensive.
CREATE INDEX IF NOT EXISTS runs_workflow_idx
    ON {schema}.runs (tenant_id, workflow_name, started_at DESC)
    WHERE kind = 1;

-- Workflows defined from the UI. `definition` is a graph definition, not code:
-- it carries only the NAMES of the agents in the catalog and a pattern.
-- Workflows defined in code DO NOT LIVE here; they are registered as factories.
--
-- Version HISTORY is not kept (there is no table like agent_definition_versions).
-- A workflow definition is only a name list and a pattern; the information needed
-- to roll back is already in audit_log. An agent definition carries instruction
-- TEXT whose old state cannot be rebuilt anywhere else -- that is the difference.
CREATE TABLE IF NOT EXISTS {schema}.workflows (
    id          uuid        NOT NULL PRIMARY KEY,
    tenant_id   text        NOT NULL,
    name        text        NOT NULL,
    version     integer     NOT NULL,
    definition  jsonb       NOT NULL,
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NOT NULL,
    CONSTRAINT workflows_tenant_name_uq UNIQUE (tenant_id, name)
);

-- 🚨 The `state` column is `json`, it is NOT `jsonb`.
--
-- The checkpoint payload of Microsoft Agent Framework is opaque and polymorphic:
-- it contains the `$type` discriminator of System.Text.Json and that
-- discriminator must be the FIRST property of the object it sits in. PostgreSQL
-- `jsonb` reorders keys first by length then by byte and the discriminator stops
-- being first; the read fails with "The metadata property ... is not the first property".
--
-- Measured (phase 15): in the 7,537 byte checkpoint of a Sequential workflow
-- with three agents the `{"$type":0,...}` discriminator really does appear from
-- byte 1260 onward. This is exactly the fault that K-027 describes.
CREATE TABLE IF NOT EXISTS {schema}.workflow_checkpoints (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    session_id    text        NOT NULL,
    checkpoint_id text        NOT NULL,
    parent_id     text,
    run_id        uuid,
    state         json        NOT NULL,
    created_at    timestamptz NOT NULL,
    CONSTRAINT workflow_checkpoints_uq UNIQUE (tenant_id, session_id, checkpoint_id)
);

CREATE INDEX IF NOT EXISTS workflow_checkpoints_session_idx
    ON {schema}.workflow_checkpoints (tenant_id, session_id, created_at);

CREATE INDEX IF NOT EXISTS workflow_checkpoints_run_idx
    ON {schema}.workflow_checkpoints (tenant_id, run_id, created_at)
    WHERE run_id IS NOT NULL;
